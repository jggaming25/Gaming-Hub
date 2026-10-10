using Velopack;
using Velopack.Sources;

namespace GamingHub.App.Services;

public sealed record UpdateCheckResult(bool HasUpdate, string? Version, string? Notes, Exception? Error);

/// <summary>
/// Wraps Velopack to check for, download and apply updates from GitHub Releases.
/// Only active when the application was installed through a Velopack package.
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

    /// <summary>Downloads the pending update and restarts the app into it.</summary>
    public async Task<bool> DownloadAndRestartAsync(Action<int>? progress = null)
    {
        var mgr = Manager;
        if (mgr is null || !mgr.IsInstalled)
            return false;

        var update = await mgr.CheckForUpdatesAsync();
        if (update is null)
            return false;

        await mgr.DownloadUpdatesAsync(update, p => progress?.Invoke(p));
        mgr.ApplyUpdatesAndRestart(update);
        return true;
    }
}
