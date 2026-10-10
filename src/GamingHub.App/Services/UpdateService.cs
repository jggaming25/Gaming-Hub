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
    private UpdateInfo? _pendingUpdate;

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
                catch (Exception ex)
                {
                    LogService.Warn("UpdateManager konnte nicht erstellt werden (Entwickler-/Portable-Build?).", ex);
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
        {
            LogService.Info("Update-Check übersprungen (kein installierter Velopack-Build).");
            return new UpdateCheckResult(false, null, null, null);
        }

        try
        {
            var update = await mgr.CheckForUpdatesAsync();
            if (update is null)
            {
                _pendingUpdate = null;
                LogService.Info("Update-Check: keine neue Version verfügbar.");
                return new UpdateCheckResult(false, null, null, null);
            }

            _pendingUpdate = update;
            var version = update.TargetFullRelease.Version.ToString();
            LogService.Info($"Update-Check: Version {version} gefunden.");

            return new UpdateCheckResult(true, version, update.TargetFullRelease.NotesMarkdown, null);
        }
        catch (Exception ex)
        {
            LogService.Error("update-check", ex);
            return new UpdateCheckResult(false, null, null, ex);
        }
    }

    /// <summary>
    /// Downloads the update found by the last <see cref="CheckForUpdatesAsync"/> and
    /// restarts the app into the new version. Falls back to a fresh check if needed.
    /// Returns false when no update is available (or the build is not installed).
    /// </summary>
    public async Task<bool> DownloadAndRestartAsync(Action<int>? progress = null)
    {
        var mgr = Manager;
        if (mgr is null || !mgr.IsInstalled)
        {
            LogService.Warn("Update-Installation abgebrochen: kein installierter Velopack-Build.");
            return false;
        }

        try
        {
            var update = _pendingUpdate ?? await mgr.CheckForUpdatesAsync();
            if (update is null)
            {
                LogService.Info("Update-Installation übersprungen: kein Update vorhanden.");
                return false;
            }

            LogService.Info($"Update wird heruntergeladen (Version {update.TargetFullRelease.Version}).");
            await mgr.DownloadUpdatesAsync(update, p => { progress?.Invoke(p); });
            LogService.Info("Download abgeschlossen – Starte Neustart zum Installieren.");
            mgr.ApplyUpdatesAndRestart(update);
            return true;
        }
        catch (Exception ex)
        {
            LogService.Error("update-install", ex);
            return false;
        }
    }
}