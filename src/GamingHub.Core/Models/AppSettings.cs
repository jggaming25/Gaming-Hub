namespace GamingHub.Core.Models;

public sealed class AppSettings
{
    public bool HasAcceptedDisclaimer { get; set; }
    public DateTime FirstRunUtc { get; set; } = DateTime.UtcNow;

    /// <summary>ISO date of the very first program start. Used for early-bird badges.</summary>
    public DateTime? FirstEverLaunchUtc { get; set; }

    public string ThemeVariant { get; set; } = "Dark";
    public bool AutoCheckUpdates { get; set; } = true;
    public bool AutoInstallUpdates { get; set; } = true;

    public List<string> CleanupScanPaths { get; set; } = new();
    public long CleanupMinFileSizeBytes { get; set; } = 1024 * 1024; // 1 MB
    public bool UseRecycleBin { get; set; } = true;

    public string? SupabaseUrl { get; set; }
    public string? SupabaseAnonKey { get; set; }
}
