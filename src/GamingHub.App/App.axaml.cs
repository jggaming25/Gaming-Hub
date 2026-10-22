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
using GamingHub.Core.Models;
using System.Runtime.InteropServices;
using System.Threading;

namespace GamingHub.App;

public partial class App : Application
{
    private EventWaitHandle? _showWindowSignal;
    private static readonly List<ScreenTimeBlockWindow> _blockWindows = new();
    private static AppLimitWindow? _appLimitWindow;
    private static DispatcherTimer? _appLimitTimer;
    private static string _appLimitProcess = string.Empty;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        LogService.Info($"App startet – Version {AppInfo.Version} · {Environment.OSVersion}");

        StartShowWindowListener();
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

    /// <summary>Hebt ein bereits laufendes Fenster in den Vordergrund, wenn die App
    /// ein zweites Mal gestartet wird (siehe Program.Main).</summary>
    private void StartShowWindowListener()
    {
        try
        {
            _showWindowSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\GamingHub.ShowWindow");
            var thread = new Thread(() =>
            {
                while (_showWindowSignal.WaitOne())
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                            && desktop.MainWindow is { } window)
                        {
                            window.Show();
                            if (window.WindowState == WindowState.Minimized)
                                window.WindowState = WindowState.Normal;
                            window.Activate();
                        }
                    });
                }
            })
            {
                IsBackground = true,
                Name = "GamingHub-ShowWindow"
            };
            thread.Start();
        }
        catch (Exception ex)
        {
            LogService.Warn("Einzelinstanz-Listener konnte nicht gestartet werden.", ex);
        }
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

            // Bildschirmzeit-Sperre an die Oberfläche binden (Topmost-Fenster).
            AppServices.ScreenTime.BlockChanged += OnScreenTimeBlockChanged;

            desktop.Exit += (_, _) =>
            {
                AppServices.Games.SavePlaytime();
                Dispatcher.UIThread.Invoke(() =>
                {
                    CloseBlockWindows();
                    CloseAppLimitWindow();
                });
                AppServices.ScreenTime.BlockChanged -= OnScreenTimeBlockChanged;
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

    private static void OnScreenTimeBlockChanged(ScreenTimeBlock? block)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (block is null)
            {
                CloseBlockWindows();
                CloseAppLimitWindow();
                return;
            }

            if (block.Kind == ScreenTimeBlockKind.AppLimit)
            {
                CloseBlockWindows();
                ShowAppLimitWindow(block);
                return;
            }

            CloseAppLimitWindow();
            ShowFullScreenBlock(block);
        });
    }

    private static void ShowFullScreenBlock(ScreenTimeBlock block)
    {
        var screens = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow?.Screens.All;
        int screenCount = Math.Max(1, screens?.Count ?? 1);

        if (_blockWindows.Count != screenCount)
        {
            CloseBlockWindows();

            if (screens is null || screens.Count == 0)
            {
                CreateBlockWindow(block, true, null);
            }
            else
            {
                for (int i = 0; i < screens.Count; i++)
                    CreateBlockWindow(block, i == 0, screens[i].Bounds);
            }
        }

        if (_blockWindows.Count > 0)
        {
            _blockWindows[0].Update(block);
            _blockWindows[0].Activate();
        }
    }

    private static void ShowAppLimitWindow(ScreenTimeBlock block)
    {
        if (_appLimitWindow is null || !string.Equals(_appLimitProcess, block.ProcessName, StringComparison.OrdinalIgnoreCase))
        {
            CloseAppLimitWindow();
            _appLimitProcess = block.ProcessName;

            var window = new AppLimitWindow();
            window.ShutdownRequested += () => AppServices.ScreenTime.TerminateProcess(_appLimitProcess);
            _appLimitWindow = window;
        }

        _appLimitWindow.Update(block);
        _appLimitTimer ??= CreateAppLimitTimer();
        _appLimitTimer.Start();
        TrackAppLimitWindow();
    }

    private static DispatcherTimer CreateAppLimitTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        timer.Tick += (_, _) => TrackAppLimitWindow();
        return timer;
    }

    private static void TrackAppLimitWindow()
    {
        var window = _appLimitWindow;
        if (window is null) return;

        var target = FindAppWindow(_appLimitProcess);
        if (target == IntPtr.Zero || IsIconic(target) || !GetWindowRect(target, out var rect))
        {
            if (window.IsVisible) window.Hide();
            return;
        }

        bool active = IsForegroundProcess(_appLimitProcess) || IsForegroundOwnProcess();
        if (!active)
        {
            if (window.IsVisible) window.Hide();
            return;
        }

        if (!window.IsVisible)
        {
            window.Show();
            window.Activate();
        }

        MoveToScreen(window, new PixelRect(rect.Left, rect.Top,
            Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top)));
    }

    private static IntPtr FindAppWindow(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return IntPtr.Zero;
        try
        {
            foreach (var process in System.Diagnostics.Process.GetProcessesByName(processName))
            {
                try
                {
                    var handle = process.MainWindowHandle;
                    if (handle != IntPtr.Zero && IsWindowVisible(handle))
                        return handle;
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch
        {
            // ignore
        }

        IntPtr found = IntPtr.Zero;
        var pids = new HashSet<uint>();
        try
        {
            foreach (var process in System.Diagnostics.Process.GetProcessesByName(processName))
            {
                try { pids.Add((uint)process.Id); } finally { process.Dispose(); }
            }
        }
        catch
        {
            return IntPtr.Zero;
        }

        if (pids.Count == 0) return IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (!pids.Contains(pid)) return true;
            found = hwnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private static bool IsForegroundOwnProcess()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;
            GetWindowThreadProcessId(hwnd, out uint pid);
            return pid == (uint)Environment.ProcessId;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsForegroundProcess(string processName)
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return false;
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            return string.Equals(process.ProcessName, processName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void CloseAppLimitWindow()
    {
        _appLimitTimer?.Stop();
        _appLimitTimer = null;
        if (_appLimitWindow is not null)
        {
            try { _appLimitWindow.CloseForced(); } catch { /* ignore */ }
        }
        _appLimitWindow = null;
        _appLimitProcess = string.Empty;
    }

    private static void CreateBlockWindow(ScreenTimeBlock block, bool primary, PixelRect? bounds)
    {
        var window = new ScreenTimeBlockWindow(primary);
        window.ExtensionRequested += minutes => AppServices.ScreenTime.GrantAccess(minutes);
        window.LogoutRequested += Logout;
        window.Show();

        if (primary)
            window.Update(block);

        if (bounds is { } rect)
            MoveToScreen(window, rect);

        _blockWindows.Add(window);
    }

    private static void CloseBlockWindows()
    {
        foreach (var window in _blockWindows)
        {
            try { window.CloseForced(); } catch { /* ignore */ }
        }
        _blockWindows.Clear();
    }

    private static void Logout()
    {
        try
        {
            AppServices.ScreenTime.Save();
            AppServices.Games.SavePlaytime();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown.exe", "/l")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch
        {
            // Abmelden kann von Windows abgelehnt werden.
        }
    }

    private static void MoveToScreen(Window window, PixelRect bounds)
    {
        try
        {
            var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero) return;
            SetWindowPos(handle, HwndTopmost, bounds.X, bounds.Y, bounds.Width, bounds.Height,
                SwpShowWindow | SwpNoActivate);
        }
        catch
        {
            // Ohne Win32-Handle bleibt das Fenster auf dem Hauptbildschirm.
        }
    }

    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpNoActivate = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

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