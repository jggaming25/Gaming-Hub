using GamingHub.Core.Services;

namespace GamingHub.App.Services;

/// <summary>
/// Simple manual composition root. Creates and shares the core services
/// for the lifetime of the application.
/// </summary>
public static class AppServices
{
    public static SettingsService Settings { get; }
    public static GameLibraryService Games { get; }
    public static DiskUsageService DiskUsage { get; }
    public static DuplicateFinderService DuplicateFinder { get; }
    public static FileDeletionService FileDeletion { get; }
    public static SystemMonitorService SystemMonitor { get; }
    public static CompatibilityService Compatibility { get; }
    public static UpdateService Updater { get; }
    public static AuthService Auth { get; }
    public static BadgeService Badges { get; }
    public static BugService Bugs { get; }
    public static ChatService Chat { get; }
    public static CallService Calls { get; }
    public static NotificationService Notifications { get; } = new();

    public static void Shutdown()
    {
        Calls.Stop();
        Notifications.Shutdown();
    }

    public static void OpenBrowser(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch
        {
            // ignore
        }
    }

    static AppServices()
    {
        Settings = new SettingsService();
        Games = new GameLibraryService(Settings.DataDirectory);
        DiskUsage = new DiskUsageService();
        DuplicateFinder = new DuplicateFinderService();
        FileDeletion = new FileDeletionService();
        SystemMonitor = new SystemMonitorService();
        Compatibility = new CompatibilityService();
        Updater = new UpdateService();
        Auth = new AuthService(Settings.DataDirectory);
        Badges = new BadgeService(Settings.DataDirectory);
        Badges.EvaluateAutomatic(Settings.Settings.FirstEverLaunchUtc);
        Bugs = new BugService();
        Chat = new ChatService();
        Calls = new CallService();
        Calls.Start();
    }
}
