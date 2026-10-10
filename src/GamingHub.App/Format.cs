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
}
