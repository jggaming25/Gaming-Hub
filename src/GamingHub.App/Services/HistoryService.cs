using System.Globalization;
using System.Linq;
using System.Text;
using Avalonia.Threading;
using GamingHub.Core.Models;

namespace GamingHub.App.Services;

/// <summary>
/// Zeichnet im Hintergrund alle 5 Minuten die System-Auslastung auf
/// (RAM, CPU, GPU sowie Prozessanzahl) und schreibt sie lokal auf:
///  – CSV-Daten für die Diagramme (history.csv) und
///  – ein lesbares Protokoll (protocol.log).
/// Der Speicher ist in Zyklen gegliedert, die ab dem Tag des ersten Starts
/// laufen: Basis 1 Woche, mit Premium 4 Wochen. Ist der Zyklus um, beginnt
/// automatisch ein neuer – die alten Daten werden gelöscht.
/// Falls ein Messwert (z. B. RAM oder CPU) nicht erhebbar ist, wird bis zu
/// 2 weitere Versuche im Abstand von 20 Sekunden unternommen.
/// </summary>
public sealed class HistoryService
{
    private const int SampleIntervalMinutes = 5;
    private const int RetryCount = 2;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(20);

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(SampleIntervalMinutes) };
    private volatile bool _running;

    public string HistoryDirectory { get; }
    public bool IsPremium => AppServices.Profile.IsPremium;
    public event Action? Changed;

    public HistoryService()
    {
        HistoryDirectory = Path.Combine(AppServices.Settings.DataDirectory, "history");
        Directory.CreateDirectory(HistoryDirectory);
    }

    public void Start()
    {
        _timer.Tick += OnTick;
        _timer.Start();
        Dispatcher.UIThread.Post(async () =>
        {
            await RetryableSampleAsync();
            CleanupCycles();
        }, DispatcherPriority.Background);
    }

    public void Stop()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        if (_running) return;
        await RetryableSampleAsync();
        CleanupCycles();
        Changed?.Invoke();
    }

    /// <summary>Führt bis zu 1 + <see cref="RetryCount"/> Messversuche mit
    /// jeweils 20 Sekunden Abstand durch, bis die Werte erhebbar sind.</summary>
    public async Task RetryableSampleAsync()
    {
        for (int attempt = 0; attempt <= RetryCount; attempt++)
        {
            if (attempt > 0)
            {
                LogService.Info($"Telemetrie: Messwerte konnten nicht erfasst werden – " +
                                $"Versuch {attempt + 1} von {RetryCount + 1} in 20 s.");
                await Task.Delay(RetryDelay);
            }

            try
            {
                var snapshot = AppServices.SystemMonitor.Sample();
                if (!IsCollectable(snapshot))
                    continue;

                WriteSnapshot(snapshot);
                return;
            }
            catch (Exception ex)
            {
                LogService.Warn("Telemetrie-Messung fehlgeschlagen.", ex);
            }
        }

        LogService.Warn("Telemetrie: Messwerte sind nach mehreren Versuchen nicht erhebbar – dieser Abfragezeitpunkt wird übersprungen.");
    }

    private static bool IsCollectable(SystemSnapshot s)
        => s.RamTotalBytes > 0
           && s.CpuUsagePercent >= 0
           && (s.ProcessCount > 0 || s.Processes.Count > 0);

    private void WriteSnapshot(SystemSnapshot s)
    {
        try
        {
            // Zyklus ab Tag des ersten Starts (Basis 1 Woche, Premium 4 Wochen).
            DateTimeOffset first = AppServices.Settings.Settings.FirstEverLaunchUtc ?? DateTimeOffset.UtcNow;
            int retentionDays = IsPremium ? 28 : 7;
            int cycle = (int)((DateTimeOffset.UtcNow.Date - first.Date).TotalDays / retentionDays);
            string cycleDir = Path.Combine(HistoryDirectory, $"cycle-{cycle}");
            Directory.CreateDirectory(cycleDir);

            string csv = Path.Combine(cycleDir, "history.csv");
            string log = Path.Combine(cycleDir, "protocol.log");
            bool csvNew = !File.Exists(csv);

            string line = string.Create(CultureInfo.InvariantCulture,
                $"{s.TimestampUtc:o},{s.CpuUsagePercent:0.00},{s.RamUsedBytes},{s.RamTotalBytes},{s.GpuUsagePercent:0.00},{s.ProcessCount}");

            using (var sw = new StreamWriter(csv, append: true, Encoding.UTF8))
            {
                if (csvNew)
                    sw.WriteLine("timestampUtc,cpuPercent,ramUsedBytes,ramTotalBytes,gpuPercent,processCount");
                sw.WriteLine(line);
            }

            double gpu = s.GpuUsagePercent >= 0 ? s.GpuUsagePercent : double.NaN;
            string human = string.Format(CultureInfo.InvariantCulture,
                "{0:HH:mm:ss}  CPU {1,5:0.0} %   RAM {2,8}/{3,8} MB   GPU {4,5:0.0} %   Prozesse {5,4}",
                s.TimestampUtc.ToLocalTime(), s.CpuUsagePercent,
                s.RamUsedBytes / (1024.0 * 1024.0), s.RamTotalBytes / (1024.0 * 1024.0),
                gpu, s.ProcessCount);
            File.AppendAllText(log, human + Environment.NewLine, Encoding.UTF8);

            LogService.Info($"Telemetrie aufgezeichnet: {human}");
        }
        catch (Exception ex)
        {
            LogService.Warn("Telemetrie-Datensatz konnte nicht gespeichert werden.", ex);
        }
    }

    /// <summary>Löscht abgelaufene Zyklen – die Daten bleiben höchstens so lange,
    /// wie es der aktuelle Zeitraum vorgibt, danach beginnt der nächste Zyklus.</summary>
    private void CleanupCycles()
    {
        try
        {
            DateTimeOffset first = AppServices.Settings.Settings.FirstEverLaunchUtc ?? DateTimeOffset.UtcNow;
            int retentionDays = IsPremium ? 28 : 7;
            int currentCycle = (int)((DateTimeOffset.UtcNow.Date - first.Date).TotalDays / retentionDays);
            int oldestKept = currentCycle - 1;

            foreach (var dir in Directory.GetDirectories(HistoryDirectory, "cycle-*"))
            {
                string name = Path.GetFileName(dir);
                if (!name.StartsWith("cycle-", StringComparison.OrdinalIgnoreCase)) continue;
                if (int.TryParse(name.AsSpan("cycle-".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int c))
                {
                    if (c < oldestKept)
                    {
                        Directory.Delete(dir, recursive: true);
                        LogService.Info($"Telemetrie: alter Zyklus {name} gelöscht (Aufbewahrung {retentionDays} Tage).");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Warn("Telemetrie-Aufräumen fehlgeschlagen.", ex);
        }
    }

    /// <summary>Lädt den aktuellen Zyklus als Liste für die Diagramme.</summary>
    public List<HistoryPoint> LoadRecent(int minutesBack = SampleIntervalMinutes * 2)
    {
        var all = LoadChartData();
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-minutesBack);
        return all.Where(p => p.TimestampUtc >= cutoff).ToList();
    }

    /// <summary>Lädt den vollständigen aktuellen Zyklus für die Diagramme.</summary>
    public List<HistoryPoint> LoadChartData()
    {
        var points = new List<HistoryPoint>();
        try
        {
            DateTimeOffset first = AppServices.Settings.Settings.FirstEverLaunchUtc ?? DateTimeOffset.UtcNow;
            int retentionDays = IsPremium ? 28 : 7;
            int currentCycle = (int)((DateTimeOffset.UtcNow.Date - first.Date).TotalDays / retentionDays);
            string csv = Path.Combine(HistoryDirectory, $"cycle-{currentCycle}", "history.csv");
            if (!File.Exists(csv)) return points;

            foreach (var raw in File.ReadAllLines(csv))
            {
                var p = ParseLine(raw);
                if (p is not null)
                    points.Add(p);
            }

            double maxProcess = points.Count > 0 ? points.Max(p => (double)p.ProcessCount) : 1;
            if (maxProcess < 1) maxProcess = 1;
            foreach (var pt in points)
                pt.MaxRecentProcessCount = maxProcess;
        }
        catch (Exception ex)
        {
            LogService.Warn("Telemetrie-Diagrammdaten konnten nicht geladen werden.", ex);
        }
        return points;
    }

    private static HistoryPoint? ParseLine(string raw)
    {
        var parts = raw.Split(',');
        if (parts.Length < 6) return null;
        if (!DateTimeOffset.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ts)) return null;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double cpu)) return null;
        if (!double.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out double ramUsed)) return null;
        if (!double.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out double ramTotal)) return null;
        if (!double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double gpu)) return null;
        if (!int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int proc)) return null;

        return new HistoryPoint
        {
            TimestampUtc = ts,
            CpuPercent = cpu,
            RamUsedBytes = ramUsed,
            RamTotalBytes = ramTotal,
            GpuPercent = gpu,
            ProcessCount = proc,
            RamUsagePercent = ramTotal > 0 ? ramUsed / ramTotal * 100.0 : 0
        };
    }
}

public sealed class HistoryPoint
{
    public DateTimeOffset TimestampUtc { get; set; }
    public double CpuPercent { get; set; }
    public double RamUsedBytes { get; set; }
    public double RamTotalBytes { get; set; }
    public double RamUsagePercent { get; set; }
    public double GpuPercent { get; set; }
    public int ProcessCount { get; set; }
    public double MaxRecentProcessCount { get; set; } = 1;

    public double CpuCol => Math.Clamp(CpuPercent, 0, 100);
    public double RamCol => Math.Clamp(RamUsagePercent, 0, 100);
    public double GpuCol => GpuPercent >= 0 ? Math.Clamp(GpuPercent, 0, 100) : 0;
    public bool HasGpu => GpuPercent >= 0;
}