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
    [ObservableProperty] private bool _isBusy;

    public bool CloudAvailable => AppServices.Bugs.CloudAvailable;

    public string DeliveryHint => CloudAvailable
        ? "Der Report wird direkt an den Gaming-Hub-Bugtracker übermittelt."
        : "Supabase ist nicht konfiguriert – der Report wird als vorausgefülltes GitHub-Issue geöffnet.";

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

        OpenGitHubIssue();
    }

    private void OpenGitHubIssue()
    {
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
