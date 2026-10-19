using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace GamingHub.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private async void CopyDiscordClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var window = TopLevel.GetTopLevel(this) as Window;
            if (window?.Clipboard is not { } clipboard)
            {
                DiscordCopyHint.Text = "Kopieren nicht möglich.";
                return;
            }

            await clipboard.SetValueAsync(DataFormat.Text, "jlg09");
            DiscordCopyHint.Text = "Discord-Name kopiert.";
        }
        catch
        {
            DiscordCopyHint.Text = "Kopieren nicht möglich.";
        }
    }
}
