using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using GamingHub.Core.Models;

namespace GamingHub.Core.Services;

/// <summary>
/// RGB-Service zur Steuerung von Maus, Tastatur, Lüftern, GPU, Mainboard etc.
/// Nutzt OpenRGB (WebSocket API) als primäres Backend, fallback auf WMI/Windows APIs.
/// </summary>
public sealed class RgbService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private ClientWebSocket? _ws;
    private readonly ConcurrentDictionary<string, RgbDevice> _devices = new();
    private readonly ObservableCollection<RgbProfile> _profiles = new();
    private readonly string _profilesPath;

    public event Action? DevicesChanged;
    public event Action? ProfilesChanged;

    public IReadOnlyDictionary<string, RgbDevice> Devices => _devices;
    public ReadOnlyObservableCollection<RgbProfile> Profiles { get; }

    public bool IsConnected => _ws?.State == WebSocketState.Open;

    public RgbService(string dataDirectory)
    {
        _profilesPath = Path.Combine(dataDirectory, "rgb-profiles.json");
        Profiles = new ReadOnlyObservableCollection<RgbProfile>(_profiles);
        LoadProfiles();
    }

    /// <summary>Startet Verbindung zu OpenRGB (läuft auf localhost:6742).</summary>
    public async Task<bool> ConnectAsync(string host = "127.0.0.1", int port = 6742)
    {
        try
        {
            _ws = new ClientWebSocket();
            await _ws.ConnectAsync(new Uri($"ws://{host}:{port}"), CancellationToken.None);
            _ = ReceiveLoopAsync();
            await RequestDevicesAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Disconnect()
    {
        _ws?.Dispose();
        _ws = null;
    }

    /// <summary>Farbe für ein Gerät setzen (alle Zonen).</summary>
    public async Task<bool> SetColorAsync(string deviceId, RgbColor color)
    {
        if (_ws?.State != WebSocketState.Open) return false;

        var msg = new
        {
            command = "set_color",
            device_id = deviceId,
            color = new { r = color.R, g = color.G, b = color.B }
        };
        return await SendAsync(msg);
    }

    /// <summary>Effekt setzen (Static, Breathing, Wave, Rainbow, etc.).</summary>
    public async Task<bool> SetEffectAsync(string deviceId, string effectName, RgbColor? color = null, int speed = 50)
    {
        if (_ws?.State != WebSocketState.Open) return false;

        var msg = new
        {
            command = "set_mode",
            device_id = deviceId,
            mode = effectName,
            color = color != null ? new { r = color.R, g = color.G, b = color.B } : null,
            speed
        };
        return await SendAsync(msg);
    }

    /// <summary>Zone-spezifische Farbe setzen.</summary>
    public async Task<bool> SetZoneColorAsync(string deviceId, int zoneIndex, RgbColor color)
    {
        if (_ws?.State != WebSocketState.Open) return false;

        var msg = new
        {
            command = "set_zone_color",
            device_id = deviceId,
            zone = zoneIndex,
            color = new { r = color.R, g = color.G, b = color.B }
        };
        return await SendAsync(msg);
    }

    /// <summary>Profil speichern.</summary>
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

    /// <summary>Profil laden & anwenden.</summary>
    public async Task<bool> ApplyProfileAsync(string profileId)
    {
        var profile = _profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile == null) return false;

        foreach (var (deviceId, state) in profile.DeviceStates)
        {
            if (state.Color is { } c) await SetColorAsync(deviceId, c);
            if (state.Effect is { } e) await SetEffectAsync(deviceId, e, state.Color, state.Speed);
        }
        return true;
    }

    /// <summary>Profil löschen.</summary>
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

    // --- Internals ---

    private async Task RequestDevicesAsync()
    {
        await SendAsync(new { command = "get_devices" });
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[8192];
        try
        {
            while (_ws?.State == WebSocketState.Open)
            {
                var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) break;

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                HandleMessage(json);
            }
        }
        catch { }
    }

    private void HandleMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("command", out var cmd) && cmd.GetString() == "devices_list")
            {
                if (root.TryGetProperty("devices", out var devices))
                {
                    foreach (var dev in devices.EnumerateArray())
                    {
                        var device = ParseDevice(dev);
                        _devices[device.Id] = device;
                    }
                    DevicesChanged?.Invoke();
                }
            }
        }
        catch { }
    }

    private static RgbDevice ParseDevice(JsonElement dev)
    {
        var id = dev.GetProperty("id").GetString() ?? "";
        var name = dev.GetProperty("name").GetString() ?? "";
        var type = Enum.TryParse<RgbDeviceType>(dev.GetProperty("type").GetString(), true, out var t) ? t : RgbDeviceType.Other;

        var zones = new List<RgbZone>();
        if (dev.TryGetProperty("zones", out var zonesEl))
        {
            foreach (var z in zonesEl.EnumerateArray())
            {
                zones.Add(new RgbZone
                {
                    Index = z.GetProperty("index").GetInt32(),
                    Name = z.GetProperty("name").GetString() ?? "",
                    LedCount = z.GetProperty("leds_count").GetInt32()
                });
            }
        }

        return new RgbDevice
        {
            Id = id,
            Name = name,
            Type = type,
            Zones = zones
        };
    }

    private async Task<bool> SendAsync(object msg)
    {
        if (_ws?.State != WebSocketState.Open) return false;
        try
        {
            var json = JsonSerializer.Serialize(msg);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            return true;
        }
        catch { return false; }
    }

    private void LoadProfiles()
    {
        try
        {
            if (File.Exists(_profilesPath))
            {
                var json = File.ReadAllText(_profilesPath);
                var list = JsonSerializer.Deserialize<List<RgbProfile>>(json) ?? new();
                foreach (var p in list) _profiles.Add(p);
            }
        }
        catch { }
    }

    private void PersistProfiles()
    {
        try
        {
            var json = JsonSerializer.Serialize(_profiles.ToList(), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_profilesPath, json);
        }
        catch { }
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
    public string Name { get; set; } = "";
    public RgbDeviceType Type { get; set; }
    public List<RgbZone> Zones { get; set; } = new();
    public RgbColor CurrentColor { get; set; } = new(255, 255, 255);
    public string CurrentEffect { get; set; } = "static";
    public int EffectSpeed { get; set; } = 50;
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