using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamingHub.App.Services;

namespace GamingHub.App.ViewModels;

/// <summary>Eine Zeile in der Download-Aufschlüsselung pro Version.</summary>
public sealed record VersionDownloadRow(
    string Version,
    string DateDisplay,
    string DownloadsDisplay,
    long Downloads,
    double SharePercent);

/// <summary>
/// Admin-only statistics: downloads of all versions, registered users and the
/// lifetime of the app (including live seconds).
/// </summary>
public partial class AdminStatsViewModel : ViewModelBase
{
    private readonly AdminStatsService _service = new();
    private readonly DispatcherTimer _timer;
    private DateTimeOffset? _appCreatedAt;
    private bool _loaded;

    public ObservableCollection<VersionDownloadRow> Versions { get; } = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "Statistik wird geladen…";
    [ObservableProperty] private string _totalDownloadsDisplay = "—";
    [ObservableProperty] private string _registeredUsersDisplay = "—";
    [ObservableProperty] private string _appCreatedDisplay = "—";
    [ObservableProperty] private string _appAgeDisplay = "—";
    [ObservableProperty] private string _appAgeLongDisplay = string.Empty;
    [ObservableProperty] private string _downloadsError = string.Empty;
    [ObservableProperty] private string _usersError = string.Empty;
    [ObservableProperty] private string _versionsSummary = string.Empty;

    public bool HasDownloadsError => !string.IsNullOrWhiteSpace(DownloadsError);
    public bool HasUsersError => !string.IsNullOrWhiteSpace(UsersError);
    public bool HasVersions => Versions.Count > 0;
    public bool IsAdmin => AppServices.Profile?.IsAdmin == true;

    partial void OnDownloadsErrorChanged(string value) => OnPropertyChanged(nameof(HasDownloadsError));
    partial void OnUsersErrorChanged(string value) => OnPropertyChanged(nameof(HasUsersError));

    public AdminStatsViewModel()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => UpdateAge();
        _timer.Start();
    }

    /// <summary>Lädt die Statistik beim ersten Öffnen (nur für Admins erreichbar).</summary>
    public void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        _loaded = true;
        if (IsLoading) return;
        IsLoading = true;
        StatusText = "Statistik wird geladen…";

        try
        {
            var stats = await _service.LoadAsync();
            _appCreatedAt = stats.AppCreatedAt;

            TotalDownloadsDisplay = stats.TotalDownloads.ToString("N0");
            RegisteredUsersDisplay = stats.UsersAvailable
                ? stats.RegisteredUsers.ToString("N0")
                : "n. v.";
            AppCreatedDisplay = stats.AppCreatedAt is null
                ? "Unbekannt"
                : stats.AppCreatedAt.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm");

            DownloadsError = stats.DownloadsError ?? string.Empty;
            UsersError = stats.UsersError ?? string.Empty;

            Versions.Clear();
            long max = stats.Versions.Count == 0 ? 0 : stats.Versions.Max(v => v.Downloads);
            foreach (var v in stats.Versions)
            {
                Versions.Add(new VersionDownloadRow(
                    "v" + v.Version,
                    v.PublishedAt is null ? "—" : v.PublishedAt.Value.ToLocalTime().ToString("dd.MM.yyyy"),
                    v.Downloads.ToString("N0"),
                    v.Downloads,
                    max <= 0 ? 0 : Math.Round(v.Downloads * 100.0 / max, 1)));
            }

            VersionsSummary = stats.Versions.Count == 0
                ? "Keine Releases gefunden."
                : $"{stats.Versions.Count} Versionen · {stats.Versions.Sum(v => v.Downloads):N0} Downloads";

            OnPropertyChanged(nameof(HasVersions));
            UpdateAge();
            StatusText = DateTime.Now.ToString("HH:mm:ss") + " Uhr aktualisiert";
        }
        catch (Exception ex)
        {
            StatusText = "Fehler beim Laden: " + ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdateAge()
    {
        if (_appCreatedAt is null) return;
        AppAgeDisplay = Format.Age(_appCreatedAt.Value);
        AppAgeLongDisplay = Format.AgeLong(_appCreatedAt.Value);
    }
}
