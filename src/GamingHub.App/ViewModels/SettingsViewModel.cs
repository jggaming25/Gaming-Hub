using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamingHub.App.Services;

namespace GamingHub.App.ViewModels;

/// <summary>One admin-controlled feature switch (mirrors a row in app_settings).</summary>
public sealed partial class FlagToggle : ObservableObject
{
    private bool _suppress;

    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;

    [ObservableProperty] private bool _value;

    partial void OnValueChanged(bool value)
    {
        if (_suppress) return;
        _ = AppServices.Chat.SetFlagAsync(Key, value);
    }

    public void SetSilently(bool value)
    {
        _suppress = true;
        Value = value;
        _suppress = false;
    }
}

public partial class SettingsViewModel : ViewModelBase
{
    private static readonly (string Key, string Label)[] FlagLabels =
    {
        ("chat_enabled", "Chat erlauben"),
        ("dm_enabled", "Direktnachrichten erlauben"),
        ("voice_enabled", "Sprachanrufe erlauben"),
        ("video_enabled", "Videoanrufe erlauben"),
        ("files_enabled", "Dateiversand erlauben"),
        ("websearch_enabled", "Web-Recherche erlauben"),
        ("cleanup_enabled", "Disk Cleanup erlauben"),
        ("games_enabled", "Spielebibliothek erlauben"),
        ("bugreport_enabled", "Bugreport erlauben")
    };

    public ObservableCollection<FlagToggle> Flags { get; } = new();

    [ObservableProperty] private bool _isAdmin;
    [ObservableProperty] private string _adminStatusText = string.Empty;

    public bool ShowAdminSection => IsAdmin;
    partial void OnIsAdminChanged(bool value) => OnPropertyChanged(nameof(ShowAdminSection));

    [ObservableProperty] private string _updateStatus = "Noch nicht geprüft.";
    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private bool _isChecking;

    public string VersionDisplay => AppInfo.VersionDisplay;
    public string DataDirectory => AppServices.Settings.DataDirectory;
    public string RepositoryUrl => AppInfo.RepositoryUrl;
    public string WebsiteUrl => AppInfo.WebsiteUrl;
    public IReadOnlyList<ChangelogEntry> ChangelogEntries => Changelog.Entries;

    public bool AutoCheckUpdates
    {
        get => AppServices.Settings.Settings.AutoCheckUpdates;
        set { AppServices.Settings.Settings.AutoCheckUpdates = value; AppServices.Settings.Save(); OnPropertyChanged(); }
    }

    public bool AutoInstallUpdates
    {
        get => AppServices.Settings.Settings.AutoInstallUpdates;
        set { AppServices.Settings.Settings.AutoInstallUpdates = value; AppServices.Settings.Save(); OnPropertyChanged(); }
    }

    public bool UseRecycleBin
    {
        get => AppServices.Settings.Settings.UseRecycleBin;
        set { AppServices.Settings.Settings.UseRecycleBin = value; AppServices.Settings.Save(); OnPropertyChanged(); }
    }

    public bool DesktopNotificationsEnabled
    {
        get => AppServices.Settings.Settings.DesktopNotificationsEnabled;
        set { AppServices.Settings.Settings.DesktopNotificationsEnabled = value; AppServices.Settings.Save(); OnPropertyChanged(); }
    }

    public bool NotificationSoundEnabled
    {
        get => AppServices.Settings.Settings.NotificationSoundEnabled;
        set { AppServices.Settings.Settings.NotificationSoundEnabled = value; AppServices.Settings.Save(); OnPropertyChanged(); }
    }

    public bool DarkTheme
    {
        get => AppServices.Settings.Settings.ThemeVariant != "Light";
        set
        {
            AppServices.Settings.Settings.ThemeVariant = value ? "Dark" : "Light";
            AppServices.Settings.Save();
            if (Application.Current is { } app)
                app.RequestedThemeVariant = value ? ThemeVariant.Dark : ThemeVariant.Light;
            OnPropertyChanged();
        }
    }

    [ObservableProperty] private string _supabaseStatusText = string.Empty;

    public string SupabaseUrl
    {
        get => AppServices.Settings.Settings.SupabaseUrl ?? string.Empty;
        set { AppServices.Settings.Settings.SupabaseUrl = value.Trim(); OnPropertyChanged(); }
    }

    public string SupabaseAnonKey
    {
        get => AppServices.Settings.Settings.SupabaseAnonKey ?? string.Empty;
        set { AppServices.Settings.Settings.SupabaseAnonKey = value.Trim(); OnPropertyChanged(); }
    }

    public bool IsSupabaseConfigured => AppServices.Auth.IsConfigured;
    public string SupabaseStatus => AppServices.Auth.IsConfigured
        ? "Verbunden (Standard-Zugang aktiv)"
        : "Nicht konfiguriert";
    public string OAuthRedirectUrl => $"http://localhost:51555/callback";

    public SettingsViewModel()
    {
        UpdateAdminState();
        AppServices.Profile.Changed += () => Dispatcher.UIThread.Post(UpdateAdminState);
        _ = LoadFlagsAsync();
    }

    private void UpdateAdminState()
    {
        IsAdmin = AppServices.Profile?.IsAdmin == true;
        if (IsAdmin && Flags.Count == 0)
            _ = LoadFlagsAsync();
    }

    private async Task LoadFlagsAsync()
    {
        var flags = await AppServices.Chat.LoadFlagsAsync();
        if (flags.Count == 0)
        {
            AdminStatusText = "Keine Schalter gefunden – bitte das Supabase-Schema (app_settings) ausführen.";
            return;
        }
        Flags.Clear();
        foreach (var (key, label) in FlagLabels)
            Flags.Add(new FlagToggle { Key = key, Label = label, Value = !flags.TryGetValue(key, out var v) || v });
        AdminStatusText = "Änderungen werden sofort gespeichert und gelten für alle Nicht-Admins.";
    }

    [RelayCommand]
    private void SaveSupabase()
    {
        AppServices.Settings.Save();
        SupabaseStatusText = "Gespeichert. Bitte Gaming Hub neu starten, damit die Verbindung aktiv wird.";
        OnPropertyChanged(nameof(IsSupabaseConfigured));
        OnPropertyChanged(nameof(SupabaseStatus));
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (IsChecking) return;
        IsChecking = true;
        UpdateStatus = "Suche nach Updates…";
        try
        {
            var res = await AppServices.Updater.CheckForUpdatesAsync();
            if (res.Error is not null)
            {
                UpdateStatus = "Fehler beim Prüfen: " + res.Error.Message;
                UpdateAvailable = false;
            }
            else if (res.HasUpdate)
            {
                UpdateStatus = $"Update {res.Version} verfügbar!";
                UpdateAvailable = true;
            }
            else
            {
                UpdateAvailable = false;
                UpdateStatus = AppServices.Updater.IsInstalled
                    ? "Du bist auf dem neuesten Stand."
                    : "Auto-Update ist nur in der installierten Version aktiv (nicht im Entwickler-Build).";
            }
        }
        finally
        {
            IsChecking = false;
        }
    }

    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        UpdateStatus = "Update wird heruntergeladen…";
        bool ok = await AppServices.Updater.DownloadAndRestartAsync(
            p => UpdateStatus = $"Update wird heruntergeladen… {p}%");
        if (!ok)
            UpdateStatus = "Das Update konnte nicht installiert werden. Details: " + LogService.LogFile;
    }

    public string LogPath => LogService.LogFile;

    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", LogService.LogFolder) { UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    [RelayCommand]
    private void OpenLogFile()
    {
        try
        {
            if (File.Exists(LogService.LogFile))
                Process.Start(new ProcessStartInfo(LogService.LogFile) { UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    [RelayCommand]
    private void ClearLog()
    {
        LogService.Clear();
        AdminStatusText = "Log wurde geleert.";
    }

    [RelayCommand]
    private void OpenWebsite() => Open(WebsiteUrl);

    [RelayCommand]
    private void OpenRepository() => Open(AppInfo.RepositoryUrl);

    [RelayCommand]
    private void OpenReleases() => Open(AppInfo.ReleasesUrl);

    [RelayCommand]
    private void OpenDataFolder()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", DataDirectory) { UseShellExecute = true }); }
        catch { /* ignore */ }
    }

    private static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* ignore */ }
    }
}
