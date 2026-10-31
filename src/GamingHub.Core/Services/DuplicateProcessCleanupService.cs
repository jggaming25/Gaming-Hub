using System.Diagnostics;

namespace GamingHub.Core.Services;

/// <summary>Ergebnis eines Aufräumlaufs für doppelte Hilfsprozesse.</summary>
public sealed class DuplicateProcessCleanupResult
{
    public int Terminated { get; set; }
    public int GroupsFound { get; set; }
    public List<string> Details { get; } = new();

    public string Summary => Terminated == 0
        ? "Keine überflüssigen doppelten Hilfsprozesse gefunden."
        : $"{Terminated} doppelte Hilfsprozesse beendet.";
}

/// <summary>
/// Beendet ausschließlich harmlose, doppelte Hilfsprozesse, um Ressourcen
/// freizugeben. Bewusst sehr konservativ:
///  – nur Prozesse mit identischem Programm Pfad (also echte Kopien),
///  – nur Launcher-/Updater-/Helper-Prozesse,
///  – nie Systemprozesse, Windows-Dienste, Multi-Prozess-Apps
///    (z. B. Chrome, Spotify, Discord) oder Prozesse mit sichtbarem Fenster,
///  – die älteste bzw. sichtbare Instanz bleibt immer erhalten,
///  – Prozesse, die erst seit wenigen Minuten laufen, werden nicht angerührt.
/// </summary>
public sealed class DuplicateProcessCleanupService : IDisposable
{
    private static readonly TimeSpan GracePeriod = TimeSpan.FromMinutes(5);

    private static readonly string[] HelperHints =
    {
        "launcher", "updater", "update", "bootstrapper", "bootstrap",
        "helper", "crashpad", "crashhandler", "installer", "setup"
    };

    private static readonly string[] ProtectedNameFragments =
    {
        "system", "svchost", "explorer", "runtimebroker", "dllhost", "conhost",
        "csrss", "winlogon", "wininit", "services", "lsass", "smss", "taskhost",
        "sihost", "ctfmon", "searchhost", "startmenuexperiencehost",
        "shellexperiencehost", "applicationframehost", "textinputhost", "widgets",
        "securityhealth", "msmpeng", "nissrv", "audiodg", "spoolsv",
        "fontdrvhost", "dwm", "lockapp", "gaminghub"
    };

    /// <summary>Apps, die absichtlich mehrere Prozesse mit demselben Pfad
    /// betreiben. Diese werden nie als Duplikat behandelt.</summary>
    private static readonly string[] MultiProcessFamilies =
    {
        "spotify", "chrome", "msedge", "firefox", "opera", "brave", "vivaldi",
        "discord", "teams", "slack", "zoom", "code", "devenv", "webview",
        "steam", "gameoverlay", "nvidia", "amd", "razer", "corsair", "logi",
        "openrgb", "icue", "armoury", "mystic", "signalrgb", "electron"
    };

    private readonly Timer _timer;
    private volatile bool _running;

    /// <summary>Meldet Aufräum-Ergebnisse an die App (Logging).</summary>
    public event Action<string>? Message;

    public DuplicateProcessCleanupService()
    {
        _timer = new Timer(_ => Run(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Erster Lauf nach 2 Minuten, danach alle 30 Minuten.</summary>
    public void Start()
    {
        _timer.Change(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(30));
    }

    public void Stop()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void Run()
    {
        if (_running)
            return;

        _running = true;
        try
        {
            var result = CleanupOnce();
            if (result.Terminated > 0)
            {
                Message?.Invoke($"Prozess-Aufräumen: {result.Summary} " +
                                string.Join(", ", result.Details));
            }
        }
        catch (Exception ex)
        {
            Message?.Invoke($"Prozess-Aufräumen fehlgeschlagen: {ex.Message}");
        }
        finally
        {
            _running = false;
        }
    }

    /// <summary>Führt genau einen Aufräumlauf aus (auch für Tests).</summary>
    public DuplicateProcessCleanupResult CleanupOnce()
    {
        var result = new DuplicateProcessCleanupResult();
        var candidates = new List<ProcessInfo>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var info = Inspect(process);
                if (info is not null)
                    candidates.Add(info);
            }
            catch
            {
                // Zugriff verweigert – bewusst überspringen.
            }
            finally
            {
                process.Dispose();
            }
        }

        foreach (var group in candidates.GroupBy(c => c.Path, StringComparer.OrdinalIgnoreCase))
        {
            var instances = group.ToList();
            if (instances.Count < 2)
                continue;

            var first = instances[0];
            if (!IsHelperProcess(first.Name) ||
                IsProtected(first.Name) ||
                IsMultiProcessFamily(first.Name))
                continue;

            result.GroupsFound++;

            // Sichtbare Fenster nie beenden; ansonsten die älteste Instanz behalten.
            var ordered = instances
                .OrderByDescending(i => i.HasWindow)
                .ThenBy(i => i.StartedAt ?? DateTime.MaxValue)
                .ToList();

            for (int i = 1; i < ordered.Count; i++)
            {
                var candidate = ordered[i];
                if (candidate.HasWindow || candidate.IsCurrentProcess)
                    continue;
                if (candidate.StartedAt is null ||
                    DateTime.Now - candidate.StartedAt.Value < GracePeriod)
                    continue;

                try
                {
                    using var process = Process.GetProcessById(candidate.Pid);
                    process.Kill();
                    result.Terminated++;
                    result.Details.Add($"{candidate.Name} (PID {candidate.Pid})");
                }
                catch
                {
                    // Prozess ist bereits beendet oder geschützt.
                }
            }
        }

        return result;
    }

    private static ProcessInfo? Inspect(Process process)
    {
        if (process.Id <= 4)
            return null;

        // Dienste und Systemprozesse laufen in Session 0 – nie anfassen.
        if (process.SessionId == 0)
            return null;

        string name = process.ProcessName;
        if (string.IsNullOrWhiteSpace(name))
            return null;

        string? path;
        try { path = process.MainModule?.FileName; }
        catch { return null; }

        if (string.IsNullOrWhiteSpace(path) ||
            !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return null;

        // Windows-Systemdateien grundsätzlich ausnehmen.
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windows) &&
            path.StartsWith(windows + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return null;

        DateTime? startedAt = null;
        try { startedAt = process.StartTime; }
        catch { /* Zeitpunkt unbekannt */ }

        bool hasWindow = false;
        try { hasWindow = process.MainWindowHandle != IntPtr.Zero; }
        catch { /* Fenster unbekannt */ }

        return new ProcessInfo
        {
            Pid = process.Id,
            Name = name,
            Path = path,
            StartedAt = startedAt,
            HasWindow = hasWindow,
            IsCurrentProcess = process.Id == Environment.ProcessId
        };
    }

    private static bool IsHelperProcess(string name) =>
        HelperHints.Any(hint => name.Contains(hint, StringComparison.OrdinalIgnoreCase));

    private static bool IsProtected(string name) =>
        ProtectedNameFragments.Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase));

    private static bool IsMultiProcessFamily(string name) =>
        MultiProcessFamilies.Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase));

    public void Dispose()
    {
        Stop();
        _timer.Dispose();
    }

    private sealed class ProcessInfo
    {
        public int Pid { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Path { get; init; } = string.Empty;
        public DateTime? StartedAt { get; init; }
        public bool HasWindow { get; init; }
        public bool IsCurrentProcess { get; init; }
    }
}
