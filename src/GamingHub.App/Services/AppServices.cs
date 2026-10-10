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
    }
}
