namespace GamingHub.App;

/// <summary>Central application metadata.</summary>
public static class AppInfo
{
    public const string AppName = "Gaming Hub";
    public const string Tagline = "Von Gamern für Gamer";
    public const string RepositoryUrl = "https://github.com/jggaming25/Gaming-Hub";
    public const string WebsiteUrl = "https://jggaming25.github.io/Gaming-Hub/";

    public static string Version =>
        typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    public static string VersionDisplay => $"v{Version}";
}
