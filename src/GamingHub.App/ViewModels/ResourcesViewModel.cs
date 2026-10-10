using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamingHub.App.Services;
using GamingHub.Core.Models;

namespace GamingHub.App.ViewModels;

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

    public ObservableCollection<string> GameNames { get; } = new();

    [ObservableProperty] private string? _selectedGame;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private string _resultPreset = string.Empty;
    [ObservableProperty] private string _resultSummary = string.Empty;
    [ObservableProperty] private int _resultScore;
    [ObservableProperty] private string _resultAccent = "#888888";
    public ObservableCollection<string> ResultNotes { get; } = new();
    public ObservableCollection<string> ResultBottlenecks { get; } = new();

    public string CpuUsageDisplay => $"{CpuUsage:0} %";
    public string RamUsageDisplay => $"{RamUsage:0} %";

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
    }

    private void Update()
    {
        var s = AppServices.SystemMonitor.Sample();
        CpuUsage = s.CpuUsagePercent;
        RamUsage = s.RamUsagePercent;
        RamUsedDisplay = Format.Bytes((long)s.RamUsedBytes);
        RamTotalDisplay = Format.Bytes((long)s.RamTotalBytes);
        OnPropertyChanged(nameof(CpuUsageDisplay));
        OnPropertyChanged(nameof(RamUsageDisplay));
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

        ResultNotes.Clear();
        foreach (var n in result.Notes) ResultNotes.Add(n);
        ResultBottlenecks.Clear();
        foreach (var b in result.Bottlenecks) ResultBottlenecks.Add(b);
    }
}
