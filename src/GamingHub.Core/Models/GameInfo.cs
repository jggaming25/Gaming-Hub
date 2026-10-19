namespace GamingHub.Core.Models;

public enum GameSource
{
    Steam,
    Epic,
    Registry,
    Manual
}

/// <summary>Category a detected program is sorted into by the classifier.</summary>
public enum GameCategory
{
    Game,
    GameTool,
    GameContent,
    Communication,
    Office,
    Development,
    Browser,
    Utility,
    Creator,
    Antivirus,
    AiTool,
    SystemService,
    Other
}

/// <summary>
/// Represents a detected game or application that Gaming Hub can manage.
/// </summary>
public sealed class GameInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public GameSource Source { get; set; }

    public string? InstallPath { get; set; }

    /// <summary>Direct executable used to start the game/app (when known).</summary>
    public string? ExecutablePath { get; set; }

    /// <summary>Protocol URI launcher, e.g. steam://rungameid/730.</summary>
    public string? LaunchUri { get; set; }

    /// <summary>Process name (without extension) used to detect a running instance.</summary>
    public string? ProcessName { get; set; }

    /// <summary>Optional path to an extracted icon file (PNG/ICO) for display in the library.</summary>
    public string? IconPath { get; set; }

    /// <summary>Optional path to a larger thumbnail image (PNG/JPG) for the game card.</summary>
    public string? ThumbnailPath { get; set; }

    public long SizeOnDisk { get; set; }

    public GameCategory Category { get; set; } = GameCategory.Other;

    public TimeSpan TotalPlaytime { get; set; }
    public DateTime? LastPlayedUtc { get; set; }

    public bool IsRunning { get; set; }

    public bool CanLaunch => !string.IsNullOrWhiteSpace(LaunchUri) || !string.IsNullOrWhiteSpace(ExecutablePath);

    /// <summary>Ob die App per Passwort (Account-Passwort) geschützt ist.</summary>
    public bool IsPasswordProtected { get; set; }
}
