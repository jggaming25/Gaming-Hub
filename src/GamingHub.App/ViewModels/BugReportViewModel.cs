using System.Diagnostics;
using Avalonia.Input;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamingHub.App.Services;

namespace GamingHub.App.ViewModels;

public partial class BugReportViewModel : ViewModelBase
{
    public string VersionDisplay => AppInfo.VersionDisplay;

    public string[] Categories { get; } =
    {
        "Absturz / Crash",
        "Feature funktioniert nicht",
        "Darstellungsfehler",
        "Startproblem",
        "Performance",
        "Sonstiges"
    };

    [ObservableProperty] private string _selectedCategory = "Absturz / Crash";
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _steps = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public bool CloudAvailable => AppServices.Bugs.CloudAvailable;

    public string LogPath => LogService.LogFile;

    public string DeliveryHint => CloudAvailable
        ? "Der Report wird direkt an den Gaming-Hub-Bugtracker übermittelt – inklusive des Programm-Logs (wird automatisch angehängt)."
        : "Der Report wird als E-Mail an genzmannjann@gmail.com vorbereitet (inkl. Log-Auszug).";

    private string BuildBody() =>
$@"### Kategorie
{SelectedCategory}

### App-Version
{AppInfo.VersionDisplay}

### Schritte zum Reproduzieren
{Steps}

### Umgebung
- Betriebssystem: {Environment.OSVersion}
- Erstellt über Gaming Hub Bugreport

### Programm-Log (Auszug, automatisch angehängt)
```text
{TrimTo(LogService.Dump(12_000), 12_000)}
```
";

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(Title) || string.IsNullOrWhiteSpace(Steps))
        {
            StatusText = "Bitte Titel und Schritte zum Reproduzieren ausfüllen.";
            return;
        }

        if (CloudAvailable)
        {
            IsBusy = true;
            StatusText = "Sende Bugreport…";
            try
            {
                var (ok, message) = await AppServices.Bugs.SubmitAsync(Title, SelectedCategory, Steps);
                StatusText = message;
                if (ok)
                {
                    AppServices.Badges.Grant("bugreporter");
                    Title = string.Empty;
                    Steps = string.Empty;
                }
            }
            finally
            {
                IsBusy = false;
            }
            return;
        }

        OpenEmailReport();
    }

    private void OpenEmailReport()
    {
        string subject = "[Gaming Hub Bug] " + Title;
        string url = $"mailto:genzmannjann@gmail.com" +
                     $"?subject={Uri.EscapeDataString(subject)}" +
                     $"&body={Uri.EscapeDataString(BuildBody())}";
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            StatusText = "E-Mail mit dem Bugreport wurde zum Absenden geöffnet.";
        }
        catch (Exception ex)
        {
            StatusText = "Konnte E-Mail-Programm nicht öffnen: " + ex.Message;
        }
    }

    [RelayCommand]
    private void Clear()
    {
        Title = string.Empty;
        Steps = string.Empty;
        StatusText = string.Empty;
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            var folder = System.IO.Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrWhiteSpace(folder))
                Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    [RelayCommand]
    private async Task CopyLogAsync()
    {
        try
        {
            var lifetime = Avalonia.Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            if (lifetime?.MainWindow?.Clipboard is { } clipboard)
            {
                await clipboard.SetValueAsync(DataFormat.Text, LogService.Dump());
                StatusText = "Programm-Log in die Zwischenablage kopiert.";
            }
            else
            {
                StatusText = "Keine Zwischenablage verfügbar.";
            }
        }
        catch (Exception ex)
        {
            StatusText = "Log konnte nicht kopiert werden: " + ex.Message;
        }
    }

    private static string TrimTo(string text, int max) => text.Length <= max ? text : "… " + text[^max..];
}
