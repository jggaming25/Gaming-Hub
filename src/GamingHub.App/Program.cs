using Avalonia;
using System;
using System.Threading;
using GamingHub.Core.Services;
using Velopack;

namespace GamingHub.App;

sealed class Program
{
    private static Mutex? _instanceMutex;

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Marker for Velopack packaging (Setup-Installer). Der Auto-Update-Check
        // läuft beim Start (siehe SplashWindow + App.ShowSplashThenMain).
        // Wichtig: Vor dem Anwenden eines Updates alle OpenRGB-Prozesse beenden.
        // OpenRGB würde sonst – durch das geerbte Arbeitsverzeichnis (…\current) –
        // das Installationsverzeichnis offen halten und der Verzeichnis-Rename von
        // Velopack beim Update schlägt fehl (App startet endlos neu).
        VelopackApp.Build()
            .OnBeforeUpdateFastCallback(_ => RgbService.KillOpenRgbProcesses())
            .Run();

        // Nur eine Instanz zulassen: Mehrere Instanzen würden sich gegenseitig
        // die Verlaufs-/Internet-Dateien sperren. Ein zweiter Start holt das
        // bereits laufende Fenster nach vorne.
        _instanceMutex = new Mutex(true, @"Local\GamingHub.SingleInstance", out bool isNewInstance);
        if (!isNewInstance)
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting(@"Local\GamingHub.ShowWindow");
                signal.Set();
            }
            catch
            {
                // ignore
            }
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        GC.KeepAlive(_instanceMutex);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
