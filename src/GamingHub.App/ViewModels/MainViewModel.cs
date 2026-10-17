using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamingHub.App.Services;

namespace GamingHub.App.ViewModels;

public sealed class NavItem
{
    public string Title { get; init; } = string.Empty;
    public string Glyph { get; init; } = string.Empty;
    public string? FlagKey { get; init; }
    public ViewModelBase ViewModel { get; init; } = null!;
}

public partial class MainViewModel : ViewModelBase
{
    private readonly List<NavItem> _allNav = new();
    private NavItem? _lastSelected;

    public ObservableCollection<NavItem> NavItems { get; } = new();

    [ObservableProperty] private NavItem? _selectedNav;
    [ObservableProperty] private ViewModelBase? _selectedView;

    public string AppName => AppInfo.AppName;
    public string Tagline => AppInfo.Tagline;
    public string VersionDisplay => AppInfo.VersionDisplay;

    public bool HasUpdate => AppServices.Updater.LastCheck?.HasUpdate == true;
    public string? UpdateVersion => AppServices.Updater.LastCheck?.Version;

    [ObservableProperty] private string _updateStatusText = string.Empty;
    public bool IsInstallingUpdate { get; private set; }

    public string UpdateBannerText => HasUpdate && !RestartPending
        ? $"Update {UpdateVersion} verfügbar – klicken zum Installieren"
        : string.Empty;

    public bool ShowUpdateBanner => HasUpdate && !RestartPending && !IsInstallingUpdate;
    public bool ShowUpdateInstalling => IsInstallingUpdate;

    /// <summary>Ein Update wurde bereits installiert und wartet auf den Neustart.</summary>
    [ObservableProperty] private bool _restartPending;

    /// <summary>Popup „Update installiert – Neustart erforderlich“ im Hauptfenster.</summary>
    [ObservableProperty] private bool _showRestartPopup;

    public InternetCheckerViewModel InternetCheckerViewModel { get; }

    /// <summary>Offline-Hinweis in der Sidebar (wenn Internet weg, aber App läuft).</summary>
    public bool ShowOfflineHint => AppServices.Internet.OfflineMode;

    public string RestartBannerText => RestartPending
        ? $"Neu starten, um Version {AppServices.Updater.PendingVersion ?? UpdateVersion} zu aktivieren"
        : string.Empty;

    public bool ShowRestartBanner => RestartPending && !ShowRestartPopup && !IsInstallingUpdate;

    public string RestartPopupText => $"Das Update {AppServices.Updater.PendingVersion ?? UpdateVersion} wurde bereits installiert.\n\n" +
                                          "Für die neue Version ist ein Neustart von Gaming Hub erforderlich.\n\n" +
                                          "Möchtest du jetzt neu starten?";

    public string RestartPopupTitle => $"Update {AppServices.Updater.PendingVersion ?? UpdateVersion} installiert";

    partial void OnRestartPendingChanged(bool value)
    {
        OnPropertyChanged(nameof(UpdateBannerText));
        OnPropertyChanged(nameof(ShowUpdateBanner));
        OnPropertyChanged(nameof(RestartBannerText));
        OnPropertyChanged(nameof(ShowRestartBanner));
    }

    partial void OnShowRestartPopupChanged(bool value) => OnPropertyChanged(nameof(ShowRestartBanner));

    partial void OnUpdateStatusTextChanged(string value) => OnPropertyChanged(nameof(UpdateBannerText));

    [RelayCommand]
    private void RestartNow()
    {
        if (IsInstallingUpdate) return;
        try
        {
            AppServices.Updater.RestartToApply();
        }
        catch (Exception ex)
        {
            LogService.Error("restart-to-update", ex);
            AppServices.Notifications.Show("Neustart fehlgeschlagen",
                "Der automatische Neustart ist fehlgeschlagen. Bitte starte Gaming Hub manuell neu.");
            ShowRestartPopup = false;
        }
    }

    [RelayCommand]
    private void RestartLater() => ShowRestartPopup = false;

    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        if (IsInstallingUpdate || !HasUpdate) return;
        IsInstallingUpdate = true;
        UpdateStatusText = $"Update {UpdateVersion} wird heruntergeladen…";
        OnPropertyChanged(nameof(ShowUpdateBanner));
        OnPropertyChanged(nameof(ShowUpdateInstalling));
        try
        {
            bool ok = await AppServices.Updater.DownloadAndRestartAsync(
                p => UpdateStatusText = $"Update wird heruntergeladen… {p}%");
            if (!ok)
            {
                UpdateStatusText = "Das Update konnte nicht installiert werden (Portable-Build?).";
                AppServices.Notifications.Show("Update fehlgeschlagen",
                    "Das Update konnte nicht automatisch installiert werden. Bitte nutze den Download auf der Website.");
                IsInstallingUpdate = false;
                OnPropertyChanged(nameof(ShowUpdateBanner));
                OnPropertyChanged(nameof(ShowUpdateInstalling));
            }
        }
        catch (Exception ex)
        {
            LogService.Error("nav-update", ex);
            UpdateStatusText = "Beim Update ist ein Fehler aufgetreten. Details: " + LogService.LogFile;
            AppServices.Notifications.Show("Update fehlgeschlagen",
                "Der Download war nicht erfolgreich. Details stehen im Log unter Einstellungen.");
            IsInstallingUpdate = false;
            OnPropertyChanged(nameof(ShowUpdateBanner));
            OnPropertyChanged(nameof(ShowUpdateInstalling));
        }
    }

    public MainViewModel()
    {
        var games = new GamesViewModel();
        var cleanup = new CleanupViewModel();
        var resources = new ResourcesViewModel();
        var bug = new BugReportViewModel();
        var profile = new ProfileViewModel();
        var settings = new SettingsViewModel();
        var internetChecker = new InternetCheckerViewModel();
        InternetCheckerViewModel = internetChecker;
        var lighting = new LightingViewModel();

        _allNav.Add(new NavItem { Title = "Spiele", Glyph = "\uE7FC", FlagKey = "games_enabled", ViewModel = games });
        _allNav.Add(new NavItem { Title = "Disk Cleanup", Glyph = "\uE74D", FlagKey = "cleanup_enabled", ViewModel = cleanup });
        _allNav.Add(new NavItem { Title = "System-Ressourcen", Glyph = "\uE9D9", ViewModel = resources });
        _allNav.Add(new NavItem { Title = "Bugreport", Glyph = "\uE7BA", FlagKey = "bugreport_enabled", ViewModel = bug });
        _allNav.Add(new NavItem { Title = "Internet-Checker", Glyph = "\uE727", FlagKey = "internet_checker_enabled", ViewModel = internetChecker });
        _allNav.Add(new NavItem { Title = "Beleuchtung", Glyph = "\uE7F4", FlagKey = "lighting_enabled", ViewModel = lighting });
        _allNav.Add(new NavItem { Title = "Profil", Glyph = "\uE77B", ViewModel = profile });
        _allNav.Add(new NavItem { Title = "Einstellungen", Glyph = "\uE713", ViewModel = settings });

        RebuildNav(new Dictionary<string, bool>());
        SelectedNav = NavItems.FirstOrDefault();

        AppServices.Notifications.Activated += _ =>
        {
            // No chat tab anymore
        };

        _ = LoadFlagsAsync();
    }

    private async Task LoadFlagsAsync()
    {
        try
        {
            // For now, use default flags (all enabled)
            var flags = new Dictionary<string, bool>
            {
                { "games_enabled", true },
                { "cleanup_enabled", true },
                { "bugreport_enabled", true },
                { "internet_checker_enabled", true },
                { "lighting_enabled", true }
            };
            RebuildNav(flags);
        }
        catch
        {
            // ignore – show all tabs on failure
        }
    }

    private void RebuildNav(Dictionary<string, bool> flags)
    {
        bool isAdmin = AppServices.Profile?.IsAdmin == true;
        bool isPremium = AppServices.Profile?.IsPremium == true;
        _lastSelected = SelectedNav;

        NavItems.Clear();
        foreach (var item in _allNav)
        {
            bool enabled = item.FlagKey is null
                           || isAdmin
                           || (item.FlagKey == "internet_checker_enabled" && isPremium)
                           || !flags.TryGetValue(item.FlagKey, out var v)
                           || v;
            if (enabled)
                NavItems.Add(item);
        }

        SelectedNav = _lastSelected is not null && NavItems.Contains(_lastSelected)
            ? _lastSelected
            : NavItems.FirstOrDefault();
    }

    partial void OnSelectedNavChanged(NavItem? value)
    {
        SelectedView = value?.ViewModel;

        if (value?.ViewModel is ResourcesViewModel res)
            res.ReloadGameNames();

        if (value?.ViewModel is LightingViewModel lighting)
            lighting.Activate();
    }

    [RelayCommand]
    private void Navigate(NavItem? item)
    {
        if (item is not null)
            SelectedNav = item;
    }
}
