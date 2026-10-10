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

    /// <summary>Ergebnis des letzten Update-Checks (wird z. B. für die
    /// Update-Anzeige in der Navigation verwendet).</summary>
    public UpdateCheckResult? LastCheck { get; private set; }

    /// <summary>Version eines bereits heruntergeladenen Updates, das nur noch
    /// durch einen Neustart aktiviert werden muss (oder null).</summary>
    public string? PendingVersion { get; private set; }

    /// <summary>True, wenn ein Update vorbereitet wurde und ein Neustart zum
    /// Aktivieren notwendig ist.</summary>
    public bool RestartRequired => PendingVersion is not null;

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
            return LastCheck = new UpdateCheckResult(false, null, null, null);
        }

        try
        {
            var update = await mgr.CheckForUpdatesAsync();
            if (update is null)
            {
                _pendingUpdate = null;
                LogService.Info("Update-Check: keine neue Version verfügbar.");
                return LastCheck = new UpdateCheckResult(false, null, null, null);
            }

            _pendingUpdate = update;
            var version = update.TargetFullRelease.Version.ToString();
            LogService.Info($"Update-Check: Version {version} gefunden.");

            return LastCheck = new UpdateCheckResult(true, version, update.TargetFullRelease.NotesMarkdown, null);
        }
        catch (Exception ex)
        {
            LogService.Error("update-check", ex);
            return LastCheck = new UpdateCheckResult(false, null, null, ex);
        }
    }

    /// <summary>
    /// Downloads the update found by the last <see cref="CheckForUpdatesAsync"/> and
    /// prepares it for installation (staging). The running app is NOT restarted here –
    /// a restart (see <see cref="RestartToApply"/>) completes the installation.
    /// Returns false when no update is available (or the build is not installed).
    /// </summary>
    public async Task<bool> PrepareUpdateAsync(Action<int>? progress = null)
    {
        var mgr = Manager;
        if (mgr is null || !mgr.IsInstalled)
        {
            LogService.Warn("Update-Vorbereitung abgebrochen: kein installierter Velopack-Build.");
            return false;
        }

        try
        {
            // Idempotent: wenn dieselbe Version schon vorbereitet ist, nichts erneut laden.
            if (PendingVersion is not null && LastCheck?.Version == PendingVersion)
            {
                LogService.Info($"Update {PendingVersion} ist bereits vorbereitet.");
                return true;
            }

            var update = _pendingUpdate ?? await mgr.CheckForUpdatesAsync();
            if (update is null)
            {
                LogService.Info("Update-Vorbereitung übersprungen: kein Update vorhanden.");
                return false;
            }

            LogService.Info($"Update wird heruntergeladen und vorbereitet (Version {update.TargetFullRelease.Version}).");
            await mgr.DownloadUpdatesAsync(update, p => progress?.Invoke(p));

            PendingVersion = update.TargetFullRelease.Version.ToString();
            LogService.Info($"Update {PendingVersion} wurde installiert – Neustart zum Aktivieren erforderlich.");
            return true;
        }
        catch (Exception ex)
        {
            LogService.Error("update-prepare", ex);
            return false;
        }
    }

    /// <summary>
    /// Startargumente für den Neustart nach einem Update. Die Autostart-Flags
    /// (--hidden / --tray) werden bewusst entfernt, damit die neue Version
    /// sichtbar startet und nicht im Tray "verschwindet".
    /// </summary>
    private static string[] BuildRestartArgs()
    {
        return Environment.GetCommandLineArgs()
            .Skip(1)
            .Where(a => !a.Equals("--hidden", StringComparison.OrdinalIgnoreCase)
                        && !a.Equals("--tray", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    /// <summary>
    /// Installs the prepared update and restarts the application into the new version.
    /// This method normally does not return, because the process is restarted.
    /// Throws when no prepared update exists or the build is not installed.
    /// </summary>
    public void RestartToApply()
    {
        var mgr = Manager ?? throw new InvalidOperationException("Kein Velopack-Build verfügbar.");
        var update = _pendingUpdate ?? throw new InvalidOperationException("Kein vorbereitetes Update vorhanden.");
        LogService.Info("Neustart zum Aktivieren von Updates…");
        mgr.ApplyUpdatesAndRestart(update.TargetFullRelease, BuildRestartArgs());
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
            mgr.ApplyUpdatesAndRestart(update.TargetFullRelease, BuildRestartArgs());
            return true;
        }
        catch (Exception ex)
        {
            LogService.Error("update-install", ex);
            return false;
        }
    }
}