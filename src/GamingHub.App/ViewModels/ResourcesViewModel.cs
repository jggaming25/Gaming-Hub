using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamingHub.App.Services;
using GamingHub.Core.Models;
using GamingHub.Core.Services;

namespace GamingHub.App.ViewModels;

public sealed record ProcessRow(string Name, string CpuText, string MemText, bool IsOurs);

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
    [ObservableProperty] private string _ramUsedDisplay = "—";
    [ObservableProperty] private string _ramTotalDisplay = "—";
    [ObservableProperty] private string _uptimeDisplay = "—";
    [ObservableProperty] private string _osDisplay = "—";
    [ObservableProperty] private string _diskReadDisplay = "0 B/s";
    [ObservableProperty] private string _diskWriteDisplay = "0 B/s";
    [ObservableProperty] private bool _hasNetworkItems;

    public ObservableCollection<double> CpuHistory { get; } = new();
    public ObservableCollection<double> RamHistory { get; } = new();
    public ObservableCollection<double> NetUpHistory { get; } = new();
    public ObservableCollection<double> NetDownHistory { get; } = new();
    public ObservableCollection<ProcessRow> ProcessItems { get; } = new();
    public ObservableCollection<NetworkRow> NetworkItems { get; } = new();

    public string CpuUsageDisplay => $"{CpuUsage:0} %";
    public string RamUsageDisplay => $"{RamUsage:0} %";

    public ObservableCollection<string> GameNames { get; } = new();

    [ObservableProperty] private string? _selectedGame;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private string _resultPreset = string.Empty;
    [ObservableProperty] private string _resultSummary = string.Empty;
    [ObservableProperty] private int _resultScore;
    [ObservableProperty] private string _resultAccent = "#888888";
    public ObservableCollection<string> ResultNotes { get; } = new();
    public ObservableCollection<string> ResultBottlenecks { get; } = new();

    [ObservableProperty] private string _webSearchUrl = string.Empty;
    [ObservableProperty] private bool _isResearching;
    [ObservableProperty] private bool _webSearchEnabled = true;
    [ObservableProperty] private bool _hasWebResult;
    [ObservableProperty] private string _webSummary = string.Empty;
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
        RamUsedDisplay = Format.Bytes((long)s.RamUsedBytes);
        RamTotalDisplay = Format.Bytes((long)s.RamTotalBytes);
        UptimeDisplay = Format.Duration(s.Uptime);
        OsDisplay = s.MachineName + " · " + s.OsDescription;
        DiskReadDisplay = Format.BytesPerSec(s.DiskReadBytesPerSec);
        DiskWriteDisplay = Format.BytesPerSec(s.DiskWriteBytesPerSec);
        OnPropertyChanged(nameof(CpuUsageDisplay));
        OnPropertyChanged(nameof(RamUsageDisplay));

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
            ProcessItems.Add(new ProcessRow(p.Name,
                $"{p.CpuPercent:0.0} %",
                Format.Bytes(p.WorkingSetBytes),
                p.IsOurs));
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
        WebSources.Clear();
        WebStatus = $"Recherchiere im Hintergrund zu „{name}\"…";

        try
        {
            var res = await AppServices.WebResearch.ResearchAsync(name + " Systemanforderungen");
            WebSummary = res.Summary;
            foreach (var s in res.Sources)
                WebSources.Add(s);
            HasWebResult = res.Found;
            WebStatus = res.Found
                ? $"Ergebnisse für „{name}\" geladen."
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