using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamingHub.App.Services;
using GamingHub.Core.Models;

namespace GamingHub.App.ViewModels;

public sealed partial class InternetStep : ObservableObject
{
    public string Name { get; init; } = string.Empty;
    public string Glyph { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isComplete;
    [ObservableProperty] private bool _isFailed;
    [ObservableProperty] private string _latencyText = "–";
    [ObservableProperty] private string _statusText = "Wartet…";
    [ObservableProperty] private string _statusColor = "#8B91AC";

    partial void OnIsFailedChanged(bool value)
    {
        if (value) { StatusText = "Fehlgeschlagen"; StatusColor = "#F87171"; }
    }
    partial void OnIsCompleteChanged(bool value)
    {
        if (value && !IsFailed) { StatusText = "OK"; StatusColor = "#4ADE80"; }
    }
}

/// <summary>Eine Datums-Beschriftung an einer festen X-Position im Verlaufs-Diagramm.</summary>
public sealed class HistoryDateLabel
{
    public double X { get; init; }
    public string Text { get; init; } = string.Empty;
}

public partial class InternetCheckerViewModel : ViewModelBase
{
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _testTimer;
    private int _testStepIndex = -1;

    public ObservableCollection<InternetStep> Steps { get; } = new()
    {
        new InternetStep { Name = "Gateway (Router)", Glyph = "🏠", Description = "Lokales Gateway / Router erreichen" },
        new InternetStep { Name = "DNS-Server", Glyph = "🔍", Description = "DNS-Auflösung (z. B. 1.1.1.1)" },
        new InternetStep { Name = "Internet (8.8.8.8)", Glyph = "🌐", Description = "Öffentliches Internet erreichbar" },
        new InternetStep { Name = "HTTP (google.com)", Glyph = "🔒", Description = "HTTPS-Verbindung zu Webserver" }
    };

    [ObservableProperty] private bool _isTesting;
    [ObservableProperty] private string _testButtonText = "Internet-Test starten";
    [ObservableProperty] private string _overallStatus = "Noch nicht getestet";
    [ObservableProperty] private string _overallStatusColor = "#8B91AC";
    [ObservableProperty] private string _assessmentText = "Führe einen Test durch, um eine Einschätzung zu erhalten.";
    [ObservableProperty] private bool _isOnline;
    [ObservableProperty] private string _currentLatencyText = "–";
    [ObservableProperty] private string _packetLossText = "–";
    [ObservableProperty] private string _gatewayIpText = "–";
    public string GatewayIp => AppServices.Internet.GatewayIp ?? "–";

    // Verlauf (4 Wochen / Premium)
    public ObservableCollection<double> LatencyHistory { get; } = new();
    public ObservableCollection<double> OnlineHistory { get; } = new(); // 1=online, 0=offline
    public ObservableCollection<double> GatewayLatencyHistory { get; } = new();
    public ObservableCollection<double> DnsLatencyHistory { get; } = new();

    /// <summary>Datums-Beschriftungen unter dem Diagramm (scrollbar).</summary>
    public ObservableCollection<HistoryDateLabel> DateLabels { get; } = new();

    /// <summary>Breite des Diagramms in Pixeln – wächst mit der Datenmenge und
    /// macht das Diagramm horizontal scrollbar (1 Woche frei, 4 Wochen Premium).</summary>
    [ObservableProperty] private double _chartWidth = 600;

    // Feste Abstände (Pixel) für die nicht gestreckten Diagramme.
    private const double ChartSpacing = 8;
    private const int MaxChartPoints = 300;

    [ObservableProperty] private bool _hasHistory;
    [ObservableProperty] private string _historyRetentionText = "Aufbewahrung: 1 Woche (Premium: 4 Wochen)";
    [ObservableProperty] private string _historyStatusText = "Keine Daten aufgezeichnet.";

    [ObservableProperty] private bool _showOfflineHint;

    public bool CanUse => AppServices.Profile.IsPremium || AppServices.Profile.IsAdmin;
    public bool ShowOfflineMode => AppServices.Internet.OfflineMode;

    public InternetCheckerViewModel()
    {
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _refreshTimer.Start();

        _testTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _testTimer.Tick += async (_, _) => await RunTestStepAsync();

        AppServices.Internet.Changed += () => Dispatcher.UIThread.Post(UpdateLiveValues);
        UpdateLiveValues();
        ReloadHistory();
    }

    private void UpdateLiveValues()
    {
        IsOnline = AppServices.Internet.IsOnline;
        CurrentLatencyText = AppServices.Internet.CurrentLatencyMs >= 0 ? $"{AppServices.Internet.CurrentLatencyMs} ms" : "–";
        PacketLossText = $"{AppServices.Internet.PacketLossPercent:0} %";
        GatewayIpText = AppServices.Internet.GatewayIp ?? "–";
        ShowOfflineHint = AppServices.Internet.OfflineMode;

        // Update step statuses based on live data (rough) - but not during manual test
        if (!IsTesting && Steps.Count >= 4)
        {
            Steps[0].IsComplete = AppServices.Internet.GatewayIp != null;
        }
    }

    private async Task RefreshAsync()
    {
        await AppServices.Internet.CheckAsync();
        UpdateLiveValues();
    }

    [RelayCommand]
    private async Task RunTestAsync()
    {
        if (IsTesting) return;
        IsTesting = true;
        TestButtonText = "Test läuft…";
        OverallStatus = "Test läuft…";
        OverallStatusColor = "#FACC15";
        _testStepIndex = 0;

        foreach (var s in Steps)
        {
            s.IsActive = false;
            s.IsComplete = false;
            s.IsFailed = false;
            s.LatencyText = "–";
            s.StatusText = "Wartet…";
            s.StatusColor = "#8B91AC";
        }

        // Pause live updates during manual test
        _refreshTimer.Stop();

        _testTimer.Start();
    }

    private async Task RunTestStepAsync()
    {
        if (_testStepIndex >= Steps.Count)
        {
            _testTimer.Stop();
            IsTesting = false;
            TestButtonText = "Internet-Test starten";
            EvaluateResult();
            return;
        }

        var step = Steps[_testStepIndex];
        step.IsActive = true;
        step.StatusText = "Prüfe…";
        step.StatusColor = "#FACC15";

        bool ok = false;
        int latency = -1;

        try
        {
            switch (_testStepIndex)
            {
                case 0: // Gateway
                    var gw = AppServices.Internet.GatewayIp;
                    if (!string.IsNullOrEmpty(gw))
                    {
                        var (o, l) = await PingHostAsync(gw, 3);
                        ok = o; latency = l ?? -1;
                    }
                    else
                    {
                        ok = false;
                    }
                    break;
                case 1: // DNS
                    var dns = GetDnsServerIp();
                    if (!string.IsNullOrEmpty(dns))
                    {
                        var (o, l) = await PingHostAsync(dns, 3);
                        ok = o; latency = l ?? -1;
                    }
                    else ok = false;
                    break;
                case 2: // Internet
                    var (o2, l2) = await PingHostAsync("8.8.8.8", 3);
                    ok = o2; latency = l2 ?? -1;
                    break;
                case 3: // HTTP
                    var (o3, l3) = await HttpHeadAsync("https://www.google.com", TimeSpan.FromSeconds(5));
                    ok = o3; latency = l3 ?? -1;
                    break;
            }
        }
        catch
        {
            ok = false;
        }

        step.IsActive = false;
        step.IsComplete = ok;
        step.IsFailed = !ok;
        step.LatencyText = latency >= 0 ? $"{latency} ms" : "–";

        _testStepIndex++;
    }

    private void EvaluateResult()
    {
        int completed = Steps.Count(s => s.IsComplete);
        if (completed == Steps.Count)
        {
            OverallStatus = "Alle Schritte OK – Internet funktioniert.";
            OverallStatusColor = "#4ADE80";
        }
        else if (completed == 0)
        {
            OverallStatus = "Kein Schritt erfolgreich – wahrscheinlich offline.";
            OverallStatusColor = "#F87171";
        }
        else
        {
            OverallStatus = $"{completed}/{Steps.Count} Schritte OK – teilweise Probleme.";
            OverallStatusColor = "#FB923C";
        }

        AssessmentText = AppServices.Internet.GetOutageAssessment();
        ReloadHistory();
    }

    private static async Task<(bool Ok, int? LatencyMs)> PingHostAsync(string host, int count)
    {
        try
        {
            using var ping = new System.Net.NetworkInformation.Ping();
            int success = 0;
            long totalLatency = 0;
            for (int i = 0; i < count; i++)
            {
                try
                {
                    var reply = await ping.SendPingAsync(host, 2000);
                    if (reply.Status == IPStatus.Success)
                    {
                        success++;
                        totalLatency += reply.RoundtripTime;
                    }
                }
                catch { }
            }
            if (success > 0) return (true, (int)(totalLatency / success));
            return (false, null);
        }
        catch { return (false, null); }
    }

    private static async Task<(bool Ok, int? LatencyMs)> HttpHeadAsync(string url, TimeSpan timeout)
    {
        try
        {
            using var http = new HttpClient { Timeout = timeout };
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            var sw = Stopwatch.StartNew();
            var resp = await http.SendAsync(req);
            sw.Stop();
            if (resp.IsSuccessStatusCode) return (true, (int)sw.ElapsedMilliseconds);
            return (false, null);
        }
        catch { return (false, null); }
    }

    private string? GetDnsServerIp()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                var props = ni.GetIPProperties();
                foreach (var dns in props.DnsAddresses)
                    if (dns.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        return dns.ToString();
            }
        }
        catch { }
        return null;
    }

    private void ReloadHistory()
    {
        var all = AppServices.Internet.LoadChartData();
        var points = Downsample(all, MaxChartPoints);

        LatencyHistory.Clear();
        OnlineHistory.Clear();
        GatewayLatencyHistory.Clear();
        DnsLatencyHistory.Clear();
        DateLabels.Clear();

        foreach (var p in points)
        {
            LatencyHistory.Add(p.EffectiveLatencyMs >= 0 ? p.EffectiveLatencyMs : double.NaN);
            OnlineHistory.Add(p.IsOnline ? 1 : 0);
            GatewayLatencyHistory.Add(p.GatewayLatencyMs >= 0 ? p.GatewayLatencyMs : double.NaN);
            DnsLatencyHistory.Add(p.DnsLatencyMs >= 0 ? p.DnsLatencyMs : double.NaN);
        }

        ChartWidth = points.Count > 0
            ? Math.Max(600, (points.Count - 1) * ChartSpacing + ChartSpacing)
            : 600;

        // Datums-Beschriftungen gleichmäßig über die Breite verteilen.
        if (points.Count > 1)
        {
            int labelCount = Math.Clamp((int)(ChartWidth / 110), 2, 20);
            int step = Math.Max(1, points.Count / labelCount);
            for (int i = 0; i < points.Count; i += step)
            {
                DateLabels.Add(new HistoryDateLabel
                {
                    X = i * ChartSpacing,
                    Text = points[i].TimestampUtc.ToLocalTime().ToString("dd.MM HH:mm")
                });
            }
        }

        HasHistory = all.Count > 0;
        bool isPremium = AppServices.Profile.IsPremium;
        HistoryRetentionText = isPremium ? "Premium: 4 Wochen" : "Aufbewahrung: 1 Woche (Premium: 4 Wochen)";
        HistoryStatusText = all.Count == 0
            ? "Noch keine Internet-Daten aufgezeichnet (alle 15 Sekunden ein Check)."
            : $"{all.Count} Messpunkte · letzte {all[^1].TimestampUtc.ToLocalTime():HH:mm:ss} · " +
              (all.Count(p => !p.IsOnline) > 0 ? $"{all.Count(p => !p.IsOnline)} Ausfälle" : "keine Ausfälle") +
              $" · Diagramm mit {points.Count} Punkten";
    }

    /// <summary>Fasst sehr viele Messpunkte zu gleich großen Zeitfenstern zusammen,
    /// damit das Diagramm flüssig bleibt und trotzdem den ganzen Zeitraum zeigt.</summary>
    private static List<InternetHistoryPoint> Downsample(List<InternetHistoryPoint> points, int maxPoints)
    {
        if (points.Count <= maxPoints) return points;

        int bucket = (int)Math.Ceiling(points.Count / (double)maxPoints);
        var result = new List<InternetHistoryPoint>(maxPoints + 1);

        for (int i = 0; i < points.Count; i += bucket)
        {
            int end = Math.Min(i + bucket, points.Count);
            var slice = points.GetRange(i, end - i);

            result.Add(new InternetHistoryPoint
            {
                TimestampUtc = slice[0].TimestampUtc,
                GatewayOk = slice.Any(p => p.GatewayOk),
                GatewayLatencyMs = Average(slice, p => p.GatewayLatencyMs),
                DnsOk = slice.Any(p => p.DnsOk),
                DnsLatencyMs = Average(slice, p => p.DnsLatencyMs),
                InternetOk = slice.Any(p => p.InternetOk),
                InternetLatencyMs = Average(slice, p => p.InternetLatencyMs),
                HttpOk = slice.Any(p => p.HttpOk),
                HttpLatencyMs = Average(slice, p => p.HttpLatencyMs),
            });
        }

        return result;
    }

    private static int Average(List<InternetHistoryPoint> slice, Func<InternetHistoryPoint, int> selector)
    {
        var valid = slice.Select(selector).Where(v => v >= 0).ToList();
        return valid.Count == 0 ? -1 : (int)Math.Round(valid.Average());
    }
}