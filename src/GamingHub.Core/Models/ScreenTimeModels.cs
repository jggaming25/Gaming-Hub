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

    /// <summary>Anzeigenamen zu Prozessnamen.</summary>
    public Dictionary<string, string> AppNames { get; set; } = new();
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

    public List<AppLimit> AppLimits { get; set; } = new();
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

/// <summary>Eine aktive Sperre mit Grund (für das Hinweisfenster).</summary>
public sealed class ScreenTimeBlock
{
    public string ProcessName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}
