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

    // Verlauf (4 Wochen / Premium) – in feste Zeitscheiben aufgeteilt, damit
    // das Diagramm immer den kompletten Zeitraum abdeckt (auch bei PC-Auszeiten).
    private const int HistoryBucketMinutes = 30;
    private const double HistoryPointSpacing = 4;
    private const int HistoryRetentionDaysBase = 7;
    private const int HistoryRetentionDaysPremium = 28;

    private IReadOnlyList<double> _latencyHistory = Array.Empty<double>();
    private IReadOnlyList<double> _onlineHistory = Array.Empty<double>();
    private IReadOnlyList<double> _gatewayLatencyHistory = Array.Empty<double>();
    private IReadOnlyList<double> _dnsLatencyHistory = Array.Empty<double>();

    public IReadOnlyList<double> LatencyHistory => _latencyHistory;
    public IReadOnlyList<double> OnlineHistory => _onlineHistory; // 1=online, 0=offline
    public IReadOnlyList<double> GatewayLatencyHistory => _gatewayLatencyHistory;
    public IReadOnlyList<double> DnsLatencyHistory => _dnsLatencyHistory;

    /// <summary>Datums-Beschriftungen unter dem Diagramm (scrollbar).</summary>
    public ObservableCollection<HistoryDateLabel> DateLabels { get; } = new();

    /// <summary>Breite des Diagramms in Pixeln – deckt den vollen Zeitraum ab und
    /// macht das Diagramm horizontal scrollbar (1 Woche frei, 4 Wochen Premium).</summary>
    [ObservableProperty] private double _chartWidth = 600;

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

        AppServices.Internet.Changed += () => Dispatcher.UIThread.Post(OnInternetChanged);
        UpdateLiveValues();
        UpdateStepLocks();
        ReloadHistory();
        _lastHistoryReload = DateTimeOffset.UtcNow;
    }

    private DateTimeOffset _lastHistoryReload;

    private void OnInternetChanged()
    {
        UpdateLiveValues();

        // Der Verlauf wächst alle 15 Sekunden. Für das Diagramm reicht ein
        // Neuaufbau pro Minute; das hält die Oberfläche flüssig.
        if ((DateTimeOffset.UtcNow - _lastHistoryReload).TotalSeconds < 60)
            return;

        _lastHistoryReload = DateTimeOffset.UtcNow;
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

    /// <summary>
    /// Baut die Diagrammreihen über den vollen Aufbewahrungszeitraum
    /// (1 Woche bzw. 4 Wochen Premium). Feste Zeitscheiben sorgen dafür, dass
    /// auch Zeiten ohne Messungen – etwa wenn der PC aus war – als Laufzeit
    /// im Diagramm erscheinen.
    /// </summary>
    private void ReloadHistory()
    {
        var all = AppServices.Internet.LoadChartData();

        bool isPremium = AppServices.Profile.IsPremium;
        int retentionDays = isPremium ? HistoryRetentionDaysPremium : HistoryRetentionDaysBase;
        DateTimeOffset end = DateTimeOffset.UtcNow;
        DateTimeOffset start = end.AddDays(-retentionDays);
        int bucketCount = retentionDays * (24 * 60 / HistoryBucketMinutes);

        var latencySum = new double[bucketCount];
        var latencyCount = new int[bucketCount];
        var onlineSum = new double[bucketCount];
        var onlineCount = new int[bucketCount];
        var gatewaySum = new double[bucketCount];
        var gatewayCount = new int[bucketCount];
        var dnsSum = new double[bucketCount];
        var dnsCount = new int[bucketCount];

        foreach (var p in all)
        {
            if (p.TimestampUtc < start || p.TimestampUtc > end)
                continue;

            int index = Math.Clamp(
                (int)((p.TimestampUtc - start).TotalMinutes / HistoryBucketMinutes),
                0, bucketCount - 1);

            onlineSum[index] += p.IsOnline ? 1 : 0;
            onlineCount[index]++;

            if (p.EffectiveLatencyMs >= 0)
            {
                latencySum[index] += p.EffectiveLatencyMs;
                latencyCount[index]++;
            }
            if (p.GatewayLatencyMs >= 0)
            {
                gatewaySum[index] += p.GatewayLatencyMs;
                gatewayCount[index]++;
            }
            if (p.DnsLatencyMs >= 0)
            {
                dnsSum[index] += p.DnsLatencyMs;
                dnsCount[index]++;
            }
        }

        static double Average(double sum, int count) => count > 0 ? sum / count : double.NaN;

        _latencyHistory = Enumerable.Range(0, bucketCount)
            .Select(i => Average(latencySum[i], latencyCount[i])).ToList();
        _onlineHistory = Enumerable.Range(0, bucketCount)
            .Select(i => Average(onlineSum[i], onlineCount[i])).ToList();
        _gatewayLatencyHistory = Enumerable.Range(0, bucketCount)
            .Select(i => Average(gatewaySum[i], gatewayCount[i])).ToList();
        _dnsLatencyHistory = Enumerable.Range(0, bucketCount)
            .Select(i => Average(dnsSum[i], dnsCount[i])).ToList();

        ChartWidth = Math.Max(600, bucketCount * HistoryPointSpacing);

        DateLabels.Clear();
        int labelCount = Math.Clamp((int)(ChartWidth / 130), 2, 18);
        for (int i = 0; i < labelCount; i++)
        {
            double fraction = labelCount <= 1 ? 0 : (double)i / (labelCount - 1);
            int bucket = (int)Math.Round((bucketCount - 1) * fraction);
            var time = start + TimeSpan.FromTicks((long)((end - start).Ticks * fraction));
            DateLabels.Add(new HistoryDateLabel
            {
                X = bucket * HistoryPointSpacing,
                Text = time.ToLocalTime().ToString("dd.MM HH:mm")
            });
        }

        OnPropertyChanged(nameof(LatencyHistory));
        OnPropertyChanged(nameof(OnlineHistory));
        OnPropertyChanged(nameof(GatewayLatencyHistory));
        OnPropertyChanged(nameof(DnsLatencyHistory));

        HasHistory = all.Count > 0;
        HistoryRetentionText = isPremium ? "Premium: 4 Wochen" : "Aufbewahrung: 1 Woche (Premium: 4 Wochen)";
        HistoryStatusText = all.Count == 0
            ? "Noch keine Internet-Daten aufgezeichnet (alle 15 Sekunden ein Check)."
            : $"Zeitraum: {retentionDays} Tage · {all.Count} Messpunkte · letzte {all[^1].TimestampUtc.ToLocalTime():HH:mm:ss} · " +
              (all.Count(p => !p.IsOnline) > 0 ? $"{all.Count(p => !p.IsOnline)} Ausfälle" : "keine Ausfälle");
    }
}