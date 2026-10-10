using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamingHub.App.Services;

namespace GamingHub.App.ViewModels;

public sealed class NavItem
{
    public string Title { get; init; } = string.Empty;
    public string Glyph { get; init; } = string.Empty;
    public ViewModelBase ViewModel { get; init; } = null!;
}

public partial class MainViewModel : ViewModelBase
{
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

        NavItems.Add(new NavItem { Title = "Spiele", Glyph = "\uE7FC", ViewModel = games });
        NavItems.Add(new NavItem { Title = "Disk Cleanup", Glyph = "\uE74D", ViewModel = cleanup });
        NavItems.Add(new NavItem { Title = "System-Ressourcen", Glyph = "\uE9D9", ViewModel = resources });
        NavItems.Add(new NavItem { Title = "Bugreport", Glyph = "\uE7BA", ViewModel = bug });
        NavItems.Add(new NavItem { Title = "Chat", Glyph = "\uE8BD", ViewModel = chat });
        NavItems.Add(new NavItem { Title = "Profil", Glyph = "\uE77B", ViewModel = profile });
        NavItems.Add(new NavItem { Title = "Einstellungen", Glyph = "\uE713", ViewModel = settings });

        AppServices.Notifications.Activated += _ =>
        {
            var chat = NavItems.FirstOrDefault(n => n.ViewModel is ChatViewModel);
            if (chat is not null)
                SelectedNav = chat;
        };

        SelectedNav = NavItems[0];
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
