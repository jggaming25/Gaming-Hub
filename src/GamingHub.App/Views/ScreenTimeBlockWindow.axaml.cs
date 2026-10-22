using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using GamingHub.App.Services;
using GamingHub.Core.Models;

namespace GamingHub.App.Views;

public partial class ScreenTimeBlockWindow : Window
{
    private readonly CancellationTokenSource _cts = new();
    private bool _allowClose;
    private bool _busy;

    public event Action<int>? ExtensionRequested;
    public event Action? LogoutRequested;

    public ScreenTimeBlockWindow() : this(true)
    {
    }

    public ScreenTimeBlockWindow(bool primary)
    {
        InitializeComponent();
        Closing += (_, e) =>
        {
            if (!_allowClose)
                e.Cancel = true;
        };

        if (!primary)
        {
            Content = null;
            Background = new SolidColorBrush(Color.Parse("#6B7280"));
        }
    }

    public void Update(ScreenTimeBlock block)
    {
        if (AppNameText is null || ReasonText is null) return;
        AppNameText.Text = string.IsNullOrWhiteSpace(block.DisplayName)
            ? "Bildschirmzeit"
            : block.DisplayName;
        ReasonText.Text = block.Reason;
    }

    public void CloseForced()
    {
        _allowClose = true;
        try { _cts.Cancel(); } catch { /* ignore */ }
        Close();
    }

    private async void OnExtensionClick(object? sender, RoutedEventArgs e)
    {
        if (ReadMinutes(sender) is { } minutes)
            await RequestExtensionAsync(minutes);
    }

    private async void OnUntilMidnightClick(object? sender, RoutedEventArgs e)
    {
        var minutes = (int)Math.Ceiling((DateTime.Today.AddDays(1) - DateTime.Now).TotalMinutes);
        await RequestExtensionAsync(Math.Max(1, minutes));
    }

    private async void OnHoursClick(object? sender, RoutedEventArgs e)
    {
        if (ReadMinutes(HoursBox.SelectedItem) is { } minutes)
            await RequestExtensionAsync(minutes);
    }

    private async void OnCustomClick(object? sender, RoutedEventArgs e)
    {
        if (int.TryParse(CustomMinutesBox.Text, out int minutes) && minutes > 0)
            await RequestExtensionAsync(minutes);
        else
            StatusText.Text = "Bitte eine gültige Minutenzahl eingeben.";
    }

    private void OnLogoutClick(object? sender, RoutedEventArgs e)
    {
        LogoutRequested?.Invoke();
        CloseForced();
    }

    private static int? ReadMinutes(object? item)
    {
        object? tag = item switch
        {
            Button button => button.Tag,
            ComboBoxItem comboItem => comboItem.Tag,
            _ => null
        };

        return tag switch
        {
            int value => value,
            string text when int.TryParse(text, out int value) => value,
            _ => null
        };
    }

    private async Task RequestExtensionAsync(int minutes)
    {
        if (_busy) return;
        _busy = true;
        ActionsPanel.IsEnabled = false;

        try
        {
            StatusText.Text = "Windows-Adminbestätigung wird geöffnet…";
            bool approved = await ScreenTimeElevation.RequestAdminApprovalAsync();
            if (!approved)
            {
                StatusText.Text = "Die Adminbestätigung wurde abgebrochen. Es wurde keine Zeit freigegeben.";
                return;
            }

            for (int remaining = 20; remaining > 0; remaining--)
            {
                StatusText.Text = $"Adminbestätigung erfolgreich. Freigabe wird in {remaining} Sekunden aktiv…";
                await Task.Delay(1000, _cts.Token);
            }

            StatusText.Text = "Freigabe wird angewendet…";
            ExtensionRequested?.Invoke(Math.Clamp(minutes, 1, 24 * 60));
            CloseForced();
        }
        catch (OperationCanceledException)
        {
            // Fenster wurde inzwischen geschlossen.
        }
        catch (Exception)
        {
            StatusText.Text = "Die Freigabe konnte nicht angewendet werden.";
        }
        finally
        {
            _busy = false;
            if (IsVisible)
                ActionsPanel.IsEnabled = true;
        }
    }
}
