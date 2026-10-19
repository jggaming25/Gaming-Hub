namespace GamingHub.App;

/// <summary>Small formatting helpers used across the UI.</summary>
public static class Format
{
    public static string Bytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
        double value = bytes;
        int i = 0;
        while (value >= 1024 && i < units.Length - 1)
        {
            value /= 1024;
            i++;
        }
        return $"{value:0.##} {units[i]}";
    }

    public static string Playtime(TimeSpan span)
    {
        if (span.TotalMinutes < 1)
            return "—";
        if (span.TotalHours < 1)
            return $"{(int)span.TotalMinutes} Min";
        if (span.TotalDays < 1)
            return $"{(int)span.TotalHours} h {span.Minutes} Min";
        return $"{(int)span.TotalDays} T {span.Hours} h";
    }

    public static string Date(DateTime? utc)
        => utc is null ? "—" : utc.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm");

    public static string Duration(TimeSpan t)
    {
        if (t.TotalDays >= 1) return $"{(int)t.TotalDays} T {t.Hours} Std";
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours} Std {t.Minutes} Min";
        if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes} Min {t.Seconds} s";
        return $"{(int)t.TotalSeconds} s";
    }

    public static string BytesPerSec(double bytesPerSec)
        => Bytes((long)Math.Max(0, bytesPerSec)) + "/s";

    /// <summary>Elapsed time as e.g. "5min, 25sek" (as requested for scan timing).</summary>
    public static string Elapsed(TimeSpan t)
    {
        if (t.TotalHours >= 1)
            return $"{(int)t.TotalHours}h, {t.Minutes}min, {t.Seconds}sek";
        if (t.TotalMinutes >= 1)
            return $"{(int)t.TotalMinutes}min, {t.Seconds}sek";
        return $"{t.Seconds}sek";
    }

    /// <summary>Zerlegt die Zeit seit <paramref name="start"/> in Jahre, Monate,
    /// Wochen, Tage, Stunden, Minuten und Sekunden.</summary>
    public static (int Years, int Months, int Weeks, int Days, int Hours, int Minutes, int Seconds)
        AgeParts(DateTimeOffset start, DateTimeOffset? now = null)
    {
        var end = now ?? DateTimeOffset.Now;
        if (end < start) end = start;

        int years = end.Year - start.Year;
        var cursor = start.AddYears(Math.Max(0, years));
        if (cursor > end)
        {
            years--;
            cursor = start.AddYears(Math.Max(0, years));
        }

        int months = 0;
        while (months < 11 && cursor.AddMonths(1) <= end)
        {
            cursor = cursor.AddMonths(1);
            months++;
        }

        var rest = end - cursor;
        return (years, months, rest.Days / 7, rest.Days % 7, rest.Hours, rest.Minutes, rest.Seconds);
    }

    /// <summary>Laufzeit im Format Jahre:Monate:Wochen:Tage:Stunden:Minuten:Sekunden.</summary>
    public static string Age(DateTimeOffset start, DateTimeOffset? now = null)
    {
        var p = AgeParts(start, now);
        return $"{p.Years}:{p.Months}:{p.Weeks}:{p.Days}:{p.Hours:00}:{p.Minutes:00}:{p.Seconds:00}";
    }

    /// <summary>Ausgeschriebene Laufzeit (Jahre, Monate, Wochen, …).</summary>
    public static string AgeLong(DateTimeOffset start, DateTimeOffset? now = null)
    {
        var p = AgeParts(start, now);
        return $"{p.Years} Jahre, {p.Months} Monate, {p.Weeks} Wochen, {p.Days} Tage, " +
               $"{p.Hours} Stunden, {p.Minutes} Minuten, {p.Seconds} Sekunden";
    }
}
