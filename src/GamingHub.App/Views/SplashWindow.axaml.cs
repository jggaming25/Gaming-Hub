using Avalonia.Controls;
using Avalonia.Threading;
using GamingHub.App.Services;

namespace GamingHub.App.Views;

public partial class SplashWindow : Window
{
    private readonly bool _hiddenStart =
        Environment.GetCommandLineArgs().Contains("--hidden", StringComparer.OrdinalIgnoreCase)
        || Environment.GetCommandLineArgs().Contains("--tray", StringComparer.OrdinalIgnoreCase);

    public SplashWindow()
    {
        InitializeComponent();
        VersionText.Text = AppInfo.VersionDisplay;
    }

    /// <summary>
    /// Plays the intro sound (unless the app was started hidden via autostart),
    /// checks for updates at startup and shows the splash long enough for the
    /// content to be visible. If a new version was found, it is downloaded and
    /// prepared right here (installation), so only a restart is needed to
    /// activate it – the main window then shows a restart popup.
    /// </summary>
    public async Task<UpdateCheckResult> RunStartupAsync()
    {
        if (!_hiddenStart)
            SoundHelper.PlayIntro();

        var minimumDelay = Task.Delay(1700);
        var settings = AppServices.Settings.Settings;
        var result = new UpdateCheckResult(false, null, null, null);

        void SetStatus(string text) => Dispatcher.UIThread.Post(() => StatusText.Text = text);
        void SetProgress(int percent) => Dispatcher.UIThread.Post(() =>
        {
            StartupBar.IsIndeterminate = false;
            StartupBar.Value = Math.Clamp(percent, 0, 100);
        });
        void ResetProgress() => Dispatcher.UIThread.Post(() => StartupBar.IsIndeterminate = true);

        if (settings.AutoCheckUpdates)
        {
            SetStatus("Suche nach Updates…");
            try
            {
                // Timeout, damit der Splash bei einer hängenden Netzwerk-/GitHub-
                // Anfrage nicht unbegrenzt bei „Suche nach Updates…“ stehen bleibt.
                result = await AppServices.Updater.CheckForUpdatesAsync()
                    .WaitAsync(TimeSpan.FromSeconds(15));
                if (result.HasUpdate)
                {
                    if (settings.AutoInstallUpdates)
                    {
                        SetStatus($"Update {result.Version} wird heruntergeladen…");
                        bool prepared = await AppServices.Updater.PrepareUpdateAsync(p =>
                        {
                            SetProgress(p);
                            SetStatus($"Update {result.Version} wird heruntergeladen… {p} %");
                        });
                        if (prepared)
                        {
                            SetProgress(100);
                            SetStatus($"Update {result.Version} installiert – Neustart wird vorbereitet.");
                        }
                        else
                        {
                            SetStatus($"Update {result.Version} konnte nicht geladen werden.");
                        }
                    }
                    else
                    {
                        // Automatische Installation deaktiviert – nur Hinweis,
                        // Installation erfolgt manuell in den Einstellungen.
                        SetStatus($"Update {result.Version} verfügbar – Installation in den Einstellungen.");
                    }
                }
            }
            catch
            {
                // ignore update failures during startup
            }
            finally
            {
                ResetProgress();
            }
        }

        await minimumDelay;
        SetStatus("Starte Gaming Hub…");
        return result;
    }
}
