using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamingHub.App.Services;
using GamingHub.Core.Services;

namespace GamingHub.App.ViewModels;

public partial class LightingViewModel : ViewModelBase
{
    private static readonly string[] FallbackEffects =
        { "static", "direct", "breathing", "wave", "rainbow", "color_cycle", "strobe", "pulse" };

    private bool _activated;
    private bool _busy;

    public ObservableCollection<RgbDevice> Devices { get; } = new();
    public ObservableCollection<RgbProfile> Profiles { get; } = new();
    public ObservableCollection<string> AvailableEffects { get; } = new();

    [ObservableProperty] private RgbDevice? _selectedDevice;
    [ObservableProperty] private RgbColor _selectedColor = new(255, 0, 0);
    [ObservableProperty] private string _selectedEffect = "static";
    [ObservableProperty] private int _effectSpeed = 50;
    [ObservableProperty] private string _newProfileName = "";
    [ObservableProperty] private string _statusText = "OpenRGB wird gesucht…";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _hasDevices;
    [ObservableProperty] private bool _openRgbNotRunning;
    [ObservableProperty] private byte _colorR = 255;
    [ObservableProperty] private byte _colorG;
    [ObservableProperty] private byte _colorB;
    private bool _syncingColor;

    public string ColorHex => SelectedColor.ToHex();

    partial void OnColorRChanged(byte value) => SyncColorFromChannels();
    partial void OnColorGChanged(byte value) => SyncColorFromChannels();
    partial void OnColorBChanged(byte value) => SyncColorFromChannels();

    private void SyncColorFromChannels()
    {
        if (_syncingColor) return;
        SelectedColor = new RgbColor(ColorR, ColorG, ColorB);
        OnPropertyChanged(nameof(ColorHex));
    }

    private void SetColorChannels(RgbColor color)
    {
        _syncingColor = true;
        ColorR = color.R;
        ColorG = color.G;
        ColorB = color.B;
        _syncingColor = false;
        SelectedColor = color;
        OnPropertyChanged(nameof(ColorHex));
    }

    public bool CanControl => SelectedDevice != null && IsConnected;

    public LightingViewModel()
    {
        AppServices.Rgb.DevicesChanged += OnDevicesChanged;
        AppServices.Rgb.ProfilesChanged += OnProfilesChanged;

        foreach (var e in FallbackEffects) AvailableEffects.Add(e);
        OnProfilesChanged();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) => Refresh();
        timer.Start();

        _ = UpdateOpenRgbAsync();

        // OpenRGB automatisch mit der App einrichten (einmal pro Sitzung).
        Activate();
    }

    /// <summary>Wird aufgerufen, sobald der Beleuchtungs-Tab geöffnet wird.</summary>
    public void Activate()
    {
        if (_activated)
        {
            Refresh();
            return;
        }
        _activated = true;
        _ = EnsureAndLaunchAsync();
    }

    private void OnDevicesChanged() => Dispatcher.UIThread.Post(() =>
    {
        Devices.Clear();
        foreach (var d in AppServices.Rgb.Devices.Values)
            Devices.Add(d);

        HasDevices = Devices.Count > 0;

        if (SelectedDevice != null)
        {
            var match = Devices.FirstOrDefault(d => d.Id == SelectedDevice.Id);
            SelectedDevice = match ?? Devices.FirstOrDefault();
        }
        else
        {
            SelectedDevice = Devices.FirstOrDefault();
        }
    });

    private void OnProfilesChanged() => Dispatcher.UIThread.Post(() =>
    {
        Profiles.Clear();
        foreach (var p in AppServices.Rgb.Profiles)
            Profiles.Add(p);
    });

    private bool _refreshing;

    private async void Refresh()
    {
        if (_busy || _refreshing) return;
        _refreshing = true;
        try
        {
            IsConnected = AppServices.Rgb.IsConnected;
            HasDevices = Devices.Count > 0;

            // IsServerRunning() ist eine blockierende TCP-Prüfung → im Hintergrund.
            bool serverRunning = IsConnected || await Task.Run(() => RgbService.IsServerRunning());
            OpenRgbNotRunning = !IsConnected && !serverRunning;

            if (IsConnected)
                StatusText = HasDevices
                    ? $"Verbunden mit OpenRGB · {Devices.Count} Gerät(e)"
                    : "Verbunden mit OpenRGB – es wurden keine RGB-Geräte gefunden.";
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>Prüft im Hintergrund auf eine neuere OpenRGB-Version (nur wenn bereits installiert).</summary>
    private async Task UpdateOpenRgbAsync()
    {
        try
        {
            var progress = new Progress<string>(s => { if (!_busy) StatusText = s; });
            await AppServices.Rgb.UpdateOpenRgbIfNeededAsync(progress);
            await RefreshConnectionAsync();
        }
        catch { }
    }

    private async Task RefreshConnectionAsync()
    {
        var ok = await AppServices.Rgb.ConnectAsync(launchIfMissing: false);
        IsConnected = ok;
        HasDevices = Devices.Count > 0;
        OpenRgbNotRunning = !ok && !await Task.Run(() => RgbService.IsServerRunning());

        if (ok)
            StatusText = $"Verbunden mit OpenRGB · {Devices.Count} Gerät(e)";
        else if (!AppServices.Rgb.IsOpenRgbInstalled)
            StatusText = "OpenRGB ist noch nicht installiert – wird beim Öffnen dieses Tabs automatisch eingerichtet.";
        else
            StatusText = "OpenRGB ist installiert, läuft aber nicht.";
    }

    /// <summary>Installiert OpenRGB bei Bedarf automatisch und startet es.</summary>
    private async Task EnsureAndLaunchAsync()
    {
        if (AppServices.Rgb.IsConnected) { Refresh(); return; }

        _busy = true;
        try
        {
            if (!await Task.Run(() => RgbService.IsServerRunning()))
            {
                StatusText = "OpenRGB wird vorbereitet…";
                var progress = new Progress<string>(s => StatusText = s);
                var installed = await AppServices.Rgb.EnsureOpenRgbInstalledAsync(progress);

                if (!installed)
                {
                    StatusText = "OpenRGB konnte nicht automatisch installiert werden. Bitte manuell installieren: openrgb.org";
                    return;
                }
            }
        }
        finally
        {
            _busy = false;
        }

        await StartOpenRgbAsync();
    }

    partial void OnSelectedDeviceChanged(RgbDevice? value)
    {        OnPropertyChanged(nameof(CanControl));
        AvailableEffects.Clear();

        if (value != null)
        {
            var effects = value.Modes.Count > 0 ? value.Modes : FallbackEffects.ToList();
            foreach (var e in effects) AvailableEffects.Add(e);
            // Ist die aktuelle Gerätefarbe schwarz (z. B. frischer Direct-Modus ohne
            // Farbe), eine sichtbare Standardfarbe verwenden – sonst würde
            // „Farbe anwenden“ die LEDs ausschalten.
            SelectedColor = IsBlack(value.CurrentColor) ? new RgbColor(255, 0, 0) : value.CurrentColor;
            SetColorChannels(SelectedColor);
            SelectedEffect = AvailableEffects.FirstOrDefault(e =>
                string.Equals(e, value.CurrentEffect, StringComparison.OrdinalIgnoreCase))
                ?? AvailableEffects.FirstOrDefault() ?? "static";
        }
        else
        {
            foreach (var e in FallbackEffects) AvailableEffects.Add(e);
        }
    }

    private static bool IsBlack(RgbColor c) => c.R <= 1 && c.G <= 1 && c.B <= 1;

    [RelayCommand]
    private async Task ApplyColorAsync()
    {
        if (SelectedDevice == null) return;
        StatusText = "Setze Farbe…";
        var ok = await AppServices.Rgb.SetColorAsync(SelectedDevice.Id, SelectedColor);
        StatusText = ok ? "Farbe gesetzt" : "Fehler beim Setzen der Farbe – ist OpenRGB im Direct-Modus?";
        if (ok) OnPropertyChanged(nameof(SelectedDevice));
    }

    [RelayCommand]
    private async Task ApplyPresetAsync(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return;
        SetColorChannels(RgbColor.FromHex(hex));
        await ApplyColorAsync();
    }

    [RelayCommand]
    private async Task ApplyEffectAsync()
    {
        if (SelectedDevice == null) return;
        StatusText = "Setze Effekt…";
        var ok = await AppServices.Rgb.SetEffectAsync(SelectedDevice.Id, SelectedEffect, SelectedColor, EffectSpeed);
        StatusText = ok ? "Effekt gesetzt" : "Dieser Effekt wird vom Gerät nicht unterstützt.";
        if (ok) OnPropertyChanged(nameof(SelectedDevice));
    }

    [RelayCommand]
    private async Task ApplyZoneColorAsync(int zoneIndex)
    {
        if (SelectedDevice == null) return;
        var ok = await AppServices.Rgb.SetZoneColorAsync(SelectedDevice.Id, zoneIndex, SelectedColor);
        StatusText = ok ? $"Zone {zoneIndex} gesetzt" : "Fehler beim Setzen der Zone";
    }

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(NewProfileName)) return;

        var profile = new RgbProfile { Name = NewProfileName, CreatedAt = DateTime.Now };
        foreach (var dev in AppServices.Rgb.Devices.Values)
        {
            profile.DeviceStates[dev.Id] = new DeviceProfileState
            {
                Color = dev.CurrentColor,
                Effect = dev.CurrentEffect,
                EffectColor = dev.CurrentColor,
                Speed = dev.EffectSpeed
            };
        }

        AppServices.Rgb.SaveProfile(profile);
        NewProfileName = "";
        StatusText = $"Profil „{profile.Name}“ gespeichert";
    }

    [RelayCommand]
    private async Task LoadProfileAsync(RgbProfile? profile)
    {
        if (profile == null) return;
        StatusText = $"Lade Profil „{profile.Name}“…";
        var ok = await AppServices.Rgb.ApplyProfileAsync(profile.Id);
        StatusText = ok ? $"Profil „{profile.Name}“ geladen" : "Fehler beim Laden";
    }

    [RelayCommand]
    private void DeleteProfile(RgbProfile? profile)
    {
        if (profile == null) return;
        AppServices.Rgb.DeleteProfile(profile.Id);
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        _busy = true;
        try
        {
            StatusText = "Verbinde mit OpenRGB…";
            var ok = await AppServices.Rgb.ConnectAsync();
            IsConnected = ok;
            HasDevices = Devices.Count > 0;
            OpenRgbNotRunning = !ok && !await Task.Run(() => RgbService.IsServerRunning());
            StatusText = ok
                ? $"Verbunden mit OpenRGB · {Devices.Count} Gerät(e)"
                : "OpenRGB nicht erreichbar. Bitte OpenRGB starten (SDK-Server auf Port 6742).";
        }
        finally
        {
            _busy = false;
        }
    }

    [RelayCommand]
    private async Task StartOpenRgbAsync()
    {
        _busy = true;
        try
        {
            StatusText = "Starte OpenRGB…";
            var started = AppServices.Rgb.StartOrRestartOpenRgb();
            if (!started)
            {
                // Noch keine Installation vorhanden – automatisch herunterladen.
                var progress = new Progress<string>(s => StatusText = s);
                var installed = await AppServices.Rgb.EnsureOpenRgbInstalledAsync(progress);
                if (installed) started = AppServices.Rgb.StartOrRestartOpenRgb();
            }

            if (!started)
            {
                StatusText = "OpenRGB wurde nicht gefunden und konnte nicht installiert werden. Bitte manuell installieren: openrgb.org";
                return;
            }

            for (var i = 0; i < 12; i++)
            {
                await Task.Delay(1500);
                if (await Task.Run(() => RgbService.IsServerRunning())) break;
            }
        }
        finally
        {
            _busy = false;
        }

        await ConnectAsync();
    }

    [RelayCommand]
    private void Disconnect()
    {
        AppServices.Rgb.Disconnect();
        IsConnected = false;
        HasDevices = false;
        StatusText = "Getrennt";
    }
}
