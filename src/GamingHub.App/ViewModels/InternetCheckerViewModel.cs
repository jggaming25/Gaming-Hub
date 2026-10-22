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
    public bool IsPremiumOnly { get; init; }

    [ObservableProperty] private bool _isUnlocked = true;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isComplete;
    [ObservableProperty] private bool _isFailed;
    [ObservableProperty] private string _latencyText = "–";
    [ObservableProperty] private string _statusText = "Wartet…";
    [ObservableProperty] private string _statusColor = "#8B91AC";
    [ObservableProperty] private string _detailText = string.Empty;

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
        new InternetStep { Name = "Netzwerkadapter", Glyph = "🔌", Description = "Aktiver LAN-/WLAN-Adapter vorhanden" },
        new InternetStep { Name = "Lokale IP-Adresse", Glyph = "📍", Description = "IP-Adresse vom Router (DHCP) erhalten", IsPremiumOnly = true },
        new InternetStep { Name = "Gateway (Router)", Glyph = "🏠", Description = "Router im lokalen Netz erreichen" },
        new InternetStep { Name = "DNS-Server", Glyph = "🔍", Description = "DNS-Server antwortet auf Ping", IsPremiumOnly = true },
        new InternetStep { Name = "DNS-Auflösung", Glyph = "🧭", Description = "Domainname wird in eine IP übersetzt", IsPremiumOnly = true },
        new InternetStep { Name = "Internet (8.8.8.8)", Glyph = "🌐", Description = "Öffentliches Internet erreichbar" },
        new InternetStep { Name = "HTTPS / Web", Glyph = "🔒", Description = "Google → YouTube → Cloudflare (Fallback)" },
        new InternetStep { Name = "Download-Test", Glyph = "⬇", Description = "1 MB Testdatei von Cloudflare laden", IsPremiumOnly = true }
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
    [ObservableProperty] private string _downloadSpeedText = "–";
    [ObservableProperty] private string _averageDownloadText = "–";
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

    /// <summary>Premium/Admin sehen den kompletten Verbindungspfad inkl. Verlauf.</summary>
    public bool IsPremiumUser =>
        AppServices.Profile.IsAdmin ||
        (AppServices.Profile.IsPremium && !AppServices.Profile.PremiumBanned);

    public bool ShowFullDetails => IsPremiumUser;
    public bool ShowPremiumHint => !IsPremiumUser;
    public bool ShowHistory => HasHistory && IsPremiumUser;
    public bool ShowNoHistory => !HasHistory && IsPremiumUser;
    public bool ShowOfflineMode => AppServices.Internet.OfflineMode;

    public InternetCheckerViewModel()
    {
        AppServices.Profile.Changed += () => Dispatcher.UIThread.Post(NotifyAccessChanged);

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _refreshTimer.Start();

        _testTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _testTimer.Tick += async (_, _) => await RunTestStepAsync();

        AppServices.Internet.Changed += () => Dispatcher.UIThread.Post(UpdateLiveValues);
        UpdateLiveValues();
        UpdateStepLocks();
        ReloadHistory();
    }

    private void NotifyAccessChanged()
    {
        OnPropertyChanged(nameof(IsPremiumUser));
        OnPropertyChanged(nameof(ShowFullDetails));
        OnPropertyChanged(nameof(ShowPremiumHint));
        OnPropertyChanged(nameof(ShowHistory));
        OnPropertyChanged(nameof(ShowNoHistory));
        OnPropertyChanged(nameof(CanUse));
        UpdateStepLocks();
    }

    private void UpdateStepLocks()
    {
        foreach (var step in Steps)
        {
            step.IsUnlocked = !step.IsPremiumOnly || IsPremiumUser;
            if (!step.IsUnlocked)
            {
                step.IsActive = false;
                step.IsComplete = false;
                step.IsFailed = false;
                step.LatencyText = "–";
                step.StatusText = "Premium";
                step.StatusColor = "#8B91AC";
                step.DetailText = string.Empty;
            }
        }
    }

    partial void OnHasHistoryChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowHistory));
        OnPropertyChanged(nameof(ShowNoHistory));
    }

    private void UpdateLiveValues()
    {
        IsOnline = AppServices.Internet.IsOnline;
        CurrentLatencyText = AppServices.Internet.CurrentLatencyMs >= 0 ? $"{AppServices.Internet.CurrentLatencyMs} ms" : "–";
        PacketLossText = $"{AppServices.Internet.PacketLossPercent:0} %";
        GatewayIpText = AppServices.Internet.GatewayIp ?? "–";
        DownloadSpeedText = AppServices.Internet.DownloadSpeedText;
        AverageDownloadText = AppServices.Internet.AverageDownloadText;
        ShowOfflineHint = AppServices.Internet.OfflineMode;

        // Update step statuses based on live data (rough) - but not during manual test
        if (!IsTesting && Steps.Count >= 8)
        {
            var adapter = GetActiveAdapter();
            Steps[0].IsComplete = adapter is not null;
            Steps[0].DetailText = adapter is null
                ? string.Empty
                : $"{adapter.Name} · {FormatSpeed(adapter.Speed)}";
            string? localIp = GetLocalIpv4(adapter);
            Steps[1].IsComplete = !string.IsNullOrWhiteSpace(localIp);
            Steps[1].DetailText = localIp ?? string.Empty;
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
            s.DetailText = string.Empty;
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
        if (!step.IsUnlocked)
        {
            _testStepIndex++;
            return;
        }

        step.IsActive = true;
        step.StatusText = "Prüfe…";
        step.StatusColor = "#FACC15";

        bool ok = false;
        int latency = -1;

        try
        {
            switch (_testStepIndex)
            {
                case 0: // Aktiver Adapter
                    var adapter = GetActiveAdapter();
                    ok = adapter is not null;
                    if (adapter is not null)
                        step.DetailText = $"{adapter.Name} · {FormatSpeed(adapter.Speed)}";
                    break;
                case 1: // Lokale IP
                    var localIp = GetLocalIpv4(GetActiveAdapter());
                    ok = !string.IsNullOrWhiteSpace(localIp);
                    step.DetailText = localIp ?? "keine IPv4-Adresse";
                    break;
                case 2: // Gateway
                    var gw = AppServices.Internet.GatewayIp;
                    if (!string.IsNullOrEmpty(gw))
                    {
                        var (o, l) = await PingHostAsync(gw, 3);
                        ok = o; latency = l ?? -1;
                        step.DetailText = gw;
                    }
                    else
                    {
                        ok = false;
                        step.DetailText = "kein Gateway ermittelbar";
                    }
                    break;
                case 3: // DNS-Server
                    var dns = GetDnsServerIp();
                    if (!string.IsNullOrEmpty(dns))
                    {
                        var (o, l) = await PingHostAsync(dns, 3);
                        ok = o; latency = l ?? -1;
                        step.DetailText = dns;
                    }
                    else
                    {
                        ok = false;
                        step.DetailText = "kein DNS-Server konfiguriert";
                    }
                    break;
                case 4: // DNS-Auflösung
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        var addresses = await Dns.GetHostAddressesAsync("www.google.com");
                        sw.Stop();
                        var first = addresses.FirstOrDefault(a =>
                            a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                        ok = addresses.Length > 0;
                        latency = (int)sw.ElapsedMilliseconds;
                        step.DetailText = first?.ToString() ?? "keine IPv4-Adresse";
                    }
                    catch
                    {
                        sw.Stop();
                        ok = false;
                        step.DetailText = "Namensauflösung fehlgeschlagen";
                    }
                    break;
                case 5: // Internet
                    var (o2, l2) = await PingHostAsync("8.8.8.8", 3);
                    ok = o2; latency = l2 ?? -1;
                    step.DetailText = "8.8.8.8 (Google DNS)";
                    break;
                case 6: // HTTP (mit Fallback)
                    var (o3, l3, url) = await AppServices.Internet.TestHttpAsync();
                    ok = o3; latency = l3;
                    step.DetailText = url is null ? "alle Testseiten nicht erreichbar" : new Uri(url).Host;
                    break;
                case 7: // Download-Test
                    var (o4, l4, detail) = await TestDownloadAsync();
                    ok = o4; latency = l4 ?? -1;
                    step.DetailText = detail;
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
        int total = Steps.Count(s => s.IsUnlocked);
        int completed = Steps.Count(s => s.IsUnlocked && s.IsComplete);
        if (completed == total)
        {
            OverallStatus = "Alle sichtbaren Schritte OK – Internet funktioniert.";
            OverallStatusColor = "#4ADE80";
        }
        else if (completed == 0)
        {
            OverallStatus = "Kein Schritt erfolgreich – wahrscheinlich offline.";
            OverallStatusColor = "#F87171";
        }
        else
        {
            OverallStatus = $"{completed}/{total} Schritte OK – teilweise Probleme.";
            OverallStatusColor = "#FB923C";
        }

        AssessmentText = AppServices.Internet.GetOutageAssessment();
        ReloadHistory();
    }

    private static NetworkInterface? GetActiveAdapter()
    {
        try
        {
            var candidates = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or
                                NetworkInterfaceType.Tunnel) &&
                            !IsVirtualAdapter(n))
                .OrderByDescending(n => HasIpv4Gateway(n))
                .ThenByDescending(n => n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or
                    NetworkInterfaceType.Wireless80211)
                .ThenByDescending(n => n.Speed)
                .ToList();
            return candidates.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static bool HasIpv4Gateway(NetworkInterface adapter)
    {
        try
        {
            return adapter.GetIPProperties().GatewayAddresses.Any(g =>
                g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                !g.Address.Equals(System.Net.IPAddress.Any));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsVirtualAdapter(NetworkInterface adapter)
    {
        string text = adapter.Name + " " + adapter.Description;
        return text.Contains("virtual", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("hyper-v", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("vmware", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("virtualbox", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("tap-", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("vpn", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("loopback", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetLocalIpv4(NetworkInterface? adapter)
    {
        try
        {
            if (adapter is null)
                return null;
            return adapter.GetIPProperties().UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily ==
                                     System.Net.Sockets.AddressFamily.InterNetwork)
                ?.Address.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static string FormatSpeed(long bitsPerSecond)
    {
        if (bitsPerSecond <= 0)
            return "Geschwindigkeit unbekannt";
        double mbit = bitsPerSecond / 1_000_000.0;
        return mbit >= 1000
            ? $"{mbit / 1000:0.#} Gbit/s"
            : $"{mbit:0.#} Mbit/s";
    }

    private static async Task<(bool Ok, int? LatencyMs, string Detail)> TestDownloadAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            var sw = Stopwatch.StartNew();
            using var response = await http.GetAsync(
                "https://speed.cloudflare.com/__down?bytes=1048576",
                HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
                return (false, null, "HTTP " + (int)response.StatusCode);

            await using var stream = await response.Content.ReadAsStreamAsync();
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer)) > 0)
                total += read;
            sw.Stop();

            double mbit = total * 8 / Math.Max(0.001, sw.Elapsed.TotalSeconds) / 1_000_000.0;
            return (total > 0, (int)sw.ElapsedMilliseconds,
                $"{mbit:0.0} Mbit/s · {total / 1024} KB in {sw.Elapsed.TotalSeconds:0.0} s");
        }
        catch (Exception ex)
        {
            return (false, null, "Download fehlgeschlagen: " + ex.Message);
        }
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