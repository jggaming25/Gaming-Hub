namespace GamingHub.App.Services;

/// <summary>Writes unhandled exceptions to %AppData%\GamingHub\crash.log.</summary>
public static class CrashLog
{
    public static string Path { get; } =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GamingHub", "crash.log");

    public static void Write(string source, Exception? ex)
    {
        if (ex is null) return;
        try
        {
            File.AppendAllText(Path, $"[{DateTime.Now:O}] [{source}]\n{ex}\n\n");
        }
        catch
        {
            // logging must never crash the app
        }
    }
}