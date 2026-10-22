using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GamingHub.Core.Models;

namespace GamingHub.App.Views;

public partial class ScreenTimeBlockWindow : Window
{
    private bool _allowClose;

    public event Action<int>? SnoozeRequested;
    public event Action? DisableRequested;

    public ScreenTimeBlockWindow()
    {
        InitializeComponent();
        Closing += (_, e) =>
        {
            if (!_allowClose)
                e.Cancel = true;
        };
    }

    public void Update(ScreenTimeBlock block)
    {
        AppNameText.Text = string.IsNullOrWhiteSpace(block.DisplayName)
            ? "Aktive App"
            : block.DisplayName;
        ReasonText.Text = block.Reason;
    }

    public void CloseForced()
    {
        _allowClose = true;
        Close();
    }

    private void OnSnooze(object? sender, RoutedEventArgs e)
    {
        SnoozeRequested?.Invoke(15);
        CloseForced();
    }

    private void OnDisable(object? sender, RoutedEventArgs e)
    {
        DisableRequested?.Invoke();
        CloseForced();
    }
}
