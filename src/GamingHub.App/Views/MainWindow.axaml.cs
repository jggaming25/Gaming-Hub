using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using System.ComponentModel;
using GamingHub.App.Services;
using GamingHub.App.ViewModels;

namespace GamingHub.App.Views;

public partial class MainWindow : Window
{
    /// <summary>Set to true by the tray „Beenden“ item, so the window may actually close.</summary>
    public static bool AllowClose { get; set; }

    private TrayIcon? _tray;

    public MainWindow()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
        DataContextChanged += OnDataContextChanged;
        CreateTray();
        Opened += (_, _) => { if (_tray is not null) _tray.IsVisible = true; };
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.SelectedNav) or nameof(MainViewModel.SelectedView))
            AnimateContentSwitch();
    }

    private void AnimateContentSwitch()
    {
        if (ContentHost is null)
            return;

        ContentHost.Opacity = 0;
        Dispatcher.UIThread.Post(() => ContentHost.Opacity = 1, DispatcherPriority.Loaded);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            WindowState = WindowState == WindowState.FullScreen
                ? WindowState.Normal
                : WindowState.FullScreen;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F12)
        {
            LogService.Info("F12 gedrückt – Log-Ordner wird geöffnet.");
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", LogService.LogFolder)
                {
                    UseShellExecute = true
                });
            }
            catch { /* ignore */ }
            e.Handled = true;
        }
    }

    /// <summary>Versteckt das Fenster im System-Tray, damit die App im Hintergrund weiterläuft.</summary>
    public void HideToTray()
    {
        ShowInTaskbar = false;
        WindowState = WindowState.Minimized;
        Hide();
        if (_tray is not null) _tray.IsVisible = true;
    }

    /// <summary>Bringt das Fenster aus dem Tray zurück.</summary>
    public void ShowFromTray()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;
        Activate();
        if (_tray is not null) _tray.IsVisible = true;
    }

    private void CreateTray()
    {
        try
        {
            var menu = new NativeMenu();

            var open = new NativeMenuItem("Gaming Hub öffnen");
            open.Click += (_, _) => ShowFromTray();
            menu.Items.Add(open);

            var quit = new NativeMenuItem("Beenden");
            quit.Click += (_, _) =>
            {
                AllowClose = true;
                if (_tray is not null) _tray.IsVisible = false;
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown();
                }
            };
            menu.Items.Add(quit);

            var icon = LoadAppIcon();
            _tray = new TrayIcon
            {
                ToolTipText = "Gaming Hub – läuft im Hintergrund",
                Icon = icon,
                Menu = menu
            };
        }
        catch (Exception ex)
        {
            LogService.Warn("Tray-Icon konnte nicht erstellt werden.", ex);
        }
    }

    private static WindowIcon? LoadAppIcon()
    {
        try
        {
            var uri = new Uri("avares://GamingHub.App/Assets/logo.ico");
            using var stream = AssetLoader.Open(uri);
            return new WindowIcon(stream);
        }
        catch
        {
            return null;
        }
    }
}