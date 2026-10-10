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
    /// Plays the intro sound, checks for updates and shows the splash long enough
    /// for the animation to be visible.
    /// </summary>
    public async Task RunStartupAsync()
    {
        SoundHelper.PlayIntro();
        var minimumDelay = Task.Delay(1700);
        var settings = AppServices.Settings.Settings;

        if (settings.AutoCheckUpdates)
        {
            StatusText.Text = "Suche nach Updates…";
            try
            {
                var result = await AppServices.Updater.CheckForUpdatesAsync();
                if (result.HasUpdate && settings.AutoInstallUpdates)
                {
                    StatusText.Text = $"Update {result.Version} wird installiert…";
                    await AppServices.Updater.DownloadAndRestartAsync(p =>
                        StatusText.Text = $"Update wird geladen… {p}%");
                }
                else if (result.HasUpdate)
                {
                    StatusText.Text = $"Update {result.Version} verfügbar (siehe Einstellungen).";
                    await Task.Delay(1200);
                }
            }
            catch
            {
                // ignore update failures during startup
            }
        }

        await minimumDelay;
        StatusText.Text = "Starte Gaming Hub…";
    }
}
