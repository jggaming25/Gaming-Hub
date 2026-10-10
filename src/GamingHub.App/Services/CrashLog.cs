namespace GamingHub.App.Services;

/// <summary>
/// Rotating, easy-to-find program log at %AppData%\GamingHub\GamingHub.log.
/// All info/warnings/errors and uncaught exceptions land here; bug reports
/// automatically attach a copy so issues can be diagnosed.
/// </summary>
public static class LogService
{
    private static readonly object Sync = new();

    public static string LogFolder { get; } =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GamingHub");

    public static string LogFile { get; } =
        System.IO.Path.Combine(LogFolder, "GamingHub.log");

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);

    public static void Error(string source, Exception? ex = null) => Write("ERROR", source, ex);

    /// <summary>Text preview for bug reports (newest lines, bounded size).</summary>
    public static string Dump(int maxChars = 90_000)
    {
        try
        {
            lock (Sync)
            {
                if (!File.Exists(LogFile)) return "Kein Log vorhanden.";
                var text = File.ReadAllText(LogFile);
                if (text.Length <= maxChars) return text;
                return text[^maxChars..]
                    .TrimStart('\n', '\r');
            }
        }
        catch (Exception ex)
        {
            return "Log konnte nicht gelesen werden: " + ex.Message;
        }
    }

    public static void Clear()
    {
        try
        {
            lock (Sync)
                if (File.Exists(LogFile))
                    File.WriteAllText(LogFile, string.Empty);
        }
        catch
        {
            // ignore
        }
    }

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (Sync)
            {
                try
                {
                    if (!Directory.Exists(LogFolder))
                        Directory.CreateDirectory(LogFolder);

                    if (File.Exists(LogFile) && new FileInfo(LogFile).Length > 1_500_000)
                    {
                        // Keep only the newest ~1 MB so the file never grows unbounded.
                        var all = File.ReadAllText(LogFile);
                        File.WriteAllText(LogFile, all[^1_000_000..].TrimStart('\n', '\r') + "\n");
                    }
                }
                catch
                {
                    // ignore rotation problems
                }

                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
                if (ex is not null)
                    line += Environment.NewLine + ex;
                File.AppendAllText(LogFile, line + Environment.NewLine);
            }
        }
        catch
        {
            // logging must never crash the app
        }
    }
}

/// <summary>Compatibility alias kept for existing call sites.</summary>
public static class CrashLog
{
    public static string Path => LogService.LogFile;

    public static void Write(string source, Exception? ex) => LogService.Error(source, ex);
}