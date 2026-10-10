using Velopack;
using Velopack.Sources;

namespace GamingHub.App.Services;

public sealed record UpdateCheckResult(bool HasUpdate, string? Version, string? Notes, Exception? Error);

/// <summary>
/// Wraps Velopack to check for, download and apply updates from the release feed.
/// Only active when the application was installed through the Setup-Installer.
/// Der Check läuft beim Programmstart (siehe SplashWindow).
/// </summary>
public sealed class UpdateService
{
    private UpdateManager? _manager;

    private UpdateManager? Manager
    {
        get
        {
            if (_manager is null)
            {
                try
                {
                    _manager = new UpdateManager(
                        new GithubSource(AppInfo.RepositoryUrl, accessToken: null, prerelease: false));
                }
                catch
                {
                    // Not a Velopack-installed build (developer run).
                }
            }
            return _manager;
        }
    }

    public bool IsInstalled => Manager?.IsInstalled ?? false;

    public async Task<UpdateCheckResult> CheckForUpdatesAsync()
    {
        var mgr = Manager;
        if (mgr is null || !mgr.IsInstalled)
            return new UpdateCheckResult(false, null, null, null);

        try
        {
            var update = await mgr.CheckForUpdatesAsync();
            if (update is null)
                return new UpdateCheckResult(false, null, null, null);

            return new UpdateCheckResult(
                true,
                update.TargetFullRelease.Version.ToString(),
                update.TargetFullRelease.NotesMarkdown,
                null);
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(false, null, null, ex);
        }
    }

    public async Task DownloadAndRestartAsync(Action<int>? progress = null)
    {
        var mgr = Manager;
        if (mgr is null || !mgr.IsInstalled)
            return;

        var update = await mgr.CheckForUpdatesAsync();
        if (update is null)
            return;

        await mgr.DownloadUpdatesAsync(update, p => progress?.Invoke(p));
        mgr.ApplyUpdatesAndRestart(update);
    }
}
