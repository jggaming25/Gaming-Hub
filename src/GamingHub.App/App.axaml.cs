using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
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
        InstallCrashHandlers();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            ApplyTheme();

            try
            {
                if (!AppServices.Settings.Settings.HasAcceptedDisclaimer)
                {
                    var disclaimer = new DisclaimerWindow();
                    desktop.MainWindow = disclaimer;
                    disclaimer.Closed += (_, _) =>
                    {
                        try
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
                        }
                        catch (Exception ex)
                        {
                            CrashLog.Write("disclaimer-close", ex);
                            ShowFatal(desktop, ex);
                        }
                    };
                }
                else
                {
                    ShowSplashThenMain(desktop);
                }
            }
            catch (Exception ex)
            {
                CrashLog.Write("startup", ex);
                ShowFatal(desktop, ex);
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
            try
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
            }
            catch (Exception ex)
            {
                CrashLog.Write("splash-to-main", ex);
                ShowFatal(desktop, ex);
                splash.Close();
            }
        };
    }

    private static void ShowFatal(IClassicDesktopStyleApplicationLifetime desktop, Exception ex)
    {
        try
        {
            var message = new TextBlock
            {
                Text = $"Beim Start ist ein Fehler aufgetreten.\n\n{ex.Message}\n\nDetails wurden unter {CrashLog.Path} gespeichert.\n\nDu kannst die App schließen.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.Parse("#E5E7EB"))
            };
            var window = new Window
            {
                Title = "Gaming Hub – Startfehler",
                Width = 560,
                Height = 300,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = new SolidColorBrush(Color.Parse("#0B0D1A")),
                Content = new Border { Padding = new Thickness(18), Child = message }
            };
            desktop.MainWindow = window;
            window.Show();
        }
        catch
        {
            // ignore
        }
    }

    private void InstallCrashHandlers()
    {
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            CrashLog.Write("ui-dispatcher", e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            CrashLog.Write("appdomain", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write("task", e.Exception);
            e.SetObserved();
        };
    }

    private void ApplyTheme()
    {
        RequestedThemeVariant = AppServices.Settings.Settings.ThemeVariant == "Light"
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
    }
}