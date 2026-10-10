namespace GamingHub.Core.Models;

public enum GameSource
{
    Steam,
    Epic,
    Registry,
    Manual
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

    public long SizeOnDisk { get; set; }

    public TimeSpan TotalPlaytime { get; set; }
    public DateTime? LastPlayedUtc { get; set; }

    public bool IsRunning { get; set; }

    public bool CanLaunch => !string.IsNullOrWhiteSpace(LaunchUri) || !string.IsNullOrWhiteSpace(ExecutablePath);
}
