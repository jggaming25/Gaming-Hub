namespace GamingHub.Core.Models;

/// <summary>Persistierte Bildschirmzeit-Daten: Nutzung pro Tag, Profile und Regeln.</summary>
public sealed class ScreenTimeData
{
    public bool Enabled { get; set; } = true;

    /// <summary>Ob Regeln (Limits/Zeiten) tatsächlich durchgesetzt und blockiert werden.</summary>
    public bool Enforce { get; set; } = true;

    public string ActiveProfileId { get; set; } = string.Empty;

    public List<ScreenTimeProfile> Profiles { get; set; } = new();

    /// <summary>Nutzung je Tag (Schlüssel: yyyy-MM-dd).</summary>
    public Dictionary<string, ScreenTimeDay> Days { get; set; } = new();

    /// <summary>Datum, an dem die Bildschirmzeit manuell ausgeschaltet wurde (yyyy-MM-dd).</summary>
    public string? DisabledForDate { get; set; }

    /// <summary>Snooze je Prozess (Ablauf als Unix-Sekunden).</summary>
    public Dictionary<string, long> SnoozeUntil { get; set; } = new();

    /// <summary>Temporäre Freigabe nach Admin-Bestätigung (Unix-Sekunden).</summary>
    public long AccessOverrideUntil { get; set; }

    /// <summary>Tag, für den die temporäre Freigabe gilt (yyyy-MM-dd).</summary>
    public string? AccessOverrideDate { get; set; }

    /// <summary>Anzeigenamen zu Prozessnamen.</summary>
    public Dictionary<string, string> AppNames { get; set; } = new();

    /// <summary>Tag, für den die Limit-Warnungen gespeichert sind (yyyy-MM-dd).</summary>
    public string? LimitWarningsDate { get; set; }

    /// <summary>Bereits gesendete 15-/2-Minuten-Warnungen des aktuellen Tages.</summary>
    public List<string> LimitWarningsSent { get; set; } = new();
}

public sealed class ScreenTimeDay
{
    public string Date { get; set; } = string.Empty;
    public int TotalSeconds { get; set; }
    public Dictionary<string, int> AppSeconds { get; set; } = new();
}

public sealed class ScreenTimeProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Standard";

    /// <summary>Tageslimit über alle Apps in Minuten (0 = kein Limit).</summary>
    public int DailyLimitMinutes { get; set; }

    public bool BlockOutsideSchedule { get; set; }

    public string WeekdayStart { get; set; } = "07:00";
    public string WeekdayEnd { get; set; } = "22:00";
    public string WeekendStart { get; set; } = "08:00";
    public string WeekendEnd { get; set; } = "23:00";

    /// <summary>Individuelle Zeiten und Gesamtlimits für jeden Wochentag.</summary>
    public List<ScreenTimeWeekdayRule> Weekdays { get; set; } = new();

    public List<AppLimit> AppLimits { get; set; } = new();
}

public sealed class ScreenTimeWeekdayRule
{
    public DayOfWeek Day { get; set; }

    /// <summary>Beginn der erlaubten Bildschirmzeit (HH:mm).</summary>
    public string AllowedStart { get; set; } = "07:00";

    /// <summary>Ende der erlaubten Bildschirmzeit (HH:mm).</summary>
    public string AllowedEnd { get; set; } = "22:00";

    /// <summary>Gesamtlimit für diesen Wochentag in Minuten (0 = kein Limit).</summary>
    public int TotalLimitMinutes { get; set; }
}

public sealed class AppLimit
{
    public string ProcessName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Erlaubte Minuten pro Tag (0 = kein Limit).</summary>
    public int DailyMinutes { get; set; }

    /// <summary>Erlaubte Minuten pro Woche (0 = kein Limit).</summary>
    public int WeeklyMinutes { get; set; }

    public bool Enabled { get; set; } = true;
}

public enum ScreenTimeBlockKind
{
    /// <summary>Gesamtlimit oder erlaubte Zeit: alle Monitore grau, Freigabe möglich.</summary>
    FullScreen,

    /// <summary>App-Limit: nur das Fenster der App grau, App muss beendet werden.</summary>
    AppLimit
}

/// <summary>Eine aktive Sperre mit Grund (für das Hinweisfenster).</summary>
public sealed class ScreenTimeBlock
{
    public string ProcessName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public ScreenTimeBlockKind Kind { get; init; } = ScreenTimeBlockKind.FullScreen;
}
