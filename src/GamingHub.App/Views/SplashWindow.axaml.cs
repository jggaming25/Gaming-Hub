using Avalonia.Controls;
using GamingHub.App.Services;

namespace GamingHub.App.Views;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        VersionText.Text = AppInfo.VersionDisplay;
    }

    /// <summary>
    /// Plays the intro sound, checks for updates at startup and shows the splash
    /// long enough for the animation to be visible. The update is NOT installed here –
    /// installation happens after the main window is up (see App.ShowSplashThenMain),
    /// which is the same code path as the manual update and therefore reliable.
    /// </summary>
    public async Task<UpdateCheckResult> RunStartupAsync()
    {
        SoundHelper.PlayIntro();
        var minimumDelay = Task.Delay(1700);
        var settings = AppServices.Settings.Settings;
        var result = new UpdateCheckResult(false, null, null, null);

        if (settings.AutoCheckUpdates)
        {
            StatusText.Text = "Suche nach Updates…";
            try
            {
                result = await AppServices.Updater.CheckForUpdatesAsync();
                if (result.HasUpdate && !settings.AutoInstallUpdates)
                {
                    StatusText.Text = $"Update {result.Version} verfügbar (siehe Einstellungen).";
                    await Task.Delay(1200);
                }
                else if (result.HasUpdate)
                {
                    StatusText.Text = $"Update {result.Version} gefunden – wird nach dem Start installiert…";
                }
            }
            catch
            {
                // ignore update failures during startup
            }
        }

        await minimumDelay;
        StatusText.Text = "Starte Gaming Hub…";
        return result;
    }
}
