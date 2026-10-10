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

    public MainViewModel()
    {
        var games = new GamesViewModel();
        var cleanup = new CleanupViewModel();
        var resources = new ResourcesViewModel();
        var bug = new BugReportViewModel();
        var chat = new ChatViewModel();
        var profile = new ProfileViewModel();
        var settings = new SettingsViewModel();

        _allNav.Add(new NavItem { Title = "Spiele", Glyph = "\uE7FC", FlagKey = "games_enabled", ViewModel = games });
        _allNav.Add(new NavItem { Title = "Disk Cleanup", Glyph = "\uE74D", FlagKey = "cleanup_enabled", ViewModel = cleanup });
        _allNav.Add(new NavItem { Title = "System-Ressourcen", Glyph = "\uE9D9", ViewModel = resources });
        _allNav.Add(new NavItem { Title = "Bugreport", Glyph = "\uE7BA", FlagKey = "bugreport_enabled", ViewModel = bug });
        _allNav.Add(new NavItem { Title = "Kommunikation", Glyph = "\uE8BD", FlagKey = "chat_enabled", ViewModel = chat });
        _allNav.Add(new NavItem { Title = "Profil", Glyph = "\uE77B", ViewModel = profile });
        _allNav.Add(new NavItem { Title = "Einstellungen", Glyph = "\uE713", ViewModel = settings });

        RebuildNav(new Dictionary<string, bool>());
        SelectedNav = NavItems.FirstOrDefault();

        AppServices.Notifications.Activated += _ =>
        {
            var chatItem = NavItems.FirstOrDefault(n => n.ViewModel is ChatViewModel);
            if (chatItem is not null)
                SelectedNav = chatItem;
        };

        _ = LoadFlagsAsync();
    }

    private async Task LoadFlagsAsync()
    {
        try
        {
            var flags = await AppServices.Chat.LoadFlagsAsync();
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
        _lastSelected = SelectedNav;

        NavItems.Clear();
        foreach (var item in _allNav)
        {
            bool enabled = item.FlagKey is null
                           || isAdmin
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
    }

    [RelayCommand]
    private void Navigate(NavItem? item)
    {
        if (item is not null)
            SelectedNav = item;
    }
}
