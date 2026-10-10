using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using GamingHub.App.Services;
using GamingHub.App.ViewModels;
using GamingHub.App.Views;

namespace GamingHub.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            ApplyTheme();

            if (!AppServices.Settings.Settings.HasAcceptedDisclaimer)
            {
                var disclaimer = new DisclaimerWindow();
                desktop.MainWindow = disclaimer;
                disclaimer.Closed += (_, _) =>
                {
                    if (disclaimer.Accepted)
                    {
                        AppServices.Settings.Settings.HasAcceptedDisclaimer = true;
                        AppServices.Settings.Save();
                        ShowSplashThenMain(desktop);
                    }
                    else
                    {
                        desktop.Shutdown();
                    }
                };
            }
            else
            {
                ShowSplashThenMain(desktop);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ShowSplashThenMain(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var splash = new SplashWindow();
        desktop.MainWindow = splash;

        splash.Opened += async (_, _) =>
        {
            await splash.RunStartupAsync();

            var main = new MainWindow { DataContext = new MainViewModel() };
            desktop.MainWindow = main;
            main.Show();
            main.Closed += (_, _) =>
            {
                AppServices.Games.SavePlaytime();
                desktop.Shutdown();
            };
            splash.Close();
        };
    }

    private void ApplyTheme()
    {
        RequestedThemeVariant = AppServices.Settings.Settings.ThemeVariant == "Light"
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
    }
}
