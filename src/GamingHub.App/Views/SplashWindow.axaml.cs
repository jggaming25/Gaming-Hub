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
    /// Plays the intro sound and shows the splash long enough for the animation
    /// to be visible.
    /// </summary>
    public async Task RunStartupAsync()
    {
        SoundHelper.PlayIntro();
        await Task.Delay(1700);
        StatusText.Text = "Starte Gaming Hub…";
    }
}
