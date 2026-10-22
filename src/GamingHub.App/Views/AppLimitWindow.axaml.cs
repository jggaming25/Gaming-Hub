using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GamingHub.Core.Models;

namespace GamingHub.App.Views;

public partial class AppLimitWindow : Window
{
    private bool _allowClose;
    private bool _shown;
    private bool _closed;

    public event Action? ShutdownRequested;

    public AppLimitWindow()
    {
        InitializeComponent();
        Closing += (_, e) =>
        {
            if (!_allowClose)
                e.Cancel = true;
        };
    }

    protected override void OnOpened(EventArgs e)
    {
        _shown = true;
        base.OnOpened(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        base.OnClosed(e);
    }

    public void Update(ScreenTimeBlock block)
    {
        AppNameText.Text = string.IsNullOrWhiteSpace(block.DisplayName)
            ? "Anwendung"
            : block.DisplayName;
        ReasonText.Text = block.Reason;
        ShutdownButton.IsEnabled = true;
        ShutdownButton.Content = "Jetzt ausschalten";
    }

    public void CloseForced()
    {
        _allowClose = true;
        if (_shown && !_closed)
            Close();
    }

    private async void OnShutdownClick(object? sender, RoutedEventArgs e)
    {
        ShutdownButton.IsEnabled = false;
        ShutdownButton.Content = "App wird beendet…";
        ShutdownRequested?.Invoke();

        await Task.Delay(3000);
        if (!IsVisible) return;
        ShutdownButton.IsEnabled = true;
        ShutdownButton.Content = "Jetzt ausschalten";
    }
}
