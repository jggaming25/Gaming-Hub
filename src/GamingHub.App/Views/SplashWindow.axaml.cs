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

        if (settings.AutoCheckUpdates)
        {
            SetStatus("Suche nach Updates…");
            try
            {
                result = await AppServices.Updater.CheckForUpdatesAsync();
                if (result.HasUpdate)
                {
                    SetStatus($"Update {result.Version} wird heruntergeladen und installiert…");
                    bool prepared = await AppServices.Updater.PrepareUpdateAsync(p =>
                        SetStatus($"Update {result.Version} wird heruntergeladen… {p} %"));

                    if (prepared)
                        SetStatus($"Update {result.Version} installiert – Neustart wird vorbereitet.");
                    else
                        SetStatus($"Update {result.Version} konnte nicht geladen werden.");
                }
            }
            catch
            {
                // ignore update failures during startup
            }
        }

        await minimumDelay;
        SetStatus("Starte Gaming Hub…");
        return result;
    }
}
