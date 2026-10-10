using Avalonia.Controls;
using Avalonia.Interactivity;

namespace GamingHub.App.Views;

public partial class DisclaimerWindow : Window
{
    public bool Accepted { get; private set; }

    public DisclaimerWindow()
    {
        InitializeComponent();
    }

    private void OnAcceptClick(object? sender, RoutedEventArgs e)
    {
        Accepted = true;
        Close();
    }

    private void OnDeclineClick(object? sender, RoutedEventArgs e)
    {
        Accepted = false;
        Close();
    }
}
