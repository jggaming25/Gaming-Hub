using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
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
        _ = SetFlagAsync(Key, value);
    }

    private async Task SetFlagAsync(string key, bool value)
    {
        // Could call Supabase RPC here
        // For now just save locally
        AppServices.Settings.Save();
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
        ("websearch_enabled", "Web-Recherche erlauben"),
        ("cleanup_enabled", "Disk Cleanup erlauben"),
        ("games_enabled", "Spielebibliothek erlauben"),
        ("bugreport_enabled", "Bugreport erlauben"),
        ("internet_checker_enabled", "Internet-Checker erlauben")
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
    public string LicenseUrl => AppInfo.LicenseUrl;

    public string ContactName => "Jann Genzmann";
    public string ContactDiscord => "jlg09";
    public string ContactEmail => "genzmannjann@gmail.com";
    public string LicenseText { get; } = LoadLicenseText();

    private static string LoadLicenseText()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("GamingHub.App.LICENSE");
            if (stream is null)
                return "Die Lizenzdatei konnte nicht geladen werden.";
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch
        {
            return "Die Lizenzdatei konnte nicht geladen werden.";
        }
    }
    public IReadOnlyList<ChangelogEntry> ChangelogEntries => Changelog.Entries;

    public bool AutoCheckUpdates
    {
        get => AppServices.Settings.Settings.AutoCheckUpdates;
        set { AppServices.Settings.Settings.AutoCheckUpdates = value; AppServices.Settings.Save(); OnPropertyChanged(); }
    }

    /// <summary>Wenn aktiv, werden gefundene Updates beim Start automatisch im
    /// Ladebildschirm heruntergeladen und installiert (Neustart nötig).</summary>
    public bool AutoInstallUpdates
    {
        get => AppServices.Settings.Settings.AutoInstallUpdates;
        set { AppServices.Settings.Settings.AutoInstallUpdates = value; AppServices.Settings.Save(); OnPropertyChanged(); }
    }

    /// <summary>Startet Gaming Hub beim Windows-Anmelden automatisch und versteckt das
    /// Fenster im System-Tray (die App läuft dann im Hintergrund weiter).
    /// Deaktivieren erfordert eine Bestätigung (Hinweis).</summary>
    public bool AutostartEnabled
    {
        get => AutostartService.IsEnabled();
        set
        {
            if (!value && AutostartService.IsEnabled())
            {
                // Erst nach Bestätigung deaktivieren – Hinweis im Programm anzeigen.
                ShowAutostartConfirm = true;
            }
            else
            {
                AutostartService.SetEnabled(value);
                AppServices.Settings.Settings.AutostartConfigured = true;
                AppServices.Settings.Save();
            }
            OnPropertyChanged();
        }
    }

    [ObservableProperty] private bool _showAutostartConfirm;

    [RelayCommand]
    private void ConfirmDisableAutostart()
    {
        AutostartService.SetEnabled(false);
        AppServices.Settings.Settings.AutostartConfigured = true;
        AppServices.Settings.Save();
        ShowAutostartConfirm = false;
        OnPropertyChanged(nameof(AutostartEnabled));
    }

    [RelayCommand]
    private void CancelDisableAutostart()
    {
        ShowAutostartConfirm = false;
        OnPropertyChanged(nameof(AutostartEnabled));
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

    public string NotificationStatusText => AppServices.Notifications.NativeStatus;
    public bool HasNotificationProblem => AppServices.Notifications.HasNotificationProblem;
    public string NotificationProblemText => AppServices.Notifications.NotificationProblem;

    [RelayCommand]
    private void TestNotification()
    {
        AppServices.Notifications.ShowTest();
        RefreshNotificationStatus();
    }

    [RelayCommand]
    private void RefreshNotificationStatus()
    {
        AppServices.Notifications.RefreshNativeStatus();
        OnPropertyChanged(nameof(NotificationStatusText));
        OnPropertyChanged(nameof(HasNotificationProblem));
        OnPropertyChanged(nameof(NotificationProblemText));
    }

    [RelayCommand]
    private void OpenNotificationSettings()
    {
        AppServices.OpenBrowser(AppServices.Notifications.NotificationSettingsUri);
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
        AppServices.Notifications.RefreshNativeStatus();
        OnPropertyChanged(nameof(NotificationStatusText));
        _ = LoadFlagsAsync();
    }

    private void UpdateAdminState()
    {
        IsAdmin = AppServices.Profile?.IsAdmin == true;
        if (IsAdmin && Flags.Count == 0)
            _ = LoadFlagsAsync();
    }

    private bool _flagsLoading;

    private async Task LoadFlagsAsync()
    {
        // Mehrfache Aufrufe (Konstruktor + Profil-Änderung) dürfen die Liste
        // nicht doppelt befüllen.
        if (_flagsLoading || Flags.Count > 0)
            return;

        _flagsLoading = true;
        try
        {
            Flags.Clear();
            foreach (var (key, label) in FlagLabels)
                Flags.Add(new FlagToggle { Key = key, Label = label, Value = true });

            AdminStatusText = "Änderungen werden sofort gespeichert und gelten für alle Nicht-Admins.";
        }
        catch
        {
            // Bei Fehlern bleibt die Liste leer; kein doppeltes Befüllen.
        }
        finally
        {
            _flagsLoading = false;
        }

        await Task.CompletedTask;
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
    private void OpenContactEmail() =>
        AppServices.OpenEmail(ContactEmail, "Gaming Hub Kontakt");

    [RelayCommand]
    private void OpenLicense() => Open(AppInfo.LicenseUrl);

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
