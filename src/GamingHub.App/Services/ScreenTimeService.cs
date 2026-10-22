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

    private readonly string _dataPath;
    private readonly object _gate = new();
    private readonly Timer _tickTimer;
    private readonly Timer _saveTimer;
    private readonly int _ownPid = Environment.ProcessId;

    private ScreenTimeData _data = new();
    private ScreenTimeBlock? _currentBlock;
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

    public string TodayKey => DateTime.Now.ToString("yyyy-MM-dd");

    public int TodayTotalSeconds
    {
        get { lock (_gate) return GetDayLocked(TodayKey).TotalSeconds; }
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
        if (string.IsNullOrWhiteSpace(processName)) return;
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

    public void GrantSnooze(int minutes)
    {
        string? process;
        lock (_gate)
        {
            process = _currentBlock?.ProcessName;
            if (process is null) return;
            _data.SnoozeUntil[process] = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(minutes, 1, 240)).ToUnixTimeSeconds();
            MarkDirty();
            _currentBlock = null;
        }
        BlockChanged?.Invoke(null);
        Changed?.Invoke();
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

        bool allowed;
        bool enabled;
        bool enforce;
        bool disabledToday;
        lock (_gate)
        {
            enabled = _data.Enabled;
            enforce = _data.Enforce;
            disabledToday = _data.DisabledForDate == TodayKey;
            allowed = HasAccess();
        }

        if (!allowed || !enabled)
        {
            SetBlock(null);
            return;
        }

        string? process = null;
        bool foregroundIsSelf = false;
        if (!disabledToday && !IsUserIdle())
            process = GetForegroundProcessName(out foregroundIsSelf);

        if (process is not null)
        {
            lock (_gate)
            {
                var day = GetDayLocked(TodayKey);
                day.AppSeconds.TryGetValue(process, out var current);
                day.AppSeconds[process] = current + 1;
                day.TotalSeconds += 1;
                _dirty = true;
            }
        }

        // Das Sperrfenster gehört zur App. Wenn es den Fokus hat, darf die
        // laufende Sperre nicht sofort wieder aufgehoben werden.
        if (foregroundIsSelf) return;

        if (enforce && !disabledToday && process is not null)
            EvaluateBlock(process);
        else
            SetBlock(null);

        if (DateTime.Now.Second % 10 == 0)
            Changed?.Invoke();
    }

    private void EvaluateBlock(string? process)
    {
        ScreenTimeBlock? block = null;
        lock (_gate)
        {
            var profile = ActiveProfileLocked();
            if (profile is not null)
            {
                var now = DateTime.Now;
                var display = process is null ? string.Empty : DisplayNameLocked(process);
                long snoozeUntil = 0;
                if (process is not null)
                    _data.SnoozeUntil.TryGetValue(process, out snoozeUntil);

                bool snoozed = process is not null && snoozeUntil > DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                if (!snoozed)
                {
                    if (profile.BlockOutsideSchedule && !WithinSchedule(profile, now))
                    {
                        string from = IsWeekend(now) ? profile.WeekendStart : profile.WeekdayStart;
                        string to = IsWeekend(now) ? profile.WeekendEnd : profile.WeekdayEnd;
                        block = new ScreenTimeBlock
                        {
                            ProcessName = process ?? string.Empty,
                            DisplayName = display,
                            Reason = $"Außerhalb der erlaubten Bildschirmzeit (erlaubt: {from}–{to} Uhr)."
                        };
                    }
                    else if (profile.DailyLimitMinutes > 0 &&
                             GetDayLocked(TodayKey).TotalSeconds >= profile.DailyLimitMinutes * 60)
                    {
                        block = new ScreenTimeBlock
                        {
                            ProcessName = process ?? string.Empty,
                            DisplayName = display,
                            Reason = $"Das Tageslimit von {profile.DailyLimitMinutes} Minuten ist erreicht."
                        };
                    }
                    else if (process is not null)
                    {
                        var limit = profile.AppLimits.FirstOrDefault(a =>
                            a.Enabled && string.Equals(a.ProcessName, process, StringComparison.OrdinalIgnoreCase));
                        if (limit is not null)
                        {
                            int today = GetTodaySecondsLocked(process);
                            int week = GetWeekSecondsLocked(process);
                            if (limit.DailyMinutes > 0 && today >= limit.DailyMinutes * 60)
                            {
                                block = new ScreenTimeBlock
                                {
                                    ProcessName = process,
                                    DisplayName = display,
                                    Reason = $"Für „{display}“ sind {limit.DailyMinutes} Minuten pro Tag erreicht."
                                };
                            }
                            else if (limit.WeeklyMinutes > 0 && week >= limit.WeeklyMinutes * 60)
                            {
                                block = new ScreenTimeBlock
                                {
                                    ProcessName = process,
                                    DisplayName = display,
                                    Reason = $"Für „{display}“ sind {limit.WeeklyMinutes} Minuten pro Woche erreicht."
                                };
                            }
                        }
                    }
                }
            }
        }

        SetBlock(block);
    }

    private void SetBlock(ScreenTimeBlock? block)
    {
        bool changed;
        lock (_gate)
        {
            changed = (_currentBlock is null) != (block is null) ||
                      (_currentBlock is not null && block is not null &&
                       (_currentBlock.ProcessName != block.ProcessName || _currentBlock.Reason != block.Reason));
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

    private static bool IsWeekend(DateTime now) =>
        now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private static bool WithinSchedule(ScreenTimeProfile profile, DateTime now)
    {
        bool weekend = IsWeekend(now);
        string start = weekend ? profile.WeekendStart : profile.WeekdayStart;
        string end = weekend ? profile.WeekendEnd : profile.WeekdayEnd;
        if (!TryParseTime(start, out var s) || !TryParseTime(end, out var e))
            return true;

        var current = now.TimeOfDay;
        if (s <= e)
            return current >= s && current <= e;
        // Über Mitternacht (z. B. 20:00–06:00)
        return current >= s || current <= e;
    }

    private static bool TryParseTime(string value, out TimeSpan time)
    {
        time = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return TimeSpan.TryParseExact(value.Trim(), new[] { @"h\:mm", @"hh\:mm" },
            System.Globalization.CultureInfo.InvariantCulture, out time);
    }

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

    private string? GetForegroundProcessName(out bool isSelf)
    {
        isSelf = false;
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return null;

            using var process = Process.GetProcessById((int)pid);
            string name = process.ProcessName;
            if (pid == (uint)_ownPid)
            {
                isSelf = true;
                return null;
            }
            if (string.IsNullOrWhiteSpace(name) || SkipProcesses.Contains(name))
                return null;

            string? display = null;
            try { display = process.MainModule?.FileVersionInfo?.FileDescription; } catch { /* Zugriff verweigert */ }
            if (string.IsNullOrWhiteSpace(display))
            {
                try { display = process.MainModule?.FileVersionInfo?.ProductName; } catch { /* ignore */ }
            }

            if (!string.IsNullOrWhiteSpace(display))
            {
                lock (_gate)
                {
                    if (!_data.AppNames.TryGetValue(name, out var existing) || existing != display)
                    {
                        _data.AppNames[name] = display!;
                        _dirty = true;
                    }
                }
            }
            return name;
        }
        catch
        {
            return null;
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

        if (_data.Profiles.Count == 0)
            _data.Profiles.Add(new ScreenTimeProfile { Name = "Standard" });
        if (string.IsNullOrWhiteSpace(_data.ActiveProfileId) ||
            _data.Profiles.All(p => p.Id != _data.ActiveProfileId))
            _data.ActiveProfileId = _data.Profiles[0].Id;

        foreach (var profile in _data.Profiles)
            profile.AppLimits ??= new List<AppLimit>();
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

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
}
