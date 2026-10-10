using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using System.ComponentModel;
using GamingHub.App.Services;
using GamingHub.App.ViewModels;

namespace GamingHub.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
        DataContextChanged += OnDataContextChanged;
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
}