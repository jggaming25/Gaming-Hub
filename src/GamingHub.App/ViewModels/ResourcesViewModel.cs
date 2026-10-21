using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using Avalonia.Input;
using Avalonia.Input.Platform;
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
    public int Pid { get; init; }
    public bool IsOurs { get; init; }

    [ObservableProperty] private string _cpuText = string.Empty;
    [ObservableProperty] private string _memText = string.Empty;
    [ObservableProperty] private double _cpuPercent;
    [ObservableProperty] private long _memoryBytes;

    [ObservableProperty] private bool _isPinned;

    /// <summary>Erster Klick bewaffnet den Beenden-Knopf, der zweite führt aus.</summary>
    [ObservableProperty] private bool _confirmArmed;

    public bool ShowConfirmButton => !IsOurs;
    public string ConfirmText => ConfirmArmed ? "Erneut klicken zum Beenden!" : "✕ Beenden";
    public string ConfirmTextColor => ConfirmArmed ? "#F87171" : "#8B91AC";
    public string PinText => IsPinned ? "📌" : "📍";
    public string PinTooltip => IsPinned ? "Anheftung lösen" : "Prozess anheften";

    partial void OnConfirmArmedChanged(bool value)
    {
        OnPropertyChanged(nameof(ConfirmText));
        OnPropertyChanged(nameof(ConfirmTextColor));
    }

    partial void OnIsPinnedChanged(bool value)
    {
        OnPropertyChanged(nameof(PinText));
        OnPropertyChanged(nameof(PinTooltip));
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
    [ObservableProperty] private string _machineName = "—";
    [ObservableProperty] private string _currentUserDisplay = "—";
    [ObservableProperty] private string _systemInstallDisplay = "—";
    [ObservableProperty] private string _systemAgeDisplay = "—";
    [ObservableProperty] private string _bootTimeDisplay = "—";
    [ObservableProperty] private string _lastActivationDisplay = "—";
    [ObservableProperty] private string _sinceActivationDisplay = "—";
    [ObservableProperty] private string _motherboardDisplay = "—";
    [ObservableProperty] private string _copyStatusText = string.Empty;
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

    public ObservableCollection<string> ProcessSortOptions { get; } = new() { "CPU", "Arbeitsspeicher", "Name" };
    [ObservableProperty] private string _selectedProcessSort = "CPU";
    [ObservableProperty] private int _processCount;
    private readonly HashSet<string> _pinnedProcessNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(int Pid, string Name), ProcessRow> _processRows = new();
    private IReadOnlyList<ProcessSample> _lastProcesses = Array.Empty<ProcessSample>();
    private DateTime _lastProcessReorder = DateTime.MinValue;

    partial void OnSelectedProcessSortChanged(string value) => RebuildProcessItems(forceReorder: true);

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
    [ObservableProperty] private string _historyRangeText = "—";
    [ObservableProperty] private string _historyAxisStart = string.Empty;
    [ObservableProperty] private string _historyAxisQuarter = string.Empty;
    [ObservableProperty] private string _historyAxisMiddle = string.Empty;
    [ObservableProperty] private string _historyAxisThreeQuarter = string.Empty;
    [ObservableProperty] private string _historyAxisEnd = string.Empty;
    [ObservableProperty] private bool _hasHistory;
    [ObservableProperty] private string _historyRetentionText = "Aufbewahrung: 1 Woche (Premium: 4 Wochen)";
    public bool IsPremium => AppServices.Profile.IsPremium;

    public string CpuUsageDisplay => $"{CpuUsage:0} %";
    public string RamUsageDisplay => $"{RamUsage:0} %";
    public string GpuUsageDisplay => !double.IsFinite(GpuUsage) || GpuUsage < 0 ? "n. v." : $"{GpuUsage:0} %";

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
        HistoryRangeText = points.Count == 0
            ? "—"
            : $"{points[0].TimestampUtc.ToLocalTime():dd.MM. HH:mm}  →  {points[^1].TimestampUtc.ToLocalTime():dd.MM. HH:mm}";
        UpdateHistoryAxis(points);
        HistoryStatusText = points.Count == 0
            ? "Noch keine Aufzeichnung vorhanden – die ersten Messdaten entstehen automatisch im Hintergrund (alle 5 Minuten). Das Protokoll wird unter „Datenordner → history“ gespeichert."
            : $"Aufzeichnung aktiv: {points.Count} Messpunkte · letzter Punkt um {points[^1].TimestampUtc.ToLocalTime():HH:mm:ss} · max. {points.Max(p => p.ProcessCount)} Prozesse gleichzeitig.";

        OnPropertyChanged(nameof(CpuSeries));
        OnPropertyChanged(nameof(RamSeries));
        OnPropertyChanged(nameof(GpuSeries));
        OnPropertyChanged(nameof(ProcSeries));
    }

    private void UpdateHistoryAxis(IReadOnlyList<HistoryPoint> points)
    {
        if (points.Count == 0)
        {
            HistoryAxisStart = HistoryAxisQuarter = HistoryAxisMiddle = HistoryAxisThreeQuarter = HistoryAxisEnd = string.Empty;
            return;
        }

        string At(double fraction)
        {
            int index = (int)Math.Round((points.Count - 1) * fraction);
            return points[Math.Clamp(index, 0, points.Count - 1)].TimestampUtc.ToLocalTime().ToString("dd.MM. HH:mm");
        }

        HistoryAxisStart = At(0);
        HistoryAxisQuarter = At(0.25);
        HistoryAxisMiddle = At(0.5);
        HistoryAxisThreeQuarter = At(0.75);
        HistoryAxisEnd = At(1);
    }

    private async Task LoadFlagsAsync()
    {
        try
        {
            WebSearchEnabled = true || AppServices.Profile?.IsAdmin == true;
        }
        catch
        {
            // ignore
        }
    }

    private bool _sampling;
    private int _researchGeneration;

    private async void Update()
    {
        // Sample() macht WMI-Abfragen und eine Prozess-Enumeration – das darf nicht
        // im UI-Thread laufen, sonst ruckelt die App spürbar. Daher im Hintergrund
        // messen und das Ergebnis anschließend auf den UI-Thread anwenden.
        if (_sampling) return;
        _sampling = true;
        try
        {
            var s = await Task.Run(AppServices.SystemMonitor.Sample);
            ApplySample(s);
        }
        catch
        {
            // Messung fehlgeschlagen – beim nächsten Tick erneut versuchen.
        }
        finally
        {
            _sampling = false;
        }
    }

    private void ApplySample(SystemSnapshot s)
    {
        CpuUsage = s.CpuUsagePercent;
        RamUsage = s.RamUsagePercent;
        GpuUsage = double.IsFinite(s.GpuUsagePercent) ? s.GpuUsagePercent : -1;
        RamUsedDisplay = Format.Bytes((long)s.RamUsedBytes);
        RamTotalDisplay = Format.Bytes((long)s.RamTotalBytes);
        UptimeDisplay = Format.Duration(s.Uptime);
        OsDisplay = s.OsDescription;
        MachineName = s.MachineName;
        MotherboardDisplay = string.IsNullOrWhiteSpace(s.MotherboardName) ? "—" : s.MotherboardName;
        BootTimeDisplay = s.BootTime.ToString("dd.MM.yyyy HH:mm");
        if (s.LastActivationUtc is { } activation)
        {
            LastActivationDisplay = activation.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
            SinceActivationDisplay = Format.Duration(DateTime.UtcNow - activation);
        }
        else
        {
            LastActivationDisplay = BootTimeDisplay;
            SinceActivationDisplay = UptimeDisplay;
        }
        SystemInstallDisplay = s.SystemInstallUtc is { } installed
            ? installed.ToLocalTime().ToString("dd.MM.yyyy")
            : "—";
        SystemAgeDisplay = s.SystemInstallUtc is { } since
            ? FormatSystemAge(DateTime.UtcNow - since)
            : "—";
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

        _lastProcesses = s.Processes;
        RebuildProcessItems();
    }

    /// <summary>Baut die Prozessliste anhand der gewählten Sortierung neu auf.
    /// Angeheftete Prozesse stehen immer oben. Werte werden jede Sekunde
    /// aktualisiert, die Reihenfolge aber nur alle 3 Sekunden neu bestimmt,
    /// damit die Liste beim Scrollen nicht ständig springt.</summary>
    private void RebuildProcessItems(bool forceReorder = false)
    {
        IEnumerable<ProcessSample> query = SelectedProcessSort switch
        {
            "Arbeitsspeicher" => _lastProcesses
                .OrderByDescending(p => p.WorkingSetBytes)
                .ThenByDescending(p => p.CpuPercent),
            "Name" => _lastProcesses
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
            _ => _lastProcesses
                .OrderByDescending(p => p.CpuPercent)
                .ThenByDescending(p => p.WorkingSetBytes)
        };

        var ordered = query.ToList();
        if (_pinnedProcessNames.Count > 0)
            ordered = ordered.OrderByDescending(p => _pinnedProcessNames.Contains(p.Name)).ToList();

        ProcessCount = ordered.Count;

        var samples = ordered.ToDictionary(p => (p.Pid, p.Name));
        bool anyArmed = ProcessItems.Any(r => r.ConfirmArmed);
        bool reorder = forceReorder || (!anyArmed && (DateTime.UtcNow - _lastProcessReorder).TotalSeconds >= 3);

        if (!reorder)
        {
            foreach (var row in ProcessItems)
                if (samples.TryGetValue((row.Pid, row.Name), out var sample))
                    ApplyToRow(row, sample);
            return;
        }

        var alive = samples.Keys.ToHashSet();
        foreach (var key in _processRows.Keys.Where(k => !alive.Contains(k)).ToList())
            _processRows.Remove(key);

        if (ProcessItems.Count == ordered.Count)
        {
            for (int i = 0; i < ordered.Count; i++)
            {
                var row = GetOrCreateRow(ordered[i]);
                ApplyToRow(row, ordered[i]);
                int current = ProcessItems.IndexOf(row);
                if (current < 0) ProcessItems.Insert(i, row);
                else if (current != i) ProcessItems.Move(current, i);
            }

            var aliveSet = samples.Keys.ToHashSet();
            for (int i = ProcessItems.Count - 1; i >= 0; i--)
            {
                var row = ProcessItems[i];
                if (!aliveSet.Contains((row.Pid, row.Name)))
                    ProcessItems.RemoveAt(i);
            }
        }
        else
        {
            ProcessItems.Clear();
            foreach (var sample in ordered)
            {
                var row = GetOrCreateRow(sample);
                ApplyToRow(row, sample);
                ProcessItems.Add(row);
            }
        }

        _lastProcessReorder = DateTime.UtcNow;
    }

    private ProcessRow GetOrCreateRow(ProcessSample sample)
    {
        var key = (sample.Pid, sample.Name);
        if (!_processRows.TryGetValue(key, out var row))
        {
            row = new ProcessRow { Name = sample.Name, Pid = sample.Pid, IsOurs = sample.IsOurs };
            _processRows[key] = row;
        }
        return row;
    }

    private void ApplyToRow(ProcessRow row, ProcessSample sample)
    {
        row.CpuText = $"{sample.CpuPercent:0.0} %";
        row.MemText = Format.Bytes(sample.WorkingSetBytes);
        row.CpuPercent = sample.CpuPercent;
        row.MemoryBytes = sample.WorkingSetBytes;
        row.IsPinned = _pinnedProcessNames.Contains(sample.Name);
    }

    [RelayCommand]
    private void TogglePinProcess(ProcessRow? row)
    {
        if (row is null || string.IsNullOrWhiteSpace(row.Name)) return;

        if (!_pinnedProcessNames.Remove(row.Name))
            _pinnedProcessNames.Add(row.Name);

        RebuildProcessItems(forceReorder: true);
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

        // Alte Web-Recherche ausblenden, damit nie Ergebnisse des vorherigen
        // Spiels (z. B. OMSI 1 bei OMSI 2) stehen bleiben.
        _researchGeneration++;
        HasWebResult = false;
        WebSummary = string.Empty;
        WebVersionNote = string.Empty;
        WebStatus = string.Empty;
        WebSources.Clear();

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
    private async Task CopySystemInfoAsync()
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Gaming Hub – Systeminformationen");
            sb.AppendLine($"Computer: {MachineName}");
            sb.AppendLine($"CPU: {CpuName} ({CpuCoresDisplay})");
            sb.AppendLine($"GPU: {GpuName} (VRAM: {GpuVramDisplay})");
            sb.AppendLine($"RAM: {RamUsedDisplay} von {RamTotalDisplay} ({RamUsageDisplay})");
            sb.AppendLine($"Mainboard: {MotherboardDisplay}");
            sb.AppendLine($"Windows: {OsDisplay}");
            sb.AppendLine($"Windows installiert: {SystemInstallDisplay}");
            sb.AppendLine($"Windows-Alter: {SystemAgeDisplay}");
            sb.AppendLine($"Letzte Aktivierung: {LastActivationDisplay}");
            sb.AppendLine($"Seit letzter Aktivierung: {SinceActivationDisplay}");
            sb.AppendLine($"Windows-Betriebszeit (inkl. Standby): {UptimeDisplay}");
            sb.AppendLine($"Letzter vollständiger Start: {BootTimeDisplay}");
            sb.AppendLine($"Angemeldet als: {CurrentUserDisplay}");
            sb.AppendLine($"Datum der Abfrage: {DateTime.Now:dd.MM.yyyy HH:mm:ss}");

            var lifetime = Avalonia.Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            if (lifetime?.MainWindow?.Clipboard is { } clipboard)
            {
                await clipboard.SetValueAsync(DataFormat.Text, sb.ToString());
                CopyStatusText = "Systeminformationen in die Zwischenablage kopiert.";
            }
            else
            {
                CopyStatusText = "Keine Zwischenablage verfügbar.";
            }
        }
        catch (Exception ex)
        {
            CopyStatusText = "Kopieren fehlgeschlagen: " + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenWebSearch() => AppServices.OpenBrowser(WebSearchUrl);

    [RelayCommand]
    private async Task ResearchAsync()
    {
        string name = string.IsNullOrWhiteSpace(SelectedGame) ? "Ausgewähltes Spiel" : SelectedGame!;
        int generation = ++_researchGeneration;
        IsResearching = true;
        HasWebResult = false;
        WebSummary = string.Empty;
        WebVersionNote = string.Empty;
        WebSources.Clear();
        WebStatus = $"Recherchiere im Hintergrund zu „{name}\"…";

        try
        {
            var res = await AppServices.WebResearch.ResearchAsync(name, SelectedGameVersion);

            // Während der Recherche wurde ein anderes Spiel ausgewählt oder
            // neu berechnet: Ergebnis verwerfen, statt es falsch zuzuordnen.
            if (generation != _researchGeneration ||
                !string.Equals(name, SelectedGame, StringComparison.Ordinal))
                return;

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

    private static string FormatSystemAge(TimeSpan age)
    {
        if (age.TotalDays < 1)
            return $"{Math.Max(1, (int)age.TotalHours)} Stunden";
        int days = (int)age.TotalDays;
        if (days < 60)
            return $"{days} Tage";
        int years = days / 365;
        int rest = days % 365;
        return rest == 0 ? $"{years} Jahr(e)" : $"{years} Jahr(e), {rest} Tage";
    }
}