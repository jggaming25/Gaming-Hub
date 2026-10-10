using System.Diagnostics;
using System.Net;
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

    private string BuildBody()
    {
        return
$@"### Kategorie
{SelectedCategory}

### App-Version
{AppInfo.VersionDisplay}

### Schritte zum Reproduzieren
{Steps}

### Zusätzliche Infos
- Betriebssystem: {Environment.OSVersion}
- Erstellt über Gaming Hub Bugreport
";
    }

    [RelayCommand]
    private void Submit()
    {
        if (string.IsNullOrWhiteSpace(Title) || string.IsNullOrWhiteSpace(Steps))
        {
            StatusText = "Bitte Titel und Schritte zum Reproduzieren ausfüllen.";
            return;
        }

        string url = $"{AppInfo.RepositoryUrl}/issues/new" +
                     $"?title={WebUtility.UrlEncode("[Bug] " + Title)}" +
                     $"&body={WebUtility.UrlEncode(BuildBody())}" +
                     $"&labels={WebUtility.UrlEncode("bug")}";

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            StatusText = "Bugreport wurde im Browser zum Absenden geöffnet.";
        }
        catch (Exception ex)
        {
            StatusText = "Konnte Browser nicht öffnen: " + ex.Message;
        }
    }

    [RelayCommand]
    private void Clear()
    {
        Title = string.Empty;
        Steps = string.Empty;
        StatusText = string.Empty;
    }
}
