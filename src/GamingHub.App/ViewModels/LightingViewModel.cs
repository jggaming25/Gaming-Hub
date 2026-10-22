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

    /// <summary>Lokaler Effekt: wechselt zwischen den beiden gewählten Farben,
    /// ohne einen Gerätemodus zu benötigen.</summary>
    public const string ColorAlternateEffect = "Farbwechsel";

    private bool _activated;
    private bool _activating;
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
    [ObservableProperty] private byte _secondaryR = 0;
    [ObservableProperty] private byte _secondaryG = 128;
    [ObservableProperty] private byte _secondaryB = 255;
    [ObservableProperty] private string _colorHexInput = "#FF0000";
    [ObservableProperty] private string _secondaryColorHexInput = "#0080FF";
    [ObservableProperty] private int _colorMix = 50;
    [ObservableProperty] private bool _mixEnabled;
    [ObservableProperty] private int _alternateSeconds = 10;
    private bool _syncingColor;
    private bool _syncingHex;

    public string ColorHex => SelectedColor.ToHex();
    public string SecondaryColorHex => SecondaryColor.ToHex();
    public RgbColor SecondaryColor => new(SecondaryR, SecondaryG, SecondaryB);
    public RgbColor MixedColor => MixEnabled
        ? RgbColor.Mix(SelectedColor, SecondaryColor, ColorMix / 100.0)
        : SelectedColor;
    public string MixedColorHex => MixedColor.ToHex();

    /// <summary>True, wenn der lokale Farbwechsel als Effekt ausgewählt ist.</summary>
    public bool IsColorAlternateSelected =>
        string.Equals(SelectedEffect, ColorAlternateEffect, StringComparison.OrdinalIgnoreCase);

    /// <summary>Ein Farbwechsel braucht zwei unterschiedliche Farben.</summary>
    public bool HasSecondColor => !string.Equals(SecondaryColorHex, ColorHex, StringComparison.OrdinalIgnoreCase);

    /// <summary>Zweitfarbe anzeigen, wenn gemischt oder ein Farbwechsel gewählt ist.</summary>
    public bool ShowSecondColorControls => MixEnabled || IsColorAlternateSelected;

    partial void OnColorRChanged(byte value) => SyncColorFromChannels();
    partial void OnColorGChanged(byte value) => SyncColorFromChannels();
    partial void OnColorBChanged(byte value) => SyncColorFromChannels();
    partial void OnSecondaryRChanged(byte value) => NotifySecondaryColorChanged();
    partial void OnSecondaryGChanged(byte value) => NotifySecondaryColorChanged();
    partial void OnSecondaryBChanged(byte value) => NotifySecondaryColorChanged();
    partial void OnColorMixChanged(int value) => NotifySecondaryColorChanged();

    partial void OnColorHexInputChanged(string value)
    {
        if (_syncingHex || !TryParseHex(value, out var color)) return;
        if (string.Equals(color.ToHex(), ColorHex, StringComparison.OrdinalIgnoreCase)) return;

        _syncingHex = true;
        try { SetColorChannels(color); }
        finally { _syncingHex = false; }
    }

    partial void OnSecondaryColorHexInputChanged(string value)
    {
        if (_syncingHex || !TryParseHex(value, out var color)) return;
        if (string.Equals(color.ToHex(), SecondaryColorHex, StringComparison.OrdinalIgnoreCase)) return;

        _syncingHex = true;
        try { SetSecondaryChannels(color); }
        finally { _syncingHex = false; }
    }

    partial void OnMixEnabledChanged(bool value)
    {
        NotifySecondaryColorChanged();
        OnPropertyChanged(nameof(ShowSecondColorControls));
    }

    partial void OnSelectedEffectChanged(string value)
    {
        OnPropertyChanged(nameof(IsColorAlternateSelected));
        OnPropertyChanged(nameof(ShowSecondColorControls));
        if (!IsColorAlternateSelected && SelectedDevice is { } device)
            AppServices.Rgb.StopColorAlternate(device.Id);
    }

    partial void OnAlternateSecondsChanged(int value)
    {
        value = Math.Clamp(value, 2, 120);
        if (SelectedDevice is { } device && AppServices.Rgb.IsColorAlternateActive(device.Id))
        {
            AppServices.Rgb.StartColorAlternate(device.Id, SelectedColor, SecondaryColor, value);
            StatusText = $"Farbwechsel aktiv – alle {value} s.";
        }
    }

    private void SyncColorFromChannels()
    {
        if (_syncingColor) return;
        SelectedColor = new RgbColor(ColorR, ColorG, ColorB);
        OnPropertyChanged(nameof(ColorHex));
        SyncColorHexInput();
        NotifySecondaryColorChanged();
    }

    private void NotifySecondaryColorChanged()
    {
        OnPropertyChanged(nameof(SecondaryColor));
        OnPropertyChanged(nameof(SecondaryColorHex));
        SyncSecondaryColorHexInput();
        OnPropertyChanged(nameof(MixedColor));
        OnPropertyChanged(nameof(MixedColorHex));
        OnPropertyChanged(nameof(HasSecondColor));
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
        SyncColorHexInput();
        NotifySecondaryColorChanged();
    }

    private void SetSecondaryChannels(RgbColor color)
    {
        SecondaryR = color.R;
        SecondaryG = color.G;
        SecondaryB = color.B;
        SyncSecondaryColorHexInput();
    }

    private void SyncColorHexInput()
    {
        if (_syncingHex) return;
        _syncingHex = true;
        try { ColorHexInput = ColorHex; }
        finally { _syncingHex = false; }
    }

    private void SyncSecondaryColorHexInput()
    {
        if (_syncingHex) return;
        _syncingHex = true;
        try { SecondaryColorHexInput = SecondaryColorHex; }
        finally { _syncingHex = false; }
    }

    private static bool TryParseHex(string? value, out RgbColor color)
    {
        color = new RgbColor();
        if (string.IsNullOrWhiteSpace(value)) return false;

        var hex = value.Trim();
        if (hex.StartsWith('#')) hex = hex[1..];
        if (hex.Length != 6) return false;

        try
        {
            color = RgbColor.FromHex(hex);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private List<RgbColor> BuildPalette()
    {
        if (!MixEnabled || ColorMix <= 0)
            return new List<RgbColor> { SelectedColor };

        var result = new List<RgbColor>();
        const int steps = 8;
        for (int i = 0; i < steps; i++)
        {
            double t = (double)i / (steps - 1) * (ColorMix / 100.0);
            result.Add(RgbColor.Mix(SelectedColor, SecondaryColor, t));
        }
        return result;
    }

    public bool CanControl => SelectedDevice != null && IsConnected;

    private static bool HasAccess =>
        AppServices.Profile?.IsAdmin == true ||
        (AppServices.Premium.IsPremium && !AppServices.Premium.IsPremiumBanned);

    public LightingViewModel()
    {
        AppServices.Rgb.DevicesChanged += OnDevicesChanged;
        AppServices.Rgb.ProfilesChanged += OnProfilesChanged;
        AppServices.Rgb.ProfileApplied += OnProfileApplied;

        FillEffects(null);
        OnProfilesChanged();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) => Refresh();
        timer.Start();

        // OpenRGB automatisch mit der App einrichten (nur mit aktivem Premium).
        if (HasAccess)
            Activate();
    }

    /// <summary>Wird aufgerufen, sobald der Beleuchtungs-Tab geöffnet wird.</summary>
    public void Activate()
    {
        if (!HasAccess)
            return;

        if (_activated)
        {
            Refresh();
            return;
        }

        _activated = true;
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        if (_activating) return;
        _activating = true;
        try
        {
            // Erst ein eventuell vorhandenes Update abschließen, dann starten und
            // verbinden. Beides gleichzeitig konnte die Geräteliste kurz leeren.
            if (AppServices.Rgb.IsOpenRgbInstalled)
                await UpdateOpenRgbAsync();

            if (!AppServices.Rgb.IsConnected)
                await EnsureAndLaunchAsync();
            else
                await RefreshConnectionAsync();
        }
        finally
        {
            _activating = false;
        }
    }

    private void OnDevicesChanged() => Dispatcher.UIThread.Post(() =>
    {
        var incoming = AppServices.Rgb.Devices.Values.ToList();
        string? selectedId = SelectedDevice?.Id;

        // Bestehende Einträge wiederverwenden, statt die ganze Liste zu leeren.
        // Das verhindert, dass die Auswahl (und damit die Farbe/der Effekt)
        // scheinbar zufällig auf ein anderes Gerät springt.
        for (int i = Devices.Count - 1; i >= 0; i--)
        {
            if (!incoming.Any(d => d.Id == Devices[i].Id))
                Devices.RemoveAt(i);
        }

        foreach (var device in incoming)
        {
            var existing = Devices.FirstOrDefault(d => d.Id == device.Id);
            if (existing is null)
            {
                Devices.Add(device);
                continue;
            }

            existing.Name = device.Name;
            existing.Vendor = device.Vendor;
            existing.Type = device.Type;
            existing.LedCount = device.LedCount;
            existing.CurrentColor = device.CurrentColor;
            existing.CurrentEffect = device.CurrentEffect;
            existing.Modes = device.Modes;
            existing.Zones = device.Zones;
        }

        HasDevices = Devices.Count > 0;

        var match = selectedId is null
            ? null
            : Devices.FirstOrDefault(d => d.Id == selectedId);
        if (match is not null)
            SelectedDevice = match;
        else if (SelectedDevice is null || !Devices.Contains(SelectedDevice))
            SelectedDevice = Devices.FirstOrDefault();
    });

    private void OnProfilesChanged() => Dispatcher.UIThread.Post(() =>
    {
        Profiles.Clear();
        foreach (var p in AppServices.Rgb.Profiles)
            Profiles.Add(p);
    });

    private void OnProfileApplied(RgbProfile profile) => Dispatcher.UIThread.Post(() =>
    {
        if (SelectedDevice is null ||
            !profile.DeviceStates.TryGetValue(SelectedDevice.Id, out var state))
            return;

        if (state.Color is { } primary)
            SetColorChannels(primary);
        if (state.SecondaryColor is { } secondary)
            SetSecondaryChannels(secondary);
        ColorMix = Math.Clamp(state.MixPercent, 0, 100);
        MixEnabled = state.MixPercent > 0;
        if (!string.IsNullOrWhiteSpace(state.Effect))
            SelectedEffect = state.Effect;
        EffectSpeed = state.Speed;
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

            // Wurde OpenRGB zwischenzeitlich gestartet, die Geräte erneut laden.
            if (!IsConnected && serverRunning && !_busy && !_activating)
            {
                if (await AppServices.Rgb.ConnectAsync(launchIfMissing: false))
                {
                    IsConnected = true;
                    HasDevices = Devices.Count > 0;
                }
            }

            // Nur setzen, wenn (noch) keine Aktion-Rückmeldung angezeigt wird –
            // sonst würde der 5-Sekunden-Refresh Meldungen wie „Farbe gesetzt“
            // sofort wieder überschreiben.
            if (IsConnected && string.IsNullOrWhiteSpace(StatusText))
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

    private void FillEffects(RgbDevice? device)
    {
        AvailableEffects.Clear();
        var effects = device?.Modes.Count > 0
            ? device.Modes.ToList()
            : FallbackEffects.ToList();

        if (!effects.Contains(ColorAlternateEffect, StringComparer.OrdinalIgnoreCase))
            effects.Insert(Math.Min(1, effects.Count), ColorAlternateEffect);

        foreach (var e in effects)
            AvailableEffects.Add(e);
    }

    partial void OnSelectedDeviceChanged(RgbDevice? value)
    {
        OnPropertyChanged(nameof(CanControl));
        FillEffects(value);

        if (value != null)
        {
            // Ist die aktuelle Gerätefarbe schwarz (z. B. frischer Direct-Modus ohne
            // Farbe), eine sichtbare Standardfarbe verwenden – sonst würde
            // „Farbe anwenden“ die LEDs ausschalten.
            SelectedColor = IsBlack(value.CurrentColor) ? new RgbColor(255, 0, 0) : value.CurrentColor;
            SetColorChannels(SelectedColor);
            SelectedEffect = AvailableEffects.FirstOrDefault(e =>
                string.Equals(e, value.CurrentEffect, StringComparison.OrdinalIgnoreCase))
                ?? AvailableEffects.FirstOrDefault() ?? "static";
        }
    }

    private static bool IsBlack(RgbColor c) => c.R <= 1 && c.G <= 1 && c.B <= 1;

    [RelayCommand]
    private async Task ApplyColorAsync()
    {
        if (SelectedDevice == null) return;
        AppServices.Rgb.StopColorAlternate(SelectedDevice.Id);
        StatusText = "Setze Farbe…";
        var color = MixedColor;
        var ok = await AppServices.Rgb.SetColorAsync(SelectedDevice.Id, color);
        StatusText = ok
            ? MixEnabled
                ? $"Farbmischung {SelectedColor.ToHex()} + {SecondaryColor.ToHex()} gesetzt"
                : $"Farbe {color.ToHex()} gesetzt"
            : "Fehler beim Setzen der Farbe – ist OpenRGB im Direct-Modus?";
        if (ok)
        {
            AppServices.Rgb.StartColorKeepAlive(SelectedDevice.Id, color);
            OnPropertyChanged(nameof(SelectedDevice));
        }
    }

    [RelayCommand]
    private void ApplyPreset(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return;
        // Nur auswählen – erst „Farbe anwenden“ überträgt sie auf das Gerät.
        SetColorChannels(RgbColor.FromHex(hex));
        StatusText = $"Farbe {ColorHex} ausgewählt – mit „Farbe anwenden“ übernehmen.";
    }

    [RelayCommand]
    private void ApplySecondaryPreset(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return;
        SetSecondaryChannels(RgbColor.FromHex(hex));
        MixEnabled = true;
        StatusText = $"Zweitfarbe {SecondaryColorHex} ausgewählt – mit „Farbe anwenden“ übernehmen.";
    }

    [RelayCommand]
    private async Task ApplyEffectAsync()
    {
        if (SelectedDevice == null) return;

        if (IsColorAlternateSelected)
        {
            if (!HasSecondColor)
            {
                StatusText = "Bitte zuerst eine abweichende Zweitfarbe auswählen.";
                return;
            }

            StatusText = "Starte Farbwechsel…";
            if (!await AppServices.Rgb.SetColorAsync(SelectedDevice.Id, SelectedColor))
            {
                StatusText = "Fehler beim Starten des Farbwechsels – ist OpenRGB im Direct-Modus?";
                return;
            }

            AppServices.Rgb.StartColorKeepAlive(SelectedDevice.Id, SelectedColor);
            AppServices.Rgb.StartColorAlternate(
                SelectedDevice.Id, SelectedColor, SecondaryColor, AlternateSeconds);
            StatusText = $"Farbwechsel aktiv – alle {AlternateSeconds} s zwischen den beiden Farben.";
            return;
        }

        AppServices.Rgb.StopColorAlternate(SelectedDevice.Id);
        StatusText = "Setze Effekt…";
        var ok = await AppServices.Rgb.SetEffectAsync(
            SelectedDevice.Id, SelectedEffect, MixedColor, EffectSpeed, BuildPalette());
        StatusText = ok ? $"Effekt „{SelectedEffect}“ gesetzt" : "Dieser Effekt wird vom Gerät nicht unterstützt.";
        if (ok)
        {
            bool direct = string.Equals(SelectedEffect, "direct", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(SelectedEffect, "static", StringComparison.OrdinalIgnoreCase);
            if (direct)
                AppServices.Rgb.StartColorKeepAlive(SelectedDevice.Id, MixedColor);
            else
                AppServices.Rgb.StopColorKeepAlive(SelectedDevice.Id);

            OnPropertyChanged(nameof(SelectedDevice));
        }
    }

    [RelayCommand]
    private async Task ApplyZoneColorAsync(int zoneIndex)
    {
        if (SelectedDevice == null) return;
        var ok = await AppServices.Rgb.SetZoneColorAsync(SelectedDevice.Id, zoneIndex, SelectedColor);
        StatusText = ok ? $"Zone {zoneIndex} gesetzt" : "Fehler beim Setzen der Zone";
    }

    [RelayCommand]
    private Task SaveProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(NewProfileName))
        {
            StatusText = "Bitte einen Profilnamen eingeben.";
            return Task.CompletedTask;
        }

        var name = NewProfileName.Trim();
        var existing = AppServices.Rgb.Profiles.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        var profile = existing ?? new RgbProfile { Name = name, CreatedAt = DateTime.Now };
        if (existing is not null)
            profile.DeviceStates.Clear();

        foreach (var dev in AppServices.Rgb.Devices.Values)
        {
            bool selected = SelectedDevice?.Id == dev.Id;
            bool keepSecond = selected && (MixEnabled || IsColorAlternateSelected);
            profile.DeviceStates[dev.Id] = new DeviceProfileState
            {
                Color = selected ? SelectedColor : dev.CurrentColor,
                Effect = selected ? SelectedEffect : dev.CurrentEffect,
                EffectColor = selected ? MixedColor : dev.CurrentColor,
                SecondaryColor = keepSecond ? SecondaryColor : null,
                MixPercent = selected && MixEnabled ? ColorMix : 0,
                Colors = selected ? BuildPalette() : new List<RgbColor>(),
                Speed = selected ? EffectSpeed : dev.EffectSpeed,
                AlternateSeconds = selected && IsColorAlternateSelected
                    ? AlternateSeconds
                    : 10
            };
        }

        AppServices.Rgb.SaveProfile(profile);
        NewProfileName = "";
        StatusText = existing is null
            ? $"Profil „{profile.Name}“ gespeichert"
            : $"Profil „{profile.Name}“ aktualisiert";
        return Task.CompletedTask;
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
                : "OpenRGB nicht erreichbar. Bitte „OpenRGB starten“ wählen oder manuell starten.";
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
        AppServices.Rgb.StopColorKeepAlive();
        AppServices.Rgb.StopColorAlternate();
        AppServices.Rgb.Disconnect();
        IsConnected = false;
        HasDevices = false;
        StatusText = "Getrennt";
    }
}
