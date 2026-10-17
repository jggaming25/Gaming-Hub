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
        LogService.Info($"App startet – Version {AppInfo.Version} · {Environment.OSVersion}");

        InstallCrashHandlers();

        // Autostart ist standardmäßig aktiv (einmalig beim ersten Start einrichten).
        EnsureAutostartDefault();

        // Hintergrund-Erfassung der System-Ressourcen (alle 5 Min, lokal,
        // mit Retry bei nicht erhebbaren Werten). Läuft auch im Tray weiter.
        AppServices.History.Start();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var startHidden = Environment.GetCommandLineArgs().Contains("--hidden", StringComparer.OrdinalIgnoreCase)
                              || Environment.GetCommandLineArgs().Contains("--tray", StringComparer.OrdinalIgnoreCase);

            ApplyTheme();

            try
            {
                // Haftungsausschluss beim ersten Start UND nach jedem Update erneut
                // einholen (Lesezeit-Gate im Fenster). Erst danach geht es weiter.
                var needsConsent = !AppServices.Settings.Settings.HasAcceptedDisclaimer
                                   || AppServices.Settings.Settings.ConsentVersion != AppInfo.Version;
                if (needsConsent)
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
                                AppServices.Settings.Settings.ConsentVersion = AppInfo.Version;
                                AppServices.Settings.Save();
                                // Nach dem Akzeptieren der Bedingungen sichtbar starten –
                                // auch wenn die App per Autostart (versteckt) gestartet wurde.
                                ShowSplashThenMain(desktop, startHidden: false);
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
                    ShowSplashThenMain(desktop, startHidden);
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

    /// <summary>Richtet den Autostart beim allerersten Start standardmäßig ein
    /// (aktiv). Wurde er bereits bewusst gesetzt, wird nichts geändert.</summary>
    private static void EnsureAutostartDefault()
    {
        try
        {
            var settings = AppServices.Settings.Settings;
            if (settings.AutostartConfigured is null)
            {
                AutostartService.SetEnabled(true);
                settings.AutostartConfigured = true;
                AppServices.Settings.Save();
                LogService.Info("Autostart standardmäßig aktiviert (Erststart).");
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("autostart-default", ex);
        }
    }

    private static void ShowSplashThenMain(IClassicDesktopStyleApplicationLifetime desktop, bool startHidden = false)
    {
        var splash = new SplashWindow();
        desktop.MainWindow = splash;
        // Nach dem Disclaimer (oder wenn das Hauptfenster später erst gesetzt wird)
        // zeigt Avalonia das MainWindow NICHT automatisch – daher explizit anzeigen.
        if (!splash.IsVisible)
            splash.Show();

        // Die Startsequenz NICHT an das Opened-Event hängen: je nach Timing kann
        // Opened bereits während Show() feuern – ein danach abonnierter Handler
        // würde nie aufgerufen und der Splash bliebe ewig bei „Initialisiere…“.
        // Deshalb direkt starten; das Fenster wird während der awaits gerendert.
        _ = ShowSplashThenMainAsync(splash, desktop, startHidden);
    }

    private static async Task ShowSplashThenMainAsync(SplashWindow splash, IClassicDesktopStyleApplicationLifetime desktop, bool startHidden)
    {
        try
        {
            var update = await RunStartupSafeAsync(splash);

            var main = new MainWindow { DataContext = new MainViewModel() };
            desktop.MainWindow = main;
            main.Show();
            desktop.Exit += (_, _) =>
            {
                AppServices.Games.SavePlaytime();
                AppServices.Shutdown();
            };

            main.Closing += (_, e) =>
            {
                // Fenster schließen beendet die App nicht – sie läuft im Hintergrund
                // (Tray) weiter, damit z. B. die System-Ressourcen-Erfassung aktiv bleibt.
                if (MainWindow.AllowClose)
                {
                    e.Cancel = false;
                }
                else
                {
                    e.Cancel = true;
                    main.HideToTray();
                }
            };

            splash.Close();

            // Ein im Splash vorbereitetes Update ist bereits installiert – jetzt nur
            // noch den Neustart erforderlichen aktivieren: Popup im Fenster anzeigen.
            if (update.HasUpdate && AppServices.Updater.RestartRequired && main.DataContext is MainViewModel mainVm)
            {
                mainVm.RestartPending = true;
                Dispatcher.UIThread.Post(() => mainVm.ShowRestartPopup = true);
            }

            if (startHidden)
            {
                main.HideToTray();
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("splash-to-main", ex);
            ShowFatal(desktop, ex);
            splash.Close();
        }
    }

    /// <summary>
    /// Führt die Splash-Startsequenz aus, ohne dass ein hängender Update-Check den
    /// UI-Thread blockieren kann (läuft im Hintergrund-Thread), und mit Zeitlimit,
    /// damit das Hauptfenster garantiert geöffnet wird.
    /// </summary>
    private static async Task<UpdateCheckResult> RunStartupSafeAsync(SplashWindow splash)
    {
        try
        {
            return await Task.Run(() => splash.RunStartupAsync())
                .WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (Exception ex)
        {
            LogService.Info($"Startsequenz abgebrochen oder zu langsam – Hauptfenster wird trotzdem geöffnet ({ex.GetType().Name}).");
            return new UpdateCheckResult(false, null, null, null);
        }
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