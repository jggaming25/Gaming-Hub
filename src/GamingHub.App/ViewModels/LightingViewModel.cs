using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamingHub.App.Services;
using GamingHub.Core.Models;
using GamingHub.Core.Services;

namespace GamingHub.App.ViewModels;

public partial class LightingViewModel : ViewModelBase
{
    private readonly DispatcherTimer _refreshTimer;

    public ObservableCollection<RgbDevice> Devices { get; } = new();
    public ObservableCollection<RgbProfile> Profiles { get; } = new();

    [ObservableProperty] private RgbDevice? _selectedDevice;
    [ObservableProperty] private RgbColor _selectedColor = new(255, 0, 0);
    [ObservableProperty] private string _selectedEffect = "static";
    [ObservableProperty] private int _effectSpeed = 50;
    [ObservableProperty] private string _newProfileName = "";
    [ObservableProperty] private string _statusText = "Verbinde mit OpenRGB…";
    [ObservableProperty] private bool _isConnected;

    public bool CanControl => SelectedDevice != null && IsConnected;

    public string[] AvailableEffects { get; } = { "static", "breathing", "wave", "rainbow", "color_cycle", "strobe", "pulse" };

    public LightingViewModel()
    {
        AppServices.Rgb.DevicesChanged += OnDevicesChanged;
        AppServices.Rgb.ProfilesChanged += OnProfilesChanged;

        // Timer für Verbindungs-Status & Geräte-Refresh
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += async (_, _) => await RefreshAsync();
        timer.Start();

        _ = RefreshAsync();
    }

    private void OnDevicesChanged() => Dispatcher.UIThread.Post(() =>
    {
        Devices.Clear();
        foreach (var d in AppServices.Rgb.Devices.Values)
            Devices.Add(d);
    });

    private void OnProfilesChanged() => Dispatcher.UIThread.Post(() =>
    {
        Profiles.Clear();
        foreach (var p in AppServices.Rgb.Profiles)
            Profiles.Add(p);
    });

    private async Task RefreshAsync()
    {
        var wasConnected = IsConnected;
        var nowConnected = AppServices.Rgb.IsConnected;
        IsConnected = nowConnected;

        if (nowConnected && !wasConnected)
        {
            StatusText = "Verbunden mit OpenRGB";
        }
        else if (!nowConnected && wasConnected)
        {
            StatusText = "Verbindung verloren – versuche Neuverbindung…";
            await AppServices.Rgb.ConnectAsync();
        }

        if (IsConnected && Devices.Count == 0)
        {
            // Geräte neu laden
            var devices = AppServices.Rgb.Devices.Values.ToList();
            Devices.Clear();
            foreach (var d in devices) Devices.Add(d);
        }
    }

    partial void OnSelectedDeviceChanged(RgbDevice? value)
    {
        OnPropertyChanged(nameof(CanControl));
        if (value != null)
        {
            SelectedColor = value.CurrentColor;
            SelectedEffect = value.CurrentEffect;
        }
    }

    [RelayCommand]
    private async Task ApplyColorAsync()
    {
        if (SelectedDevice == null) return;
        StatusText = "Setze Farbe…";
        var ok = await AppServices.Rgb.SetColorAsync(SelectedDevice.Id, SelectedColor);
        StatusText = ok ? "Farbe gesetzt" : "Fehler beim Setzen";
        if (ok && SelectedDevice != null)
        {
            SelectedDevice.CurrentColor = SelectedColor;
            OnPropertyChanged(nameof(SelectedDevice));
        }
    }

    [RelayCommand]
    private async Task ApplyEffectAsync()
    {
        if (SelectedDevice == null) return;
        StatusText = "Setze Effekt…";
        var ok = await AppServices.Rgb.SetEffectAsync(SelectedDevice.Id, SelectedEffect, SelectedColor, EffectSpeed);
        StatusText = ok ? "Effekt gesetzt" : "Fehler beim Setzen";
        if (ok && SelectedDevice != null)
        {
            SelectedDevice.CurrentEffect = SelectedEffect;
            OnPropertyChanged(nameof(SelectedDevice));
        }
    }

    [RelayCommand]
    private async Task ApplyZoneColorAsync(int zoneIndex)
    {
        if (SelectedDevice == null) return;
        var ok = await AppServices.Rgb.SetZoneColorAsync(SelectedDevice.Id, zoneIndex, SelectedColor);
        StatusText = ok ? $"Zone {zoneIndex} gesetzt" : "Fehler";
    }

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(NewProfileName)) return;

        var profile = new RgbProfile
        {
            Name = NewProfileName,
            CreatedAt = DateTime.Now
        };

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
        StatusText = ok ? "Verbunden mit OpenRGB" : "Verbindung fehlgeschlagen – läuft OpenRGB?";
    }

    [RelayCommand]
    private void Disconnect()
    {
        AppServices.Rgb.Disconnect();
        IsConnected = false;
        StatusText = "Getrennt";
    }
}