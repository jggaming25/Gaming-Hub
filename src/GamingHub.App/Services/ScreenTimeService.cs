using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using GamingHub.Core.Models;

namespace GamingHub.App.Services;

/// <summary>
/// Bildschirmzeit: erfasst im Hintergrund die aktive Vordergrund-App samt
/// Sekunden pro Tag, verwaltet Profile mit Limits und Zeiten und meldet eine
/// Sperre, wenn ein Limit erreicht ist oder die erlaubte Zeit vorbei ist.
/// Nur für Premium-/Admin-Konten aktiv.
/// </summary>
public sealed class ScreenTimeService : IDisposable
{
    private static readonly HashSet<string> SkipProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "gaminghub.app", "gaminghub", "openrgb", "explorer", "dwm", "winlogon", "csrss",
        "services", "svchost", "system", "registry", "audiodg", "conhost", "idle",
        "searchhost", "startmenuexperiencehost", "shellexperiencehost", "textinputhost",
        "applicationframehost", "lockapp", "taskmgr", "securityhealthsystray",
        "runtimebroker", "backgroundtaskhost", "widgets", "widgetservice", "phoneexperiencehost"
    };

    /// <summary>
    /// Gaming Hub darf sich nicht selbst per App-Limit sperren. Sonst wuerde
    /// das Overlay die eigene Oberflaeche blockieren und die Sperre liesse sich
    /// nicht mehr komfortabel aufheben.
    /// </summary>
    public static bool IsGamingHubProcess(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;

        string name = processName.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        return name.Equals("gaminghub", StringComparison.OrdinalIgnoreCase)
            || name.Equals("gaminghub.app", StringComparison.OrdinalIgnoreCase)
            || name.Equals(Process.GetCurrentProcess().ProcessName, StringComparison.OrdinalIgnoreCase);
    }

    private readonly string _dataPath;
    private readonly object _gate = new();
    private readonly Timer _tickTimer;
    private readonly Timer _saveTimer;
    private readonly int _ownPid = Environment.ProcessId;
    private readonly ConcurrentDictionary<string, string> _displayNames = new(StringComparer.OrdinalIgnoreCase);

    private ScreenTimeData _data = new();
    private ScreenTimeBlock? _currentBlock;
    private string _lastDayKey = DateTime.Now.ToString("yyyy-MM-dd");
    private bool _dirty;
    private bool _disposed;

    /// <summary>Meldet Änderungen (Nutzung/Limits) – die UI aktualisiert darauf hin.</summary>
    public event Action? Changed;

    /// <summary>Meldet eine aktive Sperre (oder null, wenn sie aufgehoben wurde).</summary>
    public event Action<ScreenTimeBlock?>? BlockChanged;

    public ScreenTimeService(string dataDirectory)
    {
        _dataPath = Path.Combine(dataDirectory, "screentime.json");
        Load();
        _tickTimer = new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
        _saveTimer = new Timer(_ => SaveIfDirty(), null, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20));
    }

    public bool Enabled
    {
        get { lock (_gate) return _data.Enabled; }
        set { lock (_gate) _data.Enabled = value; MarkDirty(); Changed?.Invoke(); }
    }

    public bool Enforce
    {
        get { lock (_gate) return _data.Enforce; }
        set { lock (_gate) _data.Enforce = value; MarkDirty(); Changed?.Invoke(); }
    }

    public IReadOnlyList<ScreenTimeProfile> Profiles
    {
        get { lock (_gate) return _data.Profiles.ToList(); }
    }

    public ScreenTimeProfile? ActiveProfile
    {
        get
        {
            lock (_gate)
            {
                var profile = _data.Profiles.FirstOrDefault(p => p.Id == _data.ActiveProfileId);
                return profile ?? _data.Profiles.FirstOrDefault();
            }
        }
    }

    public string ActiveProfileName => ActiveProfile?.Name ?? "Kein Profil";

    public ScreenTimeBlock? CurrentBlock
    {
        get { lock (_gate) return _currentBlock; }
    }

    public string TodayKey => DateTime.Now.ToString("yyyy-MM-dd");

    public int TodayTotalSeconds
    {
        get { lock (_gate) return GetDayLocked(TodayKey).TotalSeconds; }
    }

    public int TodayLimitMinutes
    {
        get
        {
            lock (_gate)
            {
                var profile = ActiveProfileLocked();
                return profile is null ? 0 : GetTodayRuleLocked(profile, DateTime.Now).TotalLimitMinutes;
            }
        }
    }

    public IReadOnlyList<(DateTime Date, int Seconds)> GetDailyTotals(int days)
    {
        days = Math.Clamp(days, 1, 60);
        lock (_gate)
        {
            var list = new List<(DateTime Date, int Seconds)>(days);
            for (int i = days - 1; i >= 0; i--)
            {
                var date = DateTime.Today.AddDays(-i);
                var key = date.ToString("yyyy-MM-dd");
                _data.Days.TryGetValue(key, out var day);
                list.Add((date, day?.TotalSeconds ?? 0));
            }
            return list;
        }
    }

    public bool IsDisabledForToday
    {
        get { lock (_gate) return _data.DisabledForDate == TodayKey; }
    }

    public void Start()
    {
        _tickTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    public void Stop()
    {
        _tickTimer.Change(Timeout.Infinite, Timeout.Infinite);
        SaveIfDirty();
    }

    public int GetTodaySeconds(string processName)
    {
        lock (_gate)
        {
            var day = GetDayLocked(TodayKey);
            return day.AppSeconds.TryGetValue(processName, out var s) ? s : 0;
        }
    }

    public int GetWeekSeconds(string processName)
    {
        lock (_gate)
        {
            int total = 0;
            for (int i = 0; i < 7; i++)
            {
                var key = DateTime.Now.AddDays(-i).ToString("yyyy-MM-dd");
                if (_data.Days.TryGetValue(key, out var day) &&
                    day.AppSeconds.TryGetValue(processName, out var s))
                    total += s;
            }
            return total;
        }
    }

    public IReadOnlyList<(string Process, string Display, int Seconds)> GetTopApps(int max = 12)
    {
        lock (_gate)
        {
            var day = GetDayLocked(TodayKey);
            return day.AppSeconds
                .OrderByDescending(kv => kv.Value)
                .Take(Math.Max(1, max))
                .Select(kv => (kv.Key, DisplayNameLocked(kv.Key), kv.Value))
                .ToList();
        }
    }

    public string GetDisplayName(string processName)
    {
        lock (_gate) return DisplayNameLocked(processName);
    }

    public void EnsureProfile()
    {
        lock (_gate)
        {
            if (_data.Profiles.Count == 0)
                _data.Profiles.Add(new ScreenTimeProfile { Name = "Standard" });
            if (string.IsNullOrWhiteSpace(_data.ActiveProfileId) ||
                _data.Profiles.All(p => p.Id != _data.ActiveProfileId))
                _data.ActiveProfileId = _data.Profiles[0].Id;
            foreach (var profile in _data.Profiles)
                EnsureWeekdaysLocked(profile);
            MarkDirty();
        }
        Changed?.Invoke();
    }

    public void UpdateActiveProfile(Action<ScreenTimeProfile> mutate)
    {
        lock (_gate)
        {
            var profile = _data.Profiles.FirstOrDefault(p => p.Id == _data.ActiveProfileId)
                          ?? _data.Profiles.FirstOrDefault();
            if (profile is null) return;
            mutate(profile);
            MarkDirty();
        }
        Changed?.Invoke();
    }

    public void AddAppLimit(string processName, string displayName)
    {
        if (string.IsNullOrWhiteSpace(processName) || IsGamingHubProcess(processName)) return;
        lock (_gate)
        {
            var profile = _data.Profiles.FirstOrDefault(p => p.Id == _data.ActiveProfileId)
                          ?? _data.Profiles.FirstOrDefault();
            if (profile is null) return;
            if (profile.AppLimits.Any(a => string.Equals(a.ProcessName, processName, StringComparison.OrdinalIgnoreCase)))
                return;

            profile.AppLimits.Add(new AppLimit
            {
                ProcessName = processName,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? processName : displayName
            });
            if (!_data.AppNames.ContainsKey(processName) && !string.IsNullOrWhiteSpace(displayName))
                _data.AppNames[processName] = displayName;
            MarkDirty();
        }
        Changed?.Invoke();
    }

    public void RemoveAppLimit(string processName)
    {
        lock (_gate)
        {
            var profile = ActiveProfileLocked();
            if (profile is null) return;
            profile.AppLimits.RemoveAll(a => string.Equals(a.ProcessName, processName, StringComparison.OrdinalIgnoreCase));
            MarkDirty();
        }
        Changed?.Invoke();
    }

    public void SaveProfile(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        lock (_gate)
        {
            var active = ActiveProfileLocked();
            var profile = new ScreenTimeProfile
            {
                Name = name.Trim(),
                DailyLimitMinutes = active?.DailyLimitMinutes ?? 0,
                BlockOutsideSchedule = active?.BlockOutsideSchedule ?? false,
                WeekdayStart = active?.WeekdayStart ?? "07:00",
                WeekdayEnd = active?.WeekdayEnd ?? "22:00",
                WeekendStart = active?.WeekendStart ?? "08:00",
                WeekendEnd = active?.WeekendEnd ?? "23:00",
                Weekdays = active?.Weekdays.Select(CloneWeekday).ToList() ?? new List<ScreenTimeWeekdayRule>(),
                AppLimits = active?.AppLimits.Select(CloneLimit).ToList() ?? new List<AppLimit>()
            };
            _data.Profiles.Add(profile);
            _data.ActiveProfileId = profile.Id;
            MarkDirty();
        }
        Changed?.Invoke();
    }

    public void ApplyProfile(string profileId)
    {
        lock (_gate)
        {
            if (_data.Profiles.All(p => p.Id != profileId)) return;
            _data.ActiveProfileId = profileId;
            MarkDirty();
        }
        Changed?.Invoke();
    }

    public void DeleteProfile(string profileId)
    {
        lock (_gate)
        {
            if (_data.Profiles.Count <= 1) return;
            _data.Profiles.RemoveAll(p => p.Id == profileId);
            if (_data.ActiveProfileId == profileId)
                _data.ActiveProfileId = _data.Profiles[0].Id;
            MarkDirty();
        }
        Changed?.Invoke();
    }

    public void GrantSnooze(int minutes) => GrantAccess(minutes);

    /// <summary>
    /// Gibt die Bildschirmzeit nach Admin-Bestätigung für die angegebenen
    /// Minuten frei. Die Freigabe endet spätestens um Mitternacht, damit das
    /// Tageslimit um 00:00 Uhr wieder greift.
    /// </summary>
    public void GrantAccess(int minutes)
    {
        lock (_gate)
        {
            var now = DateTime.Now;
            var end = now.AddMinutes(Math.Clamp(minutes, 1, 24 * 60));
            var midnight = now.Date.AddDays(1);
            if (end > midnight) end = midnight;
            if (end <= now) end = midnight;

            _data.AccessOverrideDate = TodayKey;
            _data.AccessOverrideUntil = new DateTimeOffset(end).ToUnixTimeSeconds();
            _currentBlock = null;
            MarkDirty();
        }
        BlockChanged?.Invoke(null);
        Changed?.Invoke();
    }

    /// <summary>Beendet alle Prozesse einer durch ein App-Limit gesperrten Anwendung.</summary>
    public void TerminateProcess(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return;
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try { process.Kill(entireProcessTree: true); }
            catch { /* Prozess kann bereits beendet sein. */ }
            finally { process.Dispose(); }
        }
    }

    public void DisableForToday()
    {
        lock (_gate)
        {
            _data.DisabledForDate = TodayKey;
            MarkDirty();
            _currentBlock = null;
        }
        BlockChanged?.Invoke(null);
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------
    // Erfassung
    // ---------------------------------------------------------------

    private void Tick()
    {
        if (_disposed) return;

        bool enabled;
        bool enforce;
        bool canEnforce;
        bool fullScreenBlocked;
        lock (_gate)
        {
            ResetDayLocked();
            enabled = _data.Enabled;
            enforce = _data.Enforce;
            canEnforce = HasAccess();
            fullScreenBlocked = _currentBlock?.Kind == ScreenTimeBlockKind.FullScreen;
        }

        if (!enabled)
        {
            SetBlock(null);
            return;
        }

        // Während der Vollbild-Sperre ist kein anderer Inhalt nutzbar, also
        // wird die Zeit auf dem Sperrbildschirm nicht als Nutzung gezählt.
        // Die Fenster werden trotzdem ermittelt, damit ein App-Limit direkt
        // nach dem Ende der Vollbild-Sperre ohne Verzögerung greift.
        bool idle = IsUserIdle();
        var apps = new List<OpenWindowApp>();
        if (fullScreenBlocked || !idle)
            apps = GetOpenWindowApps();

        // Nutzung wird für alle erfasst – Limits/Sperren nur mit Premium/Admin.
        if (!fullScreenBlocked && apps.Count > 0)
        {
            lock (_gate)
            {
                var day = GetDayLocked(TodayKey);
                foreach (var app in apps
                             .GroupBy(a => a.ProcessName, StringComparer.OrdinalIgnoreCase)
                             .Select(g => g.First()))
                {
                    day.AppSeconds.TryGetValue(app.ProcessName, out var current);
                    day.AppSeconds[app.ProcessName] = current + 1;
                    if (!string.IsNullOrWhiteSpace(app.DisplayName))
                        _data.AppNames[app.ProcessName] = app.DisplayName;
                }
                day.TotalSeconds += 1;
                _dirty = true;
            }
        }

        if (canEnforce && enforce)
        {
            EvaluateBlock(apps);
            CheckLimitWarnings(apps);
        }
        else
        {
            SetBlock(null);
        }

        if (DateTime.Now.Second % 10 == 0)
            Changed?.Invoke();
    }

    private void EvaluateBlock(IReadOnlyList<OpenWindowApp> apps)
    {
        ScreenTimeBlock? block = null;
        lock (_gate)
        {
            var profile = ActiveProfileLocked();
            if (profile is not null)
            {
                var now = DateTime.Now;
                var rule = GetTodayRuleLocked(profile, now);

                // Eine Freigabe gilt nur für die Gesamt-/Zeitlimits. App-Limits
                // müssen unabhängig davon weiter durchgesetzt werden.
                if (!IsAccessOverrideActiveLocked(now))
                {
                    if (profile.BlockOutsideSchedule && !WithinSchedule(rule, now))
                    {
                        block = new ScreenTimeBlock
                        {
                            Reason = $"Außerhalb der erlaubten Bildschirmzeit (heute erlaubt: {rule.AllowedStart}–{rule.AllowedEnd} Uhr)."
                        };
                    }
                    else if (rule.TotalLimitMinutes > 0 &&
                             GetDayLocked(TodayKey).TotalSeconds >= rule.TotalLimitMinutes * 60)
                    {
                        block = new ScreenTimeBlock
                        {
                            Reason = $"Das Tageslimit von {rule.TotalLimitMinutes} Minuten ist erreicht."
                        };
                    }
                }

                if (block is null)
                {
                    foreach (var app in GetLimitCandidatesLocked(profile, apps))
                    {
                        var limit = profile.AppLimits.FirstOrDefault(a =>
                            a.Enabled && string.Equals(a.ProcessName, app.ProcessName, StringComparison.OrdinalIgnoreCase));
                        if (limit is null) continue;

                        var displayName = string.IsNullOrWhiteSpace(app.DisplayName)
                            ? DisplayNameLocked(app.ProcessName)
                            : app.DisplayName;
                        int today = GetTodaySecondsLocked(app.ProcessName);
                        int week = GetWeekSecondsLocked(app.ProcessName);
                        if (limit.DailyMinutes > 0 && today >= limit.DailyMinutes * 60)
                        {
                            block = new ScreenTimeBlock
                            {
                                ProcessName = app.ProcessName,
                                DisplayName = displayName,
                                Reason = $"Für „{displayName}“ sind {limit.DailyMinutes} Minuten pro Tag erreicht. Die App muss jetzt beendet werden.",
                                Kind = ScreenTimeBlockKind.AppLimit
                            };
                            break;
                        }
                        if (limit.WeeklyMinutes > 0 && week >= limit.WeeklyMinutes * 60)
                        {
                            block = new ScreenTimeBlock
                            {
                                ProcessName = app.ProcessName,
                                DisplayName = displayName,
                                Reason = $"Für „{displayName}“ sind {limit.WeeklyMinutes} Minuten pro Woche erreicht. Die App muss jetzt beendet werden.",
                                Kind = ScreenTimeBlockKind.AppLimit
                            };
                            break;
                        }
                    }
                }
            }
        }

        SetBlock(block);
    }

    /// <summary>
    /// Prüft alle Limits und zeigt je Limit einmal eine Warnung bei weniger als
    /// 15 Minuten und einmal bei weniger als 2 Minuten Restzeit. Ist ein Limit
    /// bereits näher, wird nur die noch sinnvolle Warnung gesendet.
    /// </summary>
    private void CheckLimitWarnings(IReadOnlyList<OpenWindowApp> apps)
    {
        var warnings = new List<(string Title, string Body)>();
        lock (_gate)
        {
            if (_currentBlock?.Kind == ScreenTimeBlockKind.FullScreen)
                return;

            var profile = ActiveProfileLocked();
            if (profile is null)
                return;

            var now = DateTime.Now;
            var rule = GetTodayRuleLocked(profile, now);

            if (!IsAccessOverrideActiveLocked(now) && rule.TotalLimitMinutes > 0)
            {
                int remaining = rule.TotalLimitMinutes * 60 - GetDayLocked(TodayKey).TotalSeconds;
                AddLimitWarningLocked(warnings,
                    $"total:{rule.TotalLimitMinutes}",
                    remaining,
                    $"Tageslimit {rule.TotalLimitMinutes} Min.",
                    "Bildschirmzeit");
            }

            foreach (var app in GetLimitCandidatesLocked(profile, apps))
            {
                var limit = profile.AppLimits.FirstOrDefault(a =>
                    a.Enabled && string.Equals(a.ProcessName, app.ProcessName, StringComparison.OrdinalIgnoreCase));
                if (limit is null) continue;

                string displayName = string.IsNullOrWhiteSpace(app.DisplayName)
                    ? DisplayNameLocked(app.ProcessName)
                    : app.DisplayName;

                if (limit.DailyMinutes > 0)
                {
                    int remaining = limit.DailyMinutes * 60 - GetTodaySecondsLocked(app.ProcessName);
                    AddLimitWarningLocked(warnings,
                        $"app:{app.ProcessName}:daily:{limit.DailyMinutes}",
                        remaining,
                        $"Tageslimit {limit.DailyMinutes} Min.",
                        displayName);
                }

                if (limit.WeeklyMinutes > 0)
                {
                    int remaining = limit.WeeklyMinutes * 60 - GetWeekSecondsLocked(app.ProcessName);
                    AddLimitWarningLocked(warnings,
                        $"app:{app.ProcessName}:weekly:{limit.WeeklyMinutes}",
                        remaining,
                        $"Wochenlimit {limit.WeeklyMinutes} Min.",
                        displayName);
                }
            }
        }

        foreach (var (title, body) in warnings)
            AppServices.Notifications.Show(title, body);
    }

    private void AddLimitWarningLocked(List<(string Title, string Body)> warnings,
        string key, int remainingSeconds, string limitLabel, string title)
    {
        if (remainingSeconds <= 0) return;

        int threshold = remainingSeconds <= 120 ? 2 : remainingSeconds <= 900 ? 15 : 0;
        if (threshold == 0) return;

        if (_data.LimitWarningsDate != TodayKey)
        {
            _data.LimitWarningsDate = TodayKey;
            _data.LimitWarningsSent.Clear();
        }

        string sentKey = $"{key}:{threshold}";
        if (_data.LimitWarningsSent.Contains(sentKey, StringComparer.OrdinalIgnoreCase))
            return;

        _data.LimitWarningsSent.Add(sentKey);
        MarkDirty();

        string body = threshold == 2
            ? $"{limitLabel}: In weniger als 2 Minuten ist das Limit erreicht."
            : $"{limitLabel}: In weniger als 15 Minuten ist das Limit erreicht.";
        warnings.Add((title, body));
    }

    /// <summary>
    /// Liefert alle Apps, für die ein Limit gelten kann: sichtbare Fenster und
    /// zusätzlich laufende Prozesse, die z. B. nur im Infobereich laufen.
    /// </summary>
    private List<OpenWindowApp> GetLimitCandidatesLocked(ScreenTimeProfile profile,
        IReadOnlyList<OpenWindowApp> apps)
    {
        var result = new Dictionary<string, OpenWindowApp>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in apps)
        {
            if (IsGamingHubProcess(app.ProcessName)) continue;
            if (!string.IsNullOrWhiteSpace(app.ProcessName) && !result.ContainsKey(app.ProcessName))
                result[app.ProcessName] = app;
        }

        foreach (var limit in profile.AppLimits)
        {
            if (!limit.Enabled || string.IsNullOrWhiteSpace(limit.ProcessName))
                continue;
            if (IsGamingHubProcess(limit.ProcessName))
                continue;
            if (result.ContainsKey(limit.ProcessName) || !IsProcessRunning(limit.ProcessName))
                continue;

            string displayName = string.IsNullOrWhiteSpace(limit.DisplayName)
                ? DisplayNameLocked(limit.ProcessName)
                : limit.DisplayName;
            result[limit.ProcessName] = new OpenWindowApp(limit.ProcessName, displayName);
        }

        return result.Values.ToList();
    }

    private static bool IsProcessRunning(string processName)
    {
        try
        {
            var processes = Process.GetProcessesByName(processName);
            try { return processes.Length > 0; }
            finally
            {
                foreach (var process in processes)
                    process.Dispose();
            }
        }
        catch
        {
            return false;
        }
    }

    private void SetBlock(ScreenTimeBlock? block)
    {
        bool changed;
        lock (_gate)
        {
            changed = (_currentBlock is null) != (block is null) ||
                      (_currentBlock is not null && block is not null &&
                       (_currentBlock.Kind != block.Kind ||
                        _currentBlock.ProcessName != block.ProcessName ||
                        _currentBlock.Reason != block.Reason));
            _currentBlock = block;
        }

        if (changed)
            BlockChanged?.Invoke(block);
    }

    // ---------------------------------------------------------------
    // Hilfsfunktionen
    // ---------------------------------------------------------------

    private static bool HasAccess() =>
        AppServices.Profile?.IsAdmin == true ||
        (AppServices.Profile?.IsPremium == true && AppServices.Profile?.PremiumBanned != true);

    private ScreenTimeProfile? ActiveProfileLocked() =>
        _data.Profiles.FirstOrDefault(p => p.Id == _data.ActiveProfileId) ?? _data.Profiles.FirstOrDefault();

    private ScreenTimeDay GetDayLocked(string key)
    {
        if (!_data.Days.TryGetValue(key, out var day))
        {
            day = new ScreenTimeDay { Date = key };
            _data.Days[key] = day;
            PruneOldDaysLocked();
        }
        return day;
    }

    private void PruneOldDaysLocked()
    {
        if (_data.Days.Count < 70) return;
        var cutoff = DateTime.Now.AddDays(-60).ToString("yyyy-MM-dd");
        foreach (var key in _data.Days.Keys.Where(k => string.CompareOrdinal(k, cutoff) < 0).ToList())
            _data.Days.Remove(key);
    }

    private int GetTodaySecondsLocked(string processName)
    {
        var day = GetDayLocked(TodayKey);
        return day.AppSeconds.TryGetValue(processName, out var s) ? s : 0;
    }

    private int GetWeekSecondsLocked(string processName)
    {
        int total = 0;
        for (int i = 0; i < 7; i++)
        {
            var key = DateTime.Now.AddDays(-i).ToString("yyyy-MM-dd");
            if (_data.Days.TryGetValue(key, out var day) &&
                day.AppSeconds.TryGetValue(processName, out var s))
                total += s;
        }
        return total;
    }

    private string DisplayNameLocked(string processName)
    {
        if (_data.AppNames.TryGetValue(processName, out var name) && !string.IsNullOrWhiteSpace(name))
            return name;
        return processName;
    }

    private static bool WithinSchedule(ScreenTimeWeekdayRule rule, DateTime now)
    {
        if (!TryParseTime(rule.AllowedStart, out var s) || !TryParseTime(rule.AllowedEnd, out var e))
            return true;

        var current = now.TimeOfDay;
        if (s <= e)
            return current >= s && current <= e;
        // Über Mitternacht (z. B. 20:00–06:00)
        return current >= s || current <= e;
    }

    private static void EnsureWeekdaysLocked(ScreenTimeProfile profile)
    {
        profile.Weekdays ??= new List<ScreenTimeWeekdayRule>();
        bool migrated = profile.Weekdays.Count == 0;

        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            if (profile.Weekdays.Any(r => r.Day == day))
                continue;

            bool weekend = day is DayOfWeek.Saturday or DayOfWeek.Sunday;
            profile.Weekdays.Add(new ScreenTimeWeekdayRule
            {
                Day = day,
                AllowedStart = weekend ? profile.WeekendStart : profile.WeekdayStart,
                AllowedEnd = weekend ? profile.WeekendEnd : profile.WeekdayEnd,
                TotalLimitMinutes = migrated ? profile.DailyLimitMinutes : 0
            });
        }

        profile.Weekdays.Sort((a, b) => ((int)a.Day + 6) % 7 - ((int)b.Day + 6) % 7);
    }

    private static ScreenTimeWeekdayRule GetTodayRuleLocked(ScreenTimeProfile profile, DateTime now)
    {
        EnsureWeekdaysLocked(profile);
        return profile.Weekdays.First(r => r.Day == now.DayOfWeek);
    }

    private bool IsAccessOverrideActiveLocked(DateTime now)
    {
        if (_data.AccessOverrideDate != TodayKey) return false;
        return _data.AccessOverrideUntil > new DateTimeOffset(now).ToUnixTimeSeconds();
    }

    private void ResetDayLocked()
    {
        var today = TodayKey;
        if (_lastDayKey == today) return;

        _lastDayKey = today;
        _data.AccessOverrideDate = null;
        _data.AccessOverrideUntil = 0;
        _data.DisabledForDate = null;
        _data.SnoozeUntil.Clear();
        _data.LimitWarningsDate = today;
        _data.LimitWarningsSent.Clear();
        MarkDirty();
    }

    private static bool TryParseTime(string value, out TimeSpan time)
    {
        time = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return TimeSpan.TryParseExact(value.Trim(), new[] { @"h\:mm", @"hh\:mm" },
            System.Globalization.CultureInfo.InvariantCulture, out time);
    }

    private static ScreenTimeWeekdayRule CloneWeekday(ScreenTimeWeekdayRule rule) => new()
    {
        Day = rule.Day,
        AllowedStart = rule.AllowedStart,
        AllowedEnd = rule.AllowedEnd,
        TotalLimitMinutes = rule.TotalLimitMinutes
    };

    private static AppLimit CloneLimit(AppLimit limit) => new()
    {
        ProcessName = limit.ProcessName,
        DisplayName = limit.DisplayName,
        DailyMinutes = limit.DailyMinutes,
        WeeklyMinutes = limit.WeeklyMinutes,
        Enabled = limit.Enabled
    };

    private bool IsUserIdle()
    {
        try
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref info)) return false;
            uint elapsed = unchecked((uint)Environment.TickCount - info.dwTime);
            return elapsed > 5 * 60_000;
        }
        catch
        {
            return false;
        }
    }

    private List<OpenWindowApp> GetOpenWindowApps()
    {
        var apps = new List<OpenWindowApp>();
        var ownPid = (uint)_ownPid;

        try
        {
            EnumWindows((hwnd, _) =>
            {
                try
                {
                    if (!IsWindowVisible(hwnd)) return true;
                    if (GetWindowTextLength(hwnd) == 0) return true;
                    if ((GetWindowLongPtr(hwnd, -20).ToInt64() & 0x80L) != 0) return true;
                    if (IsWindowCloaked(hwnd)) return true;

                    GetWindowThreadProcessId(hwnd, out uint pid);
                    if (pid == 0 || pid == ownPid) return true;

                    using var process = Process.GetProcessById((int)pid);
                    var name = process.ProcessName;
                    if (string.IsNullOrWhiteSpace(name) || SkipProcesses.Contains(name)) return true;

                    apps.Add(new OpenWindowApp(name, GetDisplayNameForProcess(name, process)));
                }
                catch
                {
                    // Zugriff auf einzelne Prozesse kann verweigert werden.
                }
                return true;
            }, IntPtr.Zero);
        }
        catch
        {
            // Aufzählung fehlgeschlagen -> keine Nutzung zählen.
        }

        return apps
            .GroupBy(a => a.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private string GetDisplayNameForProcess(string processName, Process process)
    {
        if (_displayNames.TryGetValue(processName, out var cached))
            return cached;

        string? display = null;
        try { display = process.MainModule?.FileVersionInfo?.FileDescription; } catch { /* Zugriff verweigert */ }
        if (string.IsNullOrWhiteSpace(display))
        {
            try { display = process.MainModule?.FileVersionInfo?.ProductName; } catch { /* ignore */ }
        }
        if (string.IsNullOrWhiteSpace(display))
            display = processName;

        _displayNames[processName] = display;
        return display;
    }

    private static bool IsWindowCloaked(IntPtr hwnd)
    {
        try
        {
            return DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0;
        }
        catch
        {
            return false;
        }
    }

    private void MarkDirty() => _dirty = true;

    private void SaveIfDirty()
    {
        if (!_dirty) return;
        _dirty = false;
        Save();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_dataPath))
            {
                _data = JsonSerializer.Deserialize<ScreenTimeData>(File.ReadAllText(_dataPath)) ?? new ScreenTimeData();
            }
        }
        catch
        {
            _data = new ScreenTimeData();
        }

        _data.Profiles ??= new List<ScreenTimeProfile>();
        _data.Days ??= new Dictionary<string, ScreenTimeDay>();
        _data.SnoozeUntil ??= new Dictionary<string, long>();
        _data.AppNames ??= new Dictionary<string, string>();
        _data.LimitWarningsSent ??= new List<string>();
        if (_data.LimitWarningsDate != TodayKey)
        {
            _data.LimitWarningsDate = TodayKey;
            _data.LimitWarningsSent.Clear();
        }

        // Frühere Versionen konnten die Bildschirmzeit für den Rest des Tages
        // deaktivieren. Diese Option gibt es in der UI nicht mehr; ein alter
        // gespeicherter Wert darf Limits deshalb nicht dauerhaft aushebeln.
        _data.DisabledForDate = null;

        _data.AppNames = NormalizeMap(_data.AppNames);
        _data.SnoozeUntil = NormalizeMap(_data.SnoozeUntil);

        foreach (var day in _data.Days.Values)
            day.AppSeconds = NormalizeCounts(day.AppSeconds);

        if (_data.Profiles.Count == 0)
            _data.Profiles.Add(new ScreenTimeProfile { Name = "Standard" });
        if (string.IsNullOrWhiteSpace(_data.ActiveProfileId) ||
            _data.Profiles.All(p => p.Id != _data.ActiveProfileId))
            _data.ActiveProfileId = _data.Profiles[0].Id;

        foreach (var profile in _data.Profiles)
        {
            profile.AppLimits ??= new List<AppLimit>();
            profile.Weekdays ??= new List<ScreenTimeWeekdayRule>();
            if (profile.AppLimits.RemoveAll(a => IsGamingHubProcess(a.ProcessName)) > 0)
                MarkDirty();
            EnsureWeekdaysLocked(profile);
        }
        _lastDayKey = TodayKey;
    }

    private static Dictionary<string, int> NormalizeCounts(Dictionary<string, int>? source)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (source is null) return result;

        foreach (var (key, value) in source)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            result[key] = result.TryGetValue(key, out var existing) ? existing + value : value;
        }
        return result;
    }

    private static Dictionary<string, string> NormalizeMap(Dictionary<string, string>? source)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (source is null) return result;

        foreach (var (key, value) in source)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (!result.TryGetValue(key, out var existing) || IsBetterDisplayName(value, existing))
                result[key] = value;
        }
        return result;
    }

    private static bool IsBetterDisplayName(string? candidate, string? current)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        if (string.IsNullOrWhiteSpace(current)) return true;

        bool candidateHasUpper = candidate.Any(char.IsUpper);
        bool currentHasUpper = current.Any(char.IsUpper);
        if (candidateHasUpper != currentHasUpper)
            return candidateHasUpper;

        return candidate.Length > current.Length;
    }

    private static Dictionary<string, long> NormalizeMap(Dictionary<string, long>? source)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        if (source is null) return result;

        foreach (var (key, value) in source)
        {
            if (!string.IsNullOrWhiteSpace(key))
                result[key] = value;
        }
        return result;
    }

    public void Save()
    {
        try
        {
            lock (_gate)
            {
                File.WriteAllText(_dataPath,
                    JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch
        {
            // Persistenzfehler ignorieren.
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _tickTimer.Dispose();
        _saveTimer.Dispose();
        Save();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private sealed record OpenWindowApp(string ProcessName, string DisplayName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);
}
