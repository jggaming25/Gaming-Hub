using System.Diagnostics;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamingHub.App.Services;

namespace GamingHub.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty] private string _updateStatus = "Noch nicht geprüft.";
    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private bool _isChecking;

    public string VersionDisplay => AppInfo.VersionDisplay;
    public string DataDirectory => AppServices.Settings.DataDirectory;
    public string RepositoryUrl => AppInfo.RepositoryUrl;

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
        await AppServices.Updater.DownloadAndRestartAsync(p => UpdateStatus = $"Update wird heruntergeladen… {p}%");
    }

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
