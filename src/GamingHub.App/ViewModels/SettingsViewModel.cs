using System.Diagnostics;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamingHub.App.Services;

namespace GamingHub.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    public string VersionDisplay => AppInfo.VersionDisplay;
    public string DataDirectory => AppServices.Settings.DataDirectory;
    public string RepositoryUrl => AppInfo.RepositoryUrl;
    public string WebsiteUrl => AppInfo.WebsiteUrl;

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

    [RelayCommand]
    private void SaveSupabase()
    {
        AppServices.Settings.Save();
        SupabaseStatusText = "Gespeichert. Bitte Gaming Hub neu starten, damit die Verbindung aktiv wird.";
        OnPropertyChanged(nameof(IsSupabaseConfigured));
        OnPropertyChanged(nameof(SupabaseStatus));
    }

    [RelayCommand]
    private void OpenWebsite() => Open(WebsiteUrl);

    [RelayCommand]
    private void OpenRepository() => Open(AppInfo.RepositoryUrl);

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
