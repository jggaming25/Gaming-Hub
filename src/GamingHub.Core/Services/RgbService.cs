using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using GamingHub.Core.Models;

namespace GamingHub.Core.Services;

/// <summary>
/// RGB-Service zur Steuerung von Maus, Tastatur, Lüftern, GPU, Mainboard etc.
/// Spricht direkt mit dem binären OpenRGB-SDK (TCP, Standardport 6742) und
/// erkennt darüber alle von OpenRGB unterstützten Geräte. Läuft OpenRGB nicht,
/// kann es über <see cref="TryLaunchOpenRgb"/> gestartet werden.
/// </summary>
public sealed class RgbService : IDisposable
{
    public const string DefaultHost = "127.0.0.1";
    public const int DefaultPort = 6742;

    private readonly OpenRgbClient _client = new();
    private readonly OpenRgbInstaller _installer;
    private readonly ConcurrentDictionary<string, RgbDevice> _devices = new();
    private readonly ObservableCollection<RgbProfile> _profiles = new();
    private readonly string _profilesPath;
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    public event Action? DevicesChanged;
    public event Action? ProfilesChanged;

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
    public async Task<bool> ConnectAsync(string host = DefaultHost, int port = DefaultPort, bool launchIfMissing = true)
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

            if (launchIfMissing && TryLaunchOpenRgb())
            {
                // OpenRGB braucht einen Moment zum Starten und Erkennen der Geräte.
                for (var attempt = 0; attempt < 8; attempt++)
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

    /// <summary>Prüft, ob der OpenRGB-SDK-Server erreichbar ist.</summary>
    public static bool IsServerRunning(string host = DefaultHost, int port = DefaultPort)
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
                        Arguments = "--startminimized"
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
                _client.SetColor((uint)device.ControllerIndex, device.LedCount, color);
                return true;
            }
            catch { return false; }
        }).ConfigureAwait(false);

        if (ok)
        {
            device.CurrentColor = color;
            DevicesChanged?.Invoke();
        }
        return ok;
    }

    /// <summary>Effekt (Modus) setzen.</summary>
    public async Task<bool> SetEffectAsync(string deviceId, string effectName, RgbColor? color = null, int speed = 50)
    {
        if (!_client.IsConnected) return false;
        if (!_devices.TryGetValue(deviceId, out var device)) return false;

        var ok = await Task.Run(() =>
        {
            try
            {
                if (string.Equals(effectName, "static", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(effectName, "direct", StringComparison.OrdinalIgnoreCase))
                {
                    _client.SetCustomMode((uint)device.ControllerIndex);
                    if (color is { } c) _client.SetColor((uint)device.ControllerIndex, device.LedCount, c);
                    return true;
                }

                var mode = device.ModeDescriptions
                    .FirstOrDefault(m => string.Equals(m.Name, effectName, StringComparison.OrdinalIgnoreCase));
                if (mode == null) return false;

                mode.Speed = (uint)Math.Clamp(speed, 0, 100);
                if (color is { } chosen)
                {
                    if (mode.Colors.Count > 0)
                        for (var i = 0; i < mode.Colors.Count; i++) mode.Colors[i] = chosen;
                    else if (mode.ColorsMax >= 1 || mode.ColorsMin >= 1)
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
            if (state.Effect is { } e) await SetEffectAsync(deviceId, e, state.Color ?? state.EffectColor, state.Speed).ConfigureAwait(false);
            else if (state.Color is { } c) await SetColorAsync(deviceId, c).ConfigureAwait(false);
        }
        return true;
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

    public void Dispose() => _client.Dispose();
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
    public int Speed { get; set; } = 50;
    public Dictionary<int, RgbColor> ZoneColors { get; set; } = new();
}
