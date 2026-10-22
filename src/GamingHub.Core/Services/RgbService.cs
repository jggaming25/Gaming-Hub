using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using GamingHub.Core.Models;

namespace GamingHub.Core.Services;

/// <summary>
/// RGB-Service zur Steuerung von Maus, Tastatur, Lüftern, GPU, Mainboard etc.
/// Spricht direkt mit dem binären OpenRGB-SDK (TCP). Gaming Hub startet dafür
/// eine eigene, verwaltete OpenRGB-Instanz auf <see cref="ManagedPort"/> und
/// erkennt darüber alle von OpenRGB unterstützten Geräte. Läuft OpenRGB nicht,
/// kann es über <see cref="TryLaunchOpenRgb"/> gestartet werden.
/// </summary>
public sealed class RgbService : IDisposable
{
    public const string DefaultHost = "127.0.0.1";

    /// <summary>Eigener SDK-Port für die von Gaming Hub verwaltete OpenRGB-Instanz.
    /// Bewusst nicht der Standardport 6742: Dort läuft oft ein fremder
    /// OpenRGB-Dienst/eine fremde Installation, die die Geräte nicht (zuverlässig)
    /// steuern kann. So kollidiert die App nie mit einer vorhandenen OpenRGB-Instanz.</summary>
    public const int ManagedPort = 6790;

    private readonly OpenRgbClient _client = new();
    private readonly OpenRgbInstaller _installer;
    private readonly ConcurrentDictionary<string, RgbDevice> _devices = new();
    private readonly ObservableCollection<RgbProfile> _profiles = new();
    private readonly string _profilesPath;
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    // Manche Geräte (z. B. Corsair K55) verlieren im Direct-Modus nach einigen
    // Sekunden die Farbe, wenn kein neuer Frame gesendet wird. Dieser Keepalive
    // schickt die zuletzt gesetzte Farbe regelmäßig erneut.
    private readonly object _keepAliveLock = new();
    private Timer? _keepAliveTimer;
    private string? _keepAliveDeviceId;
    private RgbColor? _keepAliveColor;
    private bool _keepAliveDisposed;

    public event Action? DevicesChanged;
    public event Action? ProfilesChanged;

    /// <summary>Wird nach dem Laden eines Profils ausgelöst, damit die UI die
    /// gespeicherten Farben/Modi übernehmen kann.</summary>
    public event Action<RgbProfile>? ProfileApplied;

    public IReadOnlyDictionary<string, RgbDevice> Devices => _devices;
    public ReadOnlyObservableCollection<RgbProfile> Profiles { get; }

    public bool IsConnected => _client.IsConnected;
    public int ProtocolVersion => _client.ProtocolVersion;

    /// <summary>Verwaltet die automatische Installation/Aktualisierung von OpenRGB.</summary>
    public OpenRgbInstaller Installer { get; }
    public bool IsOpenRgbInstalled => Installer.IsInstalled;

    public RgbService(string dataDirectory)
    {
        _installer = new OpenRgbInstaller(dataDirectory);
        Installer = _installer;
        _profilesPath = Path.Combine(dataDirectory, "rgb-profiles.json");
        Profiles = new ReadOnlyObservableCollection<RgbProfile>(_profiles);
        LoadProfiles();
    }

    /// <summary>Stellt sicher, dass OpenRGB installiert und aktuell ist (lädt es bei Bedarf herunter).</summary>
    public Task<bool> EnsureOpenRgbInstalledAsync(IProgress<string>? progress = null)
        => _installer.EnsureInstalledAsync(progress);

    /// <summary>Aktualisiert OpenRGB im Hintergrund, falls bereits installiert.</summary>
    public Task UpdateOpenRgbIfNeededAsync(IProgress<string>? progress = null)
        => _installer.UpdateIfInstalledAsync(progress);

    /// <summary>Verbindet zu OpenRGB und lädt die Geräteliste.</summary>
    public async Task<bool> ConnectAsync(string host = DefaultHost, int port = ManagedPort, bool launchIfMissing = true)
    {
        await _connectLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var ok = await Task.Run(() =>
            {
                try
                {
                    _client.Connect(host, port, "GamingHub");
                    var controllers = _client.GetControllers();
                    ApplyControllers(controllers);
                    return true;
                }
                catch
                {
                    return false;
                }
            }).ConfigureAwait(false);

            if (ok) return true;

            if (launchIfMissing)
            {
                // Läuft der eigene OpenRGB-Server noch nicht, die verwaltete
                // Instanz auf dem eigenen Port starten. Eine fremde OpenRGB-
                // Installation/Dienst auf dem Standardport wird nicht angefasst.
                StartOrRestartOpenRgb();

                // OpenRGB braucht einen Moment zum Starten und Erkennen der Geräte.
                for (var attempt = 0; attempt < 12; attempt++)
                {
                    await Task.Delay(1500).ConfigureAwait(false);
                    var retry = await Task.Run(() =>
                    {
                        try
                        {
                            _client.Connect(host, port, "GamingHub");
                            var controllers = _client.GetControllers();
                            ApplyControllers(controllers);
                            return true;
                        }
                        catch { return false; }
                    }).ConfigureAwait(false);
                    if (retry) return true;
                }
            }
            return false;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public void Disconnect()
    {
        _client.Disconnect();
        _devices.Clear();
        DevicesChanged?.Invoke();
    }

    /// <summary>Prüft, ob der eigene OpenRGB-SDK-Server erreichbar ist.</summary>
    public static bool IsServerRunning(string host = DefaultHost, int port = ManagedPort)
    {
        try
        {
            using var tcp = new TcpClient();
            return tcp.ConnectAsync(host, port).Wait(800) && tcp.Connected;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Versucht eine lokale oder verwaltete OpenRGB-Installation zu starten.</summary>
    public bool TryLaunchOpenRgb()
    {
        foreach (var path in EnumerateLaunchCandidates())
        {
            try
            {
                if (File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo(path)
                    {
                        UseShellExecute = true,
                        // Wichtig: Arbeitsverzeichnis explizit auf den OpenRGB-Ordner setzen.
                        // Sonst erbt OpenRGB das Arbeitsverzeichnis der App (…\current) und
                        // hält dieses Verzeichnis offen. Beim Auto-Update scheitert dann der
                        // Verzeichnis-Rename von Velopack (App startet endlos neu).
                        WorkingDirectory = Path.GetDirectoryName(path) ?? AppContext.BaseDirectory,
                        // Wichtig: ohne --server startet OpenRGB den SDK-Server NICHT.
                        // --server-port bindet ihn an unseren eigenen Port, damit eine
                        // evtl. vorhandene fremde OpenRGB-Instanz (Standardport 6742)
                        // nicht angesprochen wird. --startminimized zeigt nur die
                        // minimierte GUI in der Taskleiste.
                        Arguments = $"--server --server-port {ManagedPort} --startminimized"
                    });
                    return true;
                }
            }
            catch
            {
                // nächsten Kandidaten versuchen
            }
        }
        return false;
    }

    /// <summary>Prüft, ob ein OpenRGB-Prozess läuft (unabhängig vom SDK-Server).</summary>
    public static bool IsOpenRgbProcessRunning()
    {
        try
        {
            return Process.GetProcessesByName("OpenRGB").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Startet die von Gaming Hub verwaltete OpenRGB-Instanz mit SDK-Server auf
    /// dem eigenen Port. Eine laufende, von uns verwaltete Instanz (z. B. mit
    /// altem Port) wird beendet, damit nicht zwei Instanzen um dasselbe Gerät
    /// konkurrieren. Fremde OpenRGB-Installationen/-Dienste bleiben unberührt.
    /// </summary>
    public bool StartOrRestartOpenRgb()
    {
        if (IsServerRunning()) return true;

        KillManagedOpenRgbInstances();
        Thread.Sleep(800);
        return TryLaunchOpenRgb();
    }

    /// <summary>Beendet nur OpenRGB-Prozesse aus dem verwalteten Installationsordner.</summary>
    private void KillManagedOpenRgbInstances()
    {
        var managedDir = _installer.ExecutablePath is { } exe ? Path.GetDirectoryName(exe) : null;
        if (managedDir is null) return;

        foreach (var proc in Process.GetProcessesByName("OpenRGB"))
        {
            try
            {
                var path = proc.MainModule?.FileName;
                if (path is not null &&
                    string.Equals(Path.GetDirectoryName(path), managedDir, StringComparison.OrdinalIgnoreCase))
                {
                    proc.Kill();
                    proc.WaitForExit(3000);
                }
            }
            catch
            {
                // Fremde/elevated Prozesse (kein Zugriff) überspringen.
            }
        }
    }

    /// <summary>
    /// Beendet alle laufenden OpenRGB-Prozesse. Wird u. a. vor einem App-Update
    /// aufgerufen, damit OpenRGB das Installationsverzeichnis nicht offen hält
    /// (sonst schlägt der Verzeichnis-Rename von Velopack fehl).
    /// </summary>
    public static void KillOpenRgbProcesses()
    {
        try
        {
            foreach (var proc in Process.GetProcessesByName("OpenRGB"))
            {
                try
                {
                    proc.Kill();
                    proc.WaitForExit(3000);
                }
                catch
                {
                    // nächsten Prozess versuchen
                }
            }
        }
        catch
        {
            // ignorieren
        }
    }

    private IEnumerable<string> EnumerateLaunchCandidates()
    {
        if (_installer.ExecutablePath is { } managed)
            yield return managed;

        foreach (var path in EnumerateOpenRgbPaths())
            yield return path;
    }

    private static IEnumerable<string> EnumerateOpenRgbPaths()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OpenRGB", "OpenRGB.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "OpenRGB", "OpenRGB.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "OpenRGB", "OpenRGB.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OpenRGB", "OpenRGB.exe"),
            Path.Combine(AppContext.BaseDirectory, "OpenRGB", "OpenRGB.exe"),
            Path.Combine(AppContext.BaseDirectory, "OpenRGB.exe")
        };
        return candidates;
    }

    /// <summary>Farbe für ein Gerät setzen (alle LEDs).</summary>
    public async Task<bool> SetColorAsync(string deviceId, RgbColor color)
    {
        if (!_client.IsConnected) return false;
        if (!_devices.TryGetValue(deviceId, out var device)) return false;

        var ok = await Task.Run(() =>
        {
            try
            {
                // SetColor wirkt nur im Direct-/Custom-Modus – sonst bleibt die
                // bisherige Effektfarbe aktiv und der Knopf scheinbar wirkungslos.
                _client.SetCustomMode((uint)device.ControllerIndex);
                _client.SetColor((uint)device.ControllerIndex, device.LedCount, color);
                return true;
            }
            catch { return false; }
        }).ConfigureAwait(false);

        if (ok)
        {
            device.CurrentColor = color;
            device.CurrentEffect = "direct";
            DevicesChanged?.Invoke();
        }
        return ok;
    }

    /// <summary>Hält die zuletzt gesetzte Direct-Farbe aktiv (gegen Farbverlust
    /// nach einigen Sekunden bei manchen Controllern).</summary>
    public void StartColorKeepAlive(string deviceId, RgbColor color)
    {
        lock (_keepAliveLock)
        {
            if (_keepAliveDisposed)
                return;
            _keepAliveDeviceId = deviceId;
            _keepAliveColor = color;
            _keepAliveTimer ??= new Timer(_ => KeepAliveTick(), null,
                TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
        }
    }

    /// <summary>Beendet den Keepalive (z. B. bei einem Hardware-Effekt).</summary>
    public void StopColorKeepAlive()
    {
        lock (_keepAliveLock)
        {
            _keepAliveTimer?.Dispose();
            _keepAliveTimer = null;
            _keepAliveDeviceId = null;
            _keepAliveColor = null;
        }
    }

    private void KeepAliveTick()
    {
        string? deviceId;
        RgbColor? color;
        lock (_keepAliveLock)
        {
            deviceId = _keepAliveDeviceId;
            color = _keepAliveColor;
        }

        if (deviceId is null || color is null || !_client.IsConnected)
            return;
        if (!_devices.TryGetValue(deviceId, out var device))
            return;

        try
        {
            _client.SetCustomMode((uint)device.ControllerIndex);
            _client.SetColor((uint)device.ControllerIndex, device.LedCount, color);
            device.CurrentColor = color;
            device.CurrentEffect = "direct";
        }
        catch
        {
            // Beim nächsten Tick erneut versuchen.
        }
    }

    /// <summary>Effekt (Modus) setzen. <paramref name="colors"/> erlaubt eine
    /// Farbpalette (z. B. Farbverlauf aus zwei gemischten Farben); ohne Palette
    /// wird die einzelne <paramref name="color"/> verwendet.</summary>
    public async Task<bool> SetEffectAsync(string deviceId, string effectName, RgbColor? color = null, int speed = 50,
        IReadOnlyList<RgbColor>? colors = null)
    {
        if (!_client.IsConnected) return false;
        if (!_devices.TryGetValue(deviceId, out var device)) return false;

        var palette = colors?.Where(c => c != null).ToList() ?? new List<RgbColor>();

        var ok = await Task.Run(() =>
        {
            try
            {
                if (string.Equals(effectName, "static", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(effectName, "direct", StringComparison.OrdinalIgnoreCase))
                {
                    _client.SetCustomMode((uint)device.ControllerIndex);
                    var direct = color ?? palette.FirstOrDefault();
                    if (direct is { } c) _client.SetColor((uint)device.ControllerIndex, device.LedCount, c);
                    return true;
                }

                var mode = device.ModeDescriptions
                    .FirstOrDefault(m => string.Equals(m.Name, effectName, StringComparison.OrdinalIgnoreCase));
                if (mode == null) return false;

                mode.Speed = (uint)Math.Clamp(speed, 0, 100);

                // Modi, die keine Farben annehmen (z. B. fester Regenbogen:
                // ColorsMax == 0), dürfen keine Farbslots erhalten – sonst
                // lehnt OpenRGB den Modus ab und die Farbe wird nicht übernommen.
                if (mode.ColorsMax >= 1 && palette.Count > 0)
                {
                    int max = (int)Math.Clamp(mode.ColorsMax, 1, 64);
                    int min = (int)Math.Clamp(mode.ColorsMin, 1, max);
                    int slots = Math.Clamp(palette.Count, min, max);
                    mode.Colors.Clear();
                    if (slots == 1)
                    {
                        // Einfarbige Modi (Static/Breathing) erhalten genau die
                        // gewählte (ggf. gemischte) Farbe.
                        mode.Colors.Add(color ?? palette[0]);
                    }
                    else
                    {
                        // Mehrfarbige Modi (Wave, Color Pulse o. ä.) bekommen
                        // einen gleichmäßigen Verlauf über die Farbpalette.
                        for (int i = 0; i < slots; i++)
                        {
                            int idx = (int)Math.Round((double)i * (palette.Count - 1) / (slots - 1));
                            mode.Colors.Add(palette[Math.Clamp(idx, 0, palette.Count - 1)]);
                        }
                    }
                }
                else if (color is { } chosen && mode.ColorsMax >= 1)
                {
                    if (mode.Colors.Count > 0)
                        for (var i = 0; i < mode.Colors.Count; i++) mode.Colors[i] = chosen;
                    else
                        mode.Colors.Add(chosen);
                }

                _client.SetMode((uint)device.ControllerIndex, mode);
                return true;
            }
            catch { return false; }
        }).ConfigureAwait(false);

        if (ok)
        {
            device.CurrentEffect = effectName;
            if (color is { } cc) device.CurrentColor = cc;
            DevicesChanged?.Invoke();
        }
        return ok;
    }

    /// <summary>Zone-spezifische Farbe setzen.</summary>
    public async Task<bool> SetZoneColorAsync(string deviceId, int zoneIndex, RgbColor color)
    {
        if (!_client.IsConnected) return false;
        if (!_devices.TryGetValue(deviceId, out var device)) return false;

        var zone = device.Zones.FirstOrDefault(z => z.Index == zoneIndex);
        if (zone == null) return false;

        return await Task.Run(() =>
        {
            try
            {
                _client.SetCustomMode((uint)device.ControllerIndex);
                _client.SetZoneColor((uint)device.ControllerIndex, (uint)zoneIndex, zone.LedCount, color);
                return true;
            }
            catch { return false; }
        }).ConfigureAwait(false);
    }

    /// <summary>Direkt auf Custom/Direct-Modus umschalten (für freie Farbwahl).</summary>
    public async Task<bool> SetCustomModeAsync(string deviceId)
    {
        if (!_client.IsConnected) return false;
        if (!_devices.TryGetValue(deviceId, out var device)) return false;
        return await Task.Run(() =>
        {
            try { _client.SetCustomMode((uint)device.ControllerIndex); return true; }
            catch { return false; }
        }).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------
    // Geräte-Mapping
    // ---------------------------------------------------------------

    private void ApplyControllers(List<OpenRgbController> controllers)
    {
        _devices.Clear();
        foreach (var c in controllers)
        {
            var device = new RgbDevice
            {
                Id = c.Index.ToString(),
                ControllerIndex = (int)c.Index,
                Name = string.IsNullOrWhiteSpace(c.Name) ? $"Gerät {c.Index}" : c.Name,
                Vendor = c.Vendor,
                Type = MapType(c.Type),
                LedCount = c.LedCount,
                ModeDescriptions = c.Modes,
                Modes = c.Modes.Select(m => m.Name).ToList(),
                Zones = c.Zones.Select((z, i) => new RgbZone
                {
                    Index = i,
                    Name = string.IsNullOrWhiteSpace(z.Name) ? $"Zone {i + 1}" : z.Name,
                    LedCount = z.LedCount
                }).ToList()
            };

            if (c.ActiveMode >= 0 && c.ActiveMode < c.Modes.Count)
            {
                var active = c.Modes[c.ActiveMode];
                device.CurrentEffect = active.Name;
                if (active.Colors.Count > 0) device.CurrentColor = active.Colors[0];
            }

            _devices[device.Id] = device;
        }
        DevicesChanged?.Invoke();
    }

    private static RgbDeviceType MapType(int type) => type switch
    {
        0 => RgbDeviceType.Motherboard,
        1 => RgbDeviceType.Ram,
        2 => RgbDeviceType.Gpu,
        3 => RgbDeviceType.Fan,
        4 => RgbDeviceType.LedStrip,
        5 => RgbDeviceType.Keyboard,
        6 => RgbDeviceType.Mouse,
        7 => RgbDeviceType.Mouse, // Mauspad
        15 => RgbDeviceType.Fan,  // Gehäuse
        _ => RgbDeviceType.Other
    };

    // ---------------------------------------------------------------
    // Profile
    // ---------------------------------------------------------------

    public void SaveProfile(RgbProfile profile)
    {
        var existing = _profiles.FirstOrDefault(p => p.Id == profile.Id);
        if (existing != null)
        {
            var idx = _profiles.IndexOf(existing);
            _profiles[idx] = profile;
        }
        else
        {
            _profiles.Add(profile);
        }
        PersistProfiles();
        ProfilesChanged?.Invoke();
    }

    public async Task<bool> ApplyProfileAsync(string profileId)
    {
        var profile = _profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile == null) return false;

        foreach (var (deviceId, state) in profile.DeviceStates)
        {
            var primary = state.Color ?? state.EffectColor;
            var mixed = primary is not null && state.SecondaryColor is not null && state.MixPercent > 0
                ? RgbColor.Mix(primary, state.SecondaryColor, state.MixPercent / 100.0)
                : primary;
            var colors = state.Colors.Count > 0
                ? state.Colors
                : BuildPalette(primary, state.SecondaryColor, state.MixPercent);

            if (string.Equals(state.Effect, "Farbwechsel", StringComparison.OrdinalIgnoreCase))
            {
                // Lokaler Farbwechsel: Farbe setzen; das Abwechseln übernimmt die UI.
                if (primary is { } alternateColor)
                    await SetColorAsync(deviceId, alternateColor).ConfigureAwait(false);
            }
            else if (state.Effect is { } e)
                await SetEffectAsync(deviceId, e, mixed, state.Speed, colors).ConfigureAwait(false);
            else if (state.Color is { } c)
                await SetColorAsync(deviceId, c).ConfigureAwait(false);
        }

        ProfileApplied?.Invoke(profile);
        return true;
    }

    private static List<RgbColor> BuildPalette(RgbColor? primary, RgbColor? secondary, int mixPercent)
    {
        if (primary is null)
            return new List<RgbColor>();
        if (secondary is null || mixPercent <= 0)
            return new List<RgbColor> { primary };

        var result = new List<RgbColor>();
        const int steps = 8;
        for (int i = 0; i < steps; i++)
        {
            double t = (double)i / (steps - 1);
            result.Add(RgbColor.Mix(primary, secondary, t * Math.Clamp(mixPercent, 0, 100) / 100.0));
        }
        return result;
    }

    public void DeleteProfile(string profileId)
    {
        var p = _profiles.FirstOrDefault(x => x.Id == profileId);
        if (p != null)
        {
            _profiles.Remove(p);
            PersistProfiles();
            ProfilesChanged?.Invoke();
        }
    }

    private void LoadProfiles()
    {
        try
        {
            if (File.Exists(_profilesPath))
            {
                var list = JsonSerializer.Deserialize<List<RgbProfile>>(File.ReadAllText(_profilesPath)) ?? new();
                foreach (var p in list) _profiles.Add(p);
            }
        }
        catch { }
    }

    private void PersistProfiles()
    {
        try
        {
            File.WriteAllText(_profilesPath,
                JsonSerializer.Serialize(_profiles.ToList(), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public void Dispose()
    {
        lock (_keepAliveLock)
        {
            _keepAliveDisposed = true;
            _keepAliveTimer?.Dispose();
            _keepAliveTimer = null;
        }
        _client.Dispose();
    }
}

public enum RgbDeviceType
{
    Mouse,
    Keyboard,
    Fan,
    Gpu,
    Motherboard,
    Ram,
    LedStrip,
    Other
}

public sealed class RgbDevice
{
    public string Id { get; set; } = "";
    public int ControllerIndex { get; set; }
    public string Name { get; set; } = "";
    public string Vendor { get; set; } = "";
    public RgbDeviceType Type { get; set; }
    public int LedCount { get; set; }
    public List<RgbZone> Zones { get; set; } = new();
    public List<string> Modes { get; set; } = new();
    public RgbColor CurrentColor { get; set; } = new(255, 255, 255);
    public string CurrentEffect { get; set; } = "static";
    public int EffectSpeed { get; set; } = 50;

    [System.Text.Json.Serialization.JsonIgnore]
    internal List<OpenRgbMode> ModeDescriptions { get; set; } = new();
}

public sealed class RgbZone
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public int LedCount { get; set; }
}

public sealed class RgbColor
{
    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }
    public byte A { get; set; } = 255;

    public RgbColor() { }
    public RgbColor(byte r, byte g, byte b, byte a = 255) { R = r; G = g; B = b; A = a; }

    public static RgbColor FromHex(string hex)
    {
        hex = hex.TrimStart('#');
        return new RgbColor(
            byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber),
            byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber),
            byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber)
        );
    }

    /// <summary>Mischt zwei Farben linear. <paramref name="amount"/> 0 = a, 1 = b.</summary>
    public static RgbColor Mix(RgbColor a, RgbColor b, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return new RgbColor(
            (byte)Math.Round(a.R + (b.R - a.R) * amount),
            (byte)Math.Round(a.G + (b.G - a.G) * amount),
            (byte)Math.Round(a.B + (b.B - a.B) * amount),
            (byte)Math.Round(a.A + (b.A - a.A) * amount));
    }

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";
}

public sealed class RgbProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public Dictionary<string, DeviceProfileState> DeviceStates { get; set; } = new();
}

public sealed class DeviceProfileState
{
    public RgbColor? Color { get; set; }
    public string? Effect { get; set; }
    public RgbColor? EffectColor { get; set; }
    public RgbColor? SecondaryColor { get; set; }
    public int MixPercent { get; set; }
    public List<RgbColor> Colors { get; set; } = new();
    public int Speed { get; set; } = 50;
    public Dictionary<int, RgbColor> ZoneColors { get; set; } = new();
}
