using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using GamingHub.Core.Models;
using Microsoft.Win32;

namespace GamingHub.App.Services;

/// <summary>
/// Internet-Connectivity-Service: überwacht die Erreichbarkeit, zeichnet
/// Verlauf auf, bietet einen visuellen Pfad-Test an (PC → Router → DNS → Internet)
/// und schätzt ein, ob Probleme beim eigenen Router oder flächendeckend (ISP) liegen.
/// Daten werden lokal im 4-Wochen-Zyklus (Premium) / 1 Woche (Basis) vorgehalten.
/// </summary>
public sealed class InternetService
{
    private const int CheckIntervalSeconds = 15;
    private const int HistoryRetentionDaysBase = 7;
    private const int HistoryRetentionDaysPremium = 28;
    private const int BackupIntervalDays = 5;
    private const int SpeedTestIntervalMinutes = 30;
    private const int SpeedSampleRetentionDays = 7;
    private const long SpeedTestBytes = 2_000_000;
    private static readonly Uri SpeedTestUri = new("https://speed.cloudflare.com/__down?bytes=2000000");

    /// <summary>HTTPS-Testziele in Fallback-Reihenfolge: Google → YouTube → Cloudflare.
    /// Es wird die erste erreichbare Seite gewertet, damit ein einzelner
    /// blockierter/großer Anbieter nicht fälschlich „offline“ meldet.</summary>
    private static readonly string[] HttpTestUrls =
    {
        "https://www.google.com",
        "https://www.youtube.com",
        "https://www.cloudflare.com"
    };

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly Timer _timer;
    private readonly string _dataDirectory;
    private readonly List<(DateTimeOffset Time, double Mbps)> _speedSamples = new();
    private volatile bool _running;
    private DateTimeOffset _lastBackupCheck = DateTimeOffset.MinValue;
    private DateTimeOffset _lastSpeedTest = DateTimeOffset.MinValue;
    private DateTimeOffset _lastSlowNotification = DateTimeOffset.MinValue;

    public event Action? Changed;

    public bool IsOnline { get; private set; }
    public string? LastError { get; private set; }
    public int CurrentLatencyMs { get; private set; } = -1;
    public double PacketLossPercent { get; private set; } = 0;
    public string? GatewayIp { get; private set; }
    public string? DnsServerIp { get; private set; }
    public string? LastHttpTarget { get; private set; }
    /// <summary>Zuletzt gemessene Download-Geschwindigkeit in Mbit/s (-1 = unbekannt).</summary>
    public double DownloadMbps { get; private set; } = -1;
    /// <summary>Durchschnitt der Download-Messungen der letzten 7 Tage in Mbit/s.</summary>
    public double AverageDownloadMbps7d { get; private set; } = -1;
    public string DownloadSpeedText => DownloadMbps > 0 ? $"{DownloadMbps:0.0} Mbit/s" : "–";
    public string AverageDownloadText => AverageDownloadMbps7d > 0 ? $"{AverageDownloadMbps7d:0.0} Mbit/s" : "–";
    public bool OfflineMode => !IsOnline && _hasEverBeenOnline;

    private bool _hasEverBeenOnline = false;

    public InternetService()
    {
        _dataDirectory = Path.Combine(AppServices.Settings.DataDirectory, "internet");
        Directory.CreateDirectory(_dataDirectory);
        LoadSpeedSamples();
        _timer = new Timer(async _ => await CheckAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        if (!_timer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(CheckIntervalSeconds)))
            return;
        _running = true;
        _ = CheckAsync();
    }

    public void Stop()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        _running = false;
    }

    public async Task CheckAsync()
    {
        if (!_running) return;
        if (_running && _checkLock) return;
        _checkLock = true;
        try
        {
            await PerformFullCheckAsync();
            MaybeCreateBackup();
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            LogService.Warn("Internet-Check fehlgeschlagen.", ex);
        }
        finally
        {
            _checkLock = false;
        }
    }

    private bool _checkLock = false;

    private async Task PerformFullCheckAsync()
    {
        var gateway = GetGatewayIp();
        GatewayIp = gateway;
        bool gwOk = false;
        int gwLatency = -1;
        if (!string.IsNullOrEmpty(gateway))
        {
            var (ok, lat) = await PingHostAsync(gateway, 2);
            gwOk = ok;
            gwLatency = lat ?? -1;
        }

        var dnsServer = GetDnsServerIp();
        DnsServerIp = dnsServer;
        bool dnsOk = false;
        int dnsLatency = -1;
        if (!string.IsNullOrEmpty(dnsServer))
        {
            var (ok, lat) = await PingHostAsync(dnsServer, 2);
            dnsOk = ok;
            dnsLatency = lat ?? -1;
        }

        bool inetOk = false;
        int inetLatency = -1;
        if (gwOk)
        {
            var (ok, lat) = await PingHostAsync("8.8.8.8", 2);
            inetOk = ok;
            inetLatency = lat ?? -1;
        }

        bool httpOk = false;
        int httpLatency = -1;
        if (inetOk)
        {
            var (ok, lat, url) = await TestHttpAsync();
            httpOk = ok;
            httpLatency = lat;
            LastHttpTarget = url;
        }

        IsOnline = httpOk;
        if (IsOnline)
        {
            _hasEverBeenOnline = true;
            await MaybeMeasureDownloadSpeedAsync();
        }

        int effectiveLatency = httpLatency >= 0 ? httpLatency : inetLatency >= 0 ? inetLatency : dnsLatency >= 0 ? dnsLatency : gwLatency;
        CurrentLatencyMs = effectiveLatency >= 0 ? effectiveLatency : -1;

        RecordSnapshot(gwOk, gwLatency, dnsOk, dnsLatency, inetOk, inetLatency, httpOk, httpLatency);
    }

    private async Task MaybeMeasureDownloadSpeedAsync()
    {
        if ((DateTimeOffset.UtcNow - _lastSpeedTest).TotalMinutes < SpeedTestIntervalMinutes) return;
        _lastSpeedTest = DateTimeOffset.UtcNow;

        double mbps = await MeasureDownloadSpeedAsync();
        if (mbps <= 0) return;

        DownloadMbps = mbps;
        _speedSamples.Add((DateTimeOffset.UtcNow, mbps));
        UpdateSpeedAverage();
        SaveSpeedSamples();
        MaybeNotifySlowConnection();
    }

    private async Task<double> MeasureDownloadSpeedAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var resp = await _http.GetAsync(SpeedTestUri, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!resp.IsSuccessStatusCode) return -1;

            await using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
            var buffer = new byte[81920];
            long total = 0;
            var sw = Stopwatch.StartNew();
            while (total < SpeedTestBytes)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token);
                if (read <= 0) break;
                total += read;
                if (sw.ElapsedMilliseconds > 9000) break;
            }
            sw.Stop();

            if (total < 100_000 || sw.Elapsed.TotalSeconds <= 0.05) return -1;
            return total * 8.0 / sw.Elapsed.TotalSeconds / 1_000_000.0;
        }
        catch
        {
            return -1;
        }
    }

    private void LoadSpeedSamples()
    {
        try
        {
            string file = SpeedFileName;
            if (!File.Exists(file)) return;
            var cutoff = DateTimeOffset.UtcNow.AddDays(-SpeedSampleRetentionDays);
            foreach (var raw in File.ReadAllLines(file))
            {
                var parts = raw.Split(',');
                if (parts.Length < 2) continue;
                if (DateTimeOffset.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ts)
                    && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double mbps)
                    && ts > cutoff && mbps > 0)
                {
                    _speedSamples.Add((ts, mbps));
                }
            }
            UpdateSpeedAverage();
            if (_speedSamples.Count > 0)
                DownloadMbps = _speedSamples[^1].Mbps;
        }
        catch (Exception ex)
        {
            LogService.Warn("Download-Geschwindigkeitsverlauf konnte nicht geladen werden.", ex);
        }
    }

    private string SpeedFileName => Path.Combine(_dataDirectory, "speed.csv");

    private void SaveSpeedSamples()
    {
        try
        {
            var lines = new List<string> { "timestampUtc,downloadMbps" };
            lines.AddRange(_speedSamples.Select(s =>
                $"{s.Time.ToString("o", CultureInfo.InvariantCulture)},{s.Mbps.ToString("0.##", CultureInfo.InvariantCulture)}"));
            File.WriteAllLines(SpeedFileName, lines);
        }
        catch (Exception ex)
        {
            LogService.Warn("Download-Geschwindigkeitsverlauf konnte nicht gespeichert werden.", ex);
        }
    }

    private void UpdateSpeedAverage()
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-SpeedSampleRetentionDays);
        _speedSamples.RemoveAll(s => s.Time < cutoff);
        AverageDownloadMbps7d = _speedSamples.Count > 0 ? _speedSamples.Average(s => s.Mbps) : -1;
    }

    private void MaybeNotifySlowConnection()
    {
        if (_speedSamples.Count < 5 || AverageDownloadMbps7d <= 0 || DownloadMbps <= 0) return;
        if (DownloadMbps >= AverageDownloadMbps7d * 0.5) return;
        if ((DateTimeOffset.UtcNow - _lastSlowNotification).TotalHours < 6) return;

        _lastSlowNotification = DateTimeOffset.UtcNow;
        AppServices.Notifications.Show("Langsame Internetverbindung",
            $"Aktuell {DownloadMbps:0.0} Mbit/s – 7-Tage-Schnitt {AverageDownloadMbps7d:0.0} Mbit/s.");
    }

    private async Task<(bool Ok, int? LatencyMs)> PingHostAsync(string host, int count)
    {
        try
        {
            using var ping = new Ping();
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
            if (success > 0)
                return (true, (int)(totalLatency / success));
            return (false, null);
        }
        catch
        {
            return (false, null);
        }
    }

    public async Task<(bool Ok, int LatencyMs, string? Url)> TestHttpAsync()
    {
        foreach (var url in HttpTestUrls)
        {
            var (ok, latency) = await HttpHeadAsync(url, TimeSpan.FromSeconds(5));
            if (ok)
                return (true, latency ?? -1, url);
        }
        return (false, -1, null);
    }

    private async Task<(bool Ok, int? LatencyMs)> HttpHeadAsync(string url, TimeSpan timeout)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            var sw = Stopwatch.StartNew();
            using var resp = await _http.SendAsync(req, cts.Token);
            sw.Stop();
            // Jede HTTP-Antwort beweist eine funktionierende Verbindung – auch
            // 4xx/5xx (z. B. blockiert HEAD oder Captive Portal).
            return (true, (int)sw.ElapsedMilliseconds);
        }
        catch
        {
            return (false, null);
        }
    }

    private string? GetGatewayIp()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                var props = ni.GetIPProperties();
                var gateways = props.GatewayAddresses;
                foreach (var gw in gateways)
                {
                    if (gw.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        return gw.Address.ToString();
                }
            }
        }
        catch { }
        return null;
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
                {
                    if (dns.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        return dns.ToString();
                }
            }
        }
        catch { }
        return null;
    }

    private void RecordSnapshot(
        bool gwOk, int gwLat, bool dnsOk, int dnsLat,
        bool inetOk, int inetLat, bool httpOk, int httpLat)
    {
        try
        {
            bool isPremium = AppServices.Profile.IsPremium;
            int retentionDays = isPremium ? HistoryRetentionDaysPremium : HistoryRetentionDaysBase;
            DateTimeOffset first = AppServices.Settings.Settings.FirstEverLaunchUtc ?? DateTimeOffset.UtcNow;
            int cycle = (int)((DateTimeOffset.UtcNow.Date - first.Date).TotalDays / retentionDays);
            string cycleDir = Path.Combine(_dataDirectory, $"cycle-{cycle}");
            Directory.CreateDirectory(cycleDir);

            string csv = Path.Combine(cycleDir, "history.csv");
            string log = Path.Combine(cycleDir, "protocol.log");
            bool csvNew = !File.Exists(csv);

            var ts = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            string line = $"{ts},{(gwOk ? 1 : 0)},{gwLat},{(dnsOk ? 1 : 0)},{dnsLat},{(inetOk ? 1 : 0)},{inetLat},{(httpOk ? 1 : 0)},{httpLat}";

            using (var sw = new StreamWriter(csv, append: true, Encoding.UTF8))
            {
                if (csvNew)
                    sw.WriteLine("timestampUtc,gatewayOk,gatewayLatencyMs,dnsOk,dnsLatencyMs,internetOk,internetLatencyMs,httpOk,httpLatencyMs");
                sw.WriteLine(line);
            }

            string human = $"{DateTimeOffset.UtcNow:HH:mm:ss}  GW:{(gwOk ? "OK " + gwLat + "ms" : "FAIL")}  DNS:{(dnsOk ? "OK " + dnsLat + "ms" : "FAIL")}  INET:{(inetOk ? "OK " + inetLat + "ms" : "FAIL")}  HTTP:{(httpOk ? "OK " + httpLat + "ms" : "FAIL")}";
            File.AppendAllText(log, human + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            LogService.Warn("Internet-Snapshot konnte nicht gespeichert werden.", ex);
        }
    }

    private void MaybeCreateBackup()
    {
        try
        {
            if ((DateTimeOffset.UtcNow - _lastBackupCheck).TotalDays < 1) return;
            _lastBackupCheck = DateTimeOffset.UtcNow;

            string backupDir = Path.Combine(_dataDirectory, "backups");
            Directory.CreateDirectory(backupDir);

            var existing = Directory.GetFiles(backupDir, "backup-*.zip").OrderByDescending(f => f).ToList();
            if (existing.Count > 0)
            {
                var latest = existing[0];
                var latestTime = File.GetLastWriteTimeUtc(latest);
                if ((DateTimeOffset.UtcNow - latestTime).TotalDays < BackupIntervalDays) return;
            }

            string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
            string zipPath = Path.Combine(backupDir, $"backup-{timestamp}.zip");

            string sourceDir = AppServices.Settings.DataDirectory;
            if (Directory.Exists(sourceDir))
            {
                ZipFile.CreateFromDirectory(sourceDir, zipPath, CompressionLevel.Optimal, false);
            }

            foreach (var old in existing.Skip(1))
                File.Delete(old);

            _lastBackupCheck = DateTimeOffset.UtcNow;
            LogService.Info($"Internet-Backup erstellt: {zipPath}");
        }
        catch (Exception ex)
        {
            LogService.Warn("Internet-Backup fehlgeschlagen.", ex);
        }
    }

    public List<InternetHistoryPoint> LoadChartData()
    {
        var points = new List<InternetHistoryPoint>();
        try
        {
            bool isPremium = AppServices.Profile.IsPremium;
            int retentionDays = isPremium ? HistoryRetentionDaysPremium : HistoryRetentionDaysBase;
            DateTimeOffset first = AppServices.Settings.Settings.FirstEverLaunchUtc ?? DateTimeOffset.UtcNow;
            int currentCycle = (int)((DateTimeOffset.UtcNow.Date - first.Date).TotalDays / retentionDays);
            string csv = Path.Combine(_dataDirectory, $"cycle-{currentCycle}", "history.csv");
            if (!File.Exists(csv)) return points;

            foreach (var raw in File.ReadAllLines(csv))
            {
                var p = ParseLine(raw);
                if (p is not null) points.Add(p);
            }
        }
        catch (Exception ex)
        {
            LogService.Warn("Internet-Verlauf konnte nicht geladen werden.", ex);
        }
        return points;
    }

    private static InternetHistoryPoint? ParseLine(string raw)
    {
        var parts = raw.Split(',');
        if (parts.Length < 9) return null;
        if (!DateTimeOffset.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ts)) return null;

        return new InternetHistoryPoint
        {
            TimestampUtc = ts,
            GatewayOk = parts[1] == "1",
            GatewayLatencyMs = int.TryParse(parts[2], out int gw) ? gw : -1,
            DnsOk = parts[3] == "1",
            DnsLatencyMs = int.TryParse(parts[4], out int dns) ? dns : -1,
            InternetOk = parts[5] == "1",
            InternetLatencyMs = int.TryParse(parts[6], out int inet) ? inet : -1,
            HttpOk = parts[7] == "1",
            HttpLatencyMs = int.TryParse(parts[8], out int http) ? http : -1,
        };
    }

    /// <summary>Schätzt ein, ob ein Problem eher lokal (Router) oder flächendeckend (ISP) ist.</summary>
    public string GetOutageAssessment()
    {
        var points = LoadChartData().Where(p => p.TimestampUtc > DateTimeOffset.UtcNow.AddHours(-24)).ToList();
        if (points.Count < 5) return "Nicht genügend Daten für eine Einschätzung.";

        int gwFails = points.Count(p => !p.GatewayOk);
        int inetFails = points.Count(p => p.GatewayOk && !p.InternetOk);
        int total = points.Count;

        double gwFailRate = (double)gwFails / total;
        double inetFailRate = (double)inetFails / total;

        if (gwFailRate > 0.3)
            return $"Wahrscheinlich eigenes Router-Problem (Gateway-Ausfallrate {gwFailRate:P0}). Router neu starten oder Kabel prüfen.";
        if (inetFailRate > 0.3)
            return $"Wahrscheinlich ISP-/Flächenstörung (Internet-Ausfallrate {inetFailRate:P0} bei funktionierendem Gateway).";
        return "Keine signifikanten Ausfälle in den letzten 24 h erkannt.";
    }
}

public sealed class InternetHistoryPoint
{
    public DateTimeOffset TimestampUtc { get; set; }
    public bool GatewayOk { get; set; }
    public int GatewayLatencyMs { get; set; }
    public bool DnsOk { get; set; }
    public int DnsLatencyMs { get; set; }
    public bool InternetOk { get; set; }
    public int InternetLatencyMs { get; set; }
    public bool HttpOk { get; set; }
    public int HttpLatencyMs { get; set; }

    public bool IsOnline => HttpOk;
    public int EffectiveLatencyMs => HttpLatencyMs >= 0 ? HttpLatencyMs : InternetLatencyMs >= 0 ? InternetLatencyMs : DnsLatencyMs >= 0 ? DnsLatencyMs : GatewayLatencyMs;
    public string LatencyDisplay => EffectiveLatencyMs >= 0 ? $"{EffectiveLatencyMs} ms" : "–";
    public string StatusText => HttpOk ? "Online" : InternetOk ? "Internet (kein HTTP)" : DnsOk ? "DNS ok, kein Internet" : GatewayOk ? "Gateway ok, DNS/Internet down" : "Gateway down";
    public string StatusColor => HttpOk ? "#4ADE80" : InternetOk ? "#FACC15" : DnsOk ? "#FB923C" : "#F87171";
}