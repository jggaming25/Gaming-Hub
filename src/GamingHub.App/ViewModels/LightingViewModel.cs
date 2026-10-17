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

    public ObservableCollection<RgbDevice> Devices { get; } = new();
    public ObservableCollection<RgbProfile> Profiles { get; } = new();
    public ObservableCollection<string> AvailableEffects { get; } = new();

    [ObservableProperty] private RgbDevice? _selectedDevice;
    [ObservableProperty] private RgbColor _selectedColor = new(255, 0, 0);
    [ObservableProperty] private string _selectedEffect = "static";
    [ObservableProperty] private int _effectSpeed = 50;
    [ObservableProperty] private string _newProfileName = "";
    [ObservableProperty] private string _statusText = "Verbinde mit OpenRGB…";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _hasDevices;
    [ObservableProperty] private bool _openRgbNotRunning;

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

        _ = ConnectAsync();
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
    });

    private void OnProfilesChanged() => Dispatcher.UIThread.Post(() =>
    {
        Profiles.Clear();
        foreach (var p in AppServices.Rgb.Profiles)
            Profiles.Add(p);
    });

    private void Refresh()
    {
        IsConnected = AppServices.Rgb.IsConnected;
        HasDevices = Devices.Count > 0;
        OpenRgbNotRunning = !IsConnected && !RgbService.IsServerRunning();

        if (IsConnected)
            StatusText = HasDevices
                ? $"Verbunden mit OpenRGB · {Devices.Count} Gerät(e)"
                : "Verbunden mit OpenRGB – es wurden keine RGB-Geräte gefunden.";
    }

    partial void OnSelectedDeviceChanged(RgbDevice? value)
    {
        OnPropertyChanged(nameof(CanControl));
        AvailableEffects.Clear();

        if (value != null)
        {
            var effects = value.Modes.Count > 0 ? value.Modes : FallbackEffects.ToList();
            foreach (var e in effects) AvailableEffects.Add(e);
            SelectedColor = value.CurrentColor;
            SelectedEffect = AvailableEffects.FirstOrDefault(e =>
                string.Equals(e, value.CurrentEffect, StringComparison.OrdinalIgnoreCase))
                ?? AvailableEffects.FirstOrDefault() ?? "static";
        }
        else
        {
            foreach (var e in FallbackEffects) AvailableEffects.Add(e);
        }
    }

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
        StatusText = "Verbinde mit OpenRGB…";
        var ok = await AppServices.Rgb.ConnectAsync();
        IsConnected = ok;
        HasDevices = Devices.Count > 0;
        OpenRgbNotRunning = !ok && !RgbService.IsServerRunning();
        StatusText = ok
            ? $"Verbunden mit OpenRGB · {Devices.Count} Gerät(e)"
            : "OpenRGB nicht erreichbar. Bitte OpenRGB starten (SDK-Server auf Port 6742).";
    }

    [RelayCommand]
    private async Task StartOpenRgbAsync()
    {
        StatusText = "Starte OpenRGB…";
        var started = RgbService.TryLaunchOpenRgb();
        if (!started)
        {
            StatusText = "OpenRGB wurde nicht gefunden. Bitte installieren: openrgb.org";
            return;
        }

        for (var i = 0; i < 10; i++)
        {
            await Task.Delay(1500);
            if (RgbService.IsServerRunning())
            {
                await ConnectAsync();
                return;
            }
        }
        StatusText = "OpenRGB läuft, SDK-Server aber noch nicht erreichbar.";
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
