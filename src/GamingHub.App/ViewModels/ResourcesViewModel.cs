using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamingHub.App.Services;
using GamingHub.Core.Models;
using GamingHub.Core.Services;

namespace GamingHub.App.ViewModels;

public sealed partial class ProcessRow : ObservableObject
{
    public string Name { get; init; } = string.Empty;
    public string CpuText { get; init; } = string.Empty;
    public string MemText { get; init; } = string.Empty;
    public int Pid { get; init; }
    public bool IsOurs { get; init; }

    /// <summary>Erster Klick bewaffnet den Beenden-Knopf, der zweite führt aus.</summary>
    [ObservableProperty] private bool _confirmArmed;

    public bool ShowConfirmButton => !IsOurs;
    public string ConfirmText => ConfirmArmed ? "Erneut klicken zum Beenden!" : "✕ Beenden";
    public string ConfirmTextColor => ConfirmArmed ? "#F87171" : "#8B91AC";

    partial void OnConfirmArmedChanged(bool value)
    {
        OnPropertyChanged(nameof(ConfirmText));
        OnPropertyChanged(nameof(ConfirmTextColor));
    }
}

public sealed record NetworkRow(string Name, string DownloadText, string UploadText);

public partial class ResourcesViewModel : ViewModelBase
{
    private readonly DispatcherTimer _timer;

    [ObservableProperty] private string _cpuName = "…";
    [ObservableProperty] private string _gpuName = "…";
    [ObservableProperty] private string _gpuVramDisplay = "—";
    [ObservableProperty] private string _cpuCoresDisplay = "—";
    [ObservableProperty] private double _cpuUsage;
    [ObservableProperty] private double _ramUsage;
    [ObservableProperty] private double _gpuUsage = -1;
    [ObservableProperty] private string _ramUsedDisplay = "—";
    [ObservableProperty] private string _ramTotalDisplay = "—";
    [ObservableProperty] private string _uptimeDisplay = "—";
    [ObservableProperty] private string _osDisplay = "—";
    [ObservableProperty] private string _currentUserDisplay = "—";
    [ObservableProperty] private string _diskReadDisplay = "0 B/s";
    [ObservableProperty] private string _diskWriteDisplay = "0 B/s";
    [ObservableProperty] private bool _hasNetworkItems;

    // Lokale Windows-Konten (nur für Premium sichtbar)
    public ObservableCollection<LocalUserInfo> LocalUsers { get; } = new();
    public ObservableCollection<LocalGroupInfo> LocalGroups { get; } = new();
    [ObservableProperty] private bool _showSystemAccounts;
    [ObservableProperty] private bool _hasLocalUsers;
    [ObservableProperty] private bool _hasLocalGroups;
    [ObservableProperty] private bool _isLoadingAccounts;
    private bool _accountsLoaded;

    public ObservableCollection<double> CpuHistory { get; } = new();
    public ObservableCollection<double> RamHistory { get; } = new();
    public ObservableCollection<double> NetUpHistory { get; } = new();
    public ObservableCollection<double> NetDownHistory { get; } = new();
    public ObservableCollection<ProcessRow> ProcessItems { get; } = new();
    public ObservableCollection<NetworkRow> NetworkItems { get; } = new();

    // Verlauf (Hintergrund-Aufzeichnung alle 5 Minuten, ca. 1 Woche / Premium 4 Wochen)
    private IReadOnlyList<double> _cpuSeries = Array.Empty<double>();
    private IReadOnlyList<double> _ramSeries = Array.Empty<double>();
    private IReadOnlyList<double> _gpuSeries = Array.Empty<double>();
    private IReadOnlyList<double> _procSeries = Array.Empty<double>();

    public IReadOnlyList<double> CpuSeries => _cpuSeries;
    public IReadOnlyList<double> RamSeries => _ramSeries;
    public IReadOnlyList<double> GpuSeries => _gpuSeries;
    public IReadOnlyList<double> ProcSeries => _procSeries;

    [ObservableProperty] private string _historyStatusText = "Zeichnet alle 5 Minuten auf…";
    [ObservableProperty] private bool _hasHistory;
    [ObservableProperty] private string _historyRetentionText = "Aufbewahrung: 1 Woche (Premium: 4 Wochen)";
    public bool IsPremium => AppServices.Profile.IsPremium;

    public string CpuUsageDisplay => $"{CpuUsage:0} %";
    public string RamUsageDisplay => $"{RamUsage:0} %";
    public string GpuUsageDisplay => GpuUsage < 0 ? "n. v." : $"{GpuUsage:0} %";

    public ObservableCollection<string> GameNames { get; } = new();

    [ObservableProperty] private string? _selectedGame;
    /// <summary>Optionale Versions-Präzisierung für die Web-Recherche
    /// (z. B. „2.1“), damit die Systemanforderungen zur installierten Version passen.</summary>
    [ObservableProperty] private string? _selectedGameVersion;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private string _resultPreset = string.Empty;
    [ObservableProperty] private string _resultSummary = string.Empty;
    [ObservableProperty] private int _resultScore;
    [ObservableProperty] private string _resultAccent = "#888888";
    public ObservableCollection<string> ResultNotes { get; } = new();
    public ObservableCollection<string> ResultBottlenecks { get; } = new();

    [ObservableProperty] private string _webSearchUrl = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isResearching;
    [ObservableProperty] private bool _webSearchEnabled = true;
    [ObservableProperty] private bool _hasWebResult;
    [ObservableProperty] private string _webSummary = string.Empty;
    [ObservableProperty] private string _webVersionNote = string.Empty;
    [ObservableProperty] private string _webStatus = string.Empty;
    public ObservableCollection<WebResearchSource> WebSources { get; } = new();

    public bool HasNoWebResult => !HasWebResult;

    partial void OnHasWebResultChanged(bool value) => OnPropertyChanged(nameof(HasNoWebResult));

    public bool HasNoResult => !HasResult;

    partial void OnHasResultChanged(bool value) => OnPropertyChanged(nameof(HasNoResult));

    public ResourcesViewModel()
    {
        CpuName = AppServices.SystemMonitor.CpuName;
        GpuName = AppServices.SystemMonitor.GpuName;
        GpuVramDisplay = AppServices.SystemMonitor.GpuVramBytes > 0
            ? Format.Bytes((long)AppServices.SystemMonitor.GpuVramBytes)
            : "—";
        CpuCoresDisplay = $"{AppServices.SystemMonitor.CpuCores} Kerne / {AppServices.SystemMonitor.CpuThreads} Threads";

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Update();
        _timer.Start();
        Update();
        _ = LoadFlagsAsync();

        UpdateHistoryRetention();
        ReloadHistory();
        AppServices.History.Changed += ReloadHistory;
        AppServices.Profile.Changed += () => { UpdateHistoryRetention(); ReloadHistory(); UpdateAccountsVisibility(); };
        UpdateAccountsVisibility();
    }

    /// <summary>Zeigt die lokalen Windows-Konten nur Premium-Nutzern und lädt sie
    /// beim ersten Anzeigen (WMI-Abfrage, daher im Hintergrund).</summary>
    private void UpdateAccountsVisibility()
    {
        ShowSystemAccounts = AppServices.Profile.IsPremium;
        if (ShowSystemAccounts && !_accountsLoaded && !IsLoadingAccounts)
            _ = LoadAccountsAsync();
    }

    private async Task LoadAccountsAsync()
    {
        IsLoadingAccounts = true;
        try
        {
            var monitor = AppServices.SystemMonitor;
            var users = await Task.Run(monitor.GetLocalUsers);
            var groups = await Task.Run(monitor.GetLocalGroups);

            LocalUsers.Clear();
            foreach (var u in users) LocalUsers.Add(u);
            LocalGroups.Clear();
            foreach (var g in groups) LocalGroups.Add(g);

            HasLocalUsers = LocalUsers.Count > 0;
            HasLocalGroups = LocalGroups.Count > 0;
            _accountsLoaded = true;
        }
        catch
        {
            // ignore
        }
        finally
        {
            IsLoadingAccounts = false;
        }
    }

    private void UpdateHistoryRetention()
    {
        HistoryRetentionText = IsPremium ? "Premium: 4 Wochen" : "Aufbewahrung: 1 Woche (Premium: 4 Wochen)";
    }

    /// <summary>Lädt die hintergrundaufgezeichneten Verkaufsdaten neu (CSV + Protokoll).</summary>
    private void ReloadHistory()
    {
        var points = AppServices.History.LoadChartData();
        _cpuSeries = points.Select(p => p.CpuCol).ToList();
        _ramSeries = points.Select(p => p.RamCol).ToList();
        _gpuSeries = points.Where(p => p.HasGpu).Select(p => p.GpuCol).ToList();
        double maxProc = points.Count > 0 ? Math.Max(1, points.Max(p => (double)p.ProcessCount)) : 1;
        _procSeries = points.Select(p => Math.Clamp(p.ProcessCount / maxProc * 100.0, 0, 100)).ToList();

        HasHistory = points.Count > 0;
        HistoryStatusText = points.Count == 0
            ? "Noch keine Aufzeichnung vorhanden – die ersten Messdaten entstehen automatisch im Hintergrund (alle 5 Minuten). Das Protokoll wird unter „Datenordner → history“ gespeichert."
            : $"Aufzeichnung aktiv: {points.Count} Messpunkte · letzter Punkt um {points[^1].TimestampUtc.ToLocalTime():HH:mm:ss} · max. {points.Max(p => p.ProcessCount)} Prozesse gleichzeitig.";

        OnPropertyChanged(nameof(CpuSeries));
        OnPropertyChanged(nameof(RamSeries));
        OnPropertyChanged(nameof(GpuSeries));
        OnPropertyChanged(nameof(ProcSeries));
    }

    private async Task LoadFlagsAsync()
    {
        try
        {
            var flags = await AppServices.Chat.LoadFlagsAsync();
            if (flags.TryGetValue("websearch_enabled", out var v))
                WebSearchEnabled = v || AppServices.Profile?.IsAdmin == true;
        }
        catch
        {
            // ignore
        }
    }

    private void Update()
    {
        var s = AppServices.SystemMonitor.Sample();
        CpuUsage = s.CpuUsagePercent;
        RamUsage = s.RamUsagePercent;
        GpuUsage = s.GpuUsagePercent;
        RamUsedDisplay = Format.Bytes((long)s.RamUsedBytes);
        RamTotalDisplay = Format.Bytes((long)s.RamTotalBytes);
        UptimeDisplay = Format.Duration(s.Uptime);
        OsDisplay = s.MachineName + " · " + s.OsDescription;
        CurrentUserDisplay = string.IsNullOrWhiteSpace(s.UserDomain) || string.Equals(s.UserDomain, s.MachineName, StringComparison.OrdinalIgnoreCase)
            ? s.UserName
            : $"{s.UserDomain}\\{s.UserName}";
        DiskReadDisplay = Format.BytesPerSec(s.DiskReadBytesPerSec);
        DiskWriteDisplay = Format.BytesPerSec(s.DiskWriteBytesPerSec);
        OnPropertyChanged(nameof(CpuUsageDisplay));
        OnPropertyChanged(nameof(RamUsageDisplay));
        OnPropertyChanged(nameof(GpuUsageDisplay));

        PushHistory(CpuHistory, Math.Clamp(CpuUsage, 0, 100) * 0.55);
        PushHistory(RamHistory, Math.Clamp(RamUsage, 0, 100) * 0.55);

        double down = s.Adapters.Sum(a => a.DownloadBytesPerSec);
        double up = s.Adapters.Sum(a => a.UploadBytesPerSec);
        PushHistory(NetDownHistory, Math.Min(60, down / (10.0 * 1024 * 1024) * 60));
        PushHistory(NetUpHistory, Math.Min(60, up / (10.0 * 1024 * 1024) * 60));

        NetworkItems.Clear();
        foreach (var a in s.Adapters.OrderByDescending(a => a.DownloadBytesPerSec + a.UploadBytesPerSec))
            NetworkItems.Add(new NetworkRow(a.Name,
                Format.BytesPerSec(a.DownloadBytesPerSec),
                Format.BytesPerSec(a.UploadBytesPerSec)));
        HasNetworkItems = NetworkItems.Count > 0;

        ProcessItems.Clear();
        foreach (var p in s.Processes)
            ProcessItems.Add(new ProcessRow
            {
                Name = p.Name,
                CpuText = $"{p.CpuPercent:0.0} %",
                MemText = Format.Bytes(p.WorkingSetBytes),
                Pid = p.Pid,
                IsOurs = p.IsOurs
            });
    }

    [RelayCommand]
    private async Task StopProcessAsync(ProcessRow? row)
    {
        if (row is null || row.IsOurs || row.Pid <= 0)
        {
            if (row is not null && row.IsOurs)
                StatusText = "Der eigene Gaming-Hub-Prozess kann nicht beendet werden.";
            return;
        }

        // Zwei-Stufen-Bestätigung: erster Klick bewaffnet, zweiter Klick beendet.
        if (!row.ConfirmArmed)
        {
            foreach (var other in ProcessItems)
                other.ConfirmArmed = false;
            row.ConfirmArmed = true;
            StatusText = $"Erneut auf „{row.Name}“ klicken, um ihn wirklich zu beenden.";
            return;
        }

        IsBusy = true;
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(row.Pid);
            LogService.Info($"Beende Prozess {row.Name} (PID {row.Pid}).");
            p.Kill(entireProcessTree: true);
            StatusText = $"Prozess „{row.Name}“ wurde beendet.";
        }
        catch (Exception ex)
        {
            LogService.Warn($"Prozess {row.Name} kann nicht beendet werden.", ex);
            StatusText = $"Prozess „{row.Name}“ konnte nicht beendet werden ({ex.Message}). Möglicherweise sind Administratorrechte nötig.";
        }
        finally
        {
            IsBusy = false;
            if (row is not null) row.ConfirmArmed = false;
        }
    }

    private static void PushHistory(ObservableCollection<double> history, double value)
    {
        history.Add(Math.Max(0, value));
        while (history.Count > 60)
            history.RemoveAt(0);
    }

    public void ReloadGameNames()
    {
        GameNames.Clear();
        foreach (var g in AppServices.Games.Games.OrderBy(g => g.Name))
            GameNames.Add(g.Name);
    }

    [RelayCommand]
    private void Evaluate()
    {
        string name = string.IsNullOrWhiteSpace(SelectedGame) ? "Ausgewähltes Spiel" : SelectedGame!;
        var monitor = AppServices.SystemMonitor;
        var result = AppServices.Compatibility.Evaluate(
            name, monitor.CpuName, monitor.GpuName,
            (ulong)Math.Max(0, monitor.Sample().RamTotalBytes));

        HasResult = true;
        AppServices.Badges.Grant("systemprofi");
        ResultScore = result.Score;
        ResultPreset = result.Recommended switch
        {
            GraphicsPreset.Ultra => "ULTRA",
            GraphicsPreset.High => "HOCH",
            GraphicsPreset.Medium => "MITTEL",
            GraphicsPreset.Low => "NIEDRIG",
            _ => "NICHT SPIELBAR"
        };
        ResultAccent = result.Recommended switch
        {
            GraphicsPreset.Ultra => "#22D3EE",
            GraphicsPreset.High => "#4ADE80",
            GraphicsPreset.Medium => "#FACC15",
            GraphicsPreset.Low => "#FB923C",
            _ => "#F87171"
        };
        ResultSummary = result.Summary;
        WebSearchUrl = "https://www.google.com/search?q=" + Uri.EscapeDataString(name + " systemanforderungen");

        ResultNotes.Clear();
        foreach (var n in result.Notes) ResultNotes.Add(n);
        ResultBottlenecks.Clear();
        foreach (var b in result.Bottlenecks) ResultBottlenecks.Add(b);
    }

    [RelayCommand]
    private void OpenWebSearch() => AppServices.OpenBrowser(WebSearchUrl);

    [RelayCommand]
    private async Task ResearchAsync()
    {
        string name = string.IsNullOrWhiteSpace(SelectedGame) ? "Ausgewähltes Spiel" : SelectedGame!;
        IsResearching = true;
        HasWebResult = false;
        WebSummary = string.Empty;
        WebVersionNote = string.Empty;
        WebSources.Clear();
        WebStatus = $"Recherchiere im Hintergrund zu „{name}\"…";

        try
        {
            var res = await AppServices.WebResearch.ResearchAsync(name, SelectedGameVersion);
            WebSummary = res.Summary;
            WebVersionNote = res.VersionNote ?? string.Empty;
            foreach (var s in res.Sources)
                WebSources.Add(s);
            HasWebResult = res.Found;
            WebStatus = res.Found
                ? (res.IsEstimate
                    ? $"Ergebnisse für „{name}“ geladen (Schätzung ohne präzise Systemanforderungen) – Details: {res.Sources.FirstOrDefault()?.Title}."
                    : $"Ergebnisse für „{name}“ geladen – Systemanforderungen gefunden.")
                : res.Error is null ? "Keine Treffer gefunden." : "Fehler: " + res.Error;
        }
        catch (Exception ex)
        {
            WebStatus = "Fehler: " + ex.Message;
        }
        finally
        {
            IsResearching = false;
        }
    }
}