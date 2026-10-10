namespace GamingHub.Core.Models;

public sealed class AppSettings
{
    /// <summary>Ob der Haftungsausschluss bereits akzeptiert wurde.</summary>
    public bool HasAcceptedDisclaimer { get; set; }

    /// <summary>App-Version, in der der Haftungsausschluss zuletzt akzeptiert
    /// wurde. Nach einem Update muss neu eingewilligt werden (Lesezeit-Gate).</summary>
    public string? ConsentVersion { get; set; }

    public DateTime FirstRunUtc { get; set; } = DateTime.UtcNow;

    /// <summary>ISO date of the very first program start. Used for early-bird badges.</summary>
    public DateTime? FirstEverLaunchUtc { get; set; }

    public string ThemeVariant { get; set; } = "Dark";
    public bool AutoCheckUpdates { get; set; } = true;
    public bool AutoInstallUpdates { get; set; } = true;

    /// <summary>Ob der Autostart (Standard: aktiv) bereits einmal eingerichtet
    /// wurde. null = noch nie gesetzt (Erststart → automatisch aktivieren).</summary>
    public bool? AutostartConfigured { get; set; }

    public bool DesktopNotificationsEnabled { get; set; } = true;
    public bool NotificationSoundEnabled { get; set; } = true;

    public List<string> CleanupScanPaths { get; set; } = new();
    public long CleanupMinFileSizeBytes { get; set; } = 1024 * 1024; // 1 MB
    public bool UseRecycleBin { get; set; } = true;

    public string? SupabaseUrl { get; set; }
    public string? SupabaseAnonKey { get; set; }
}
