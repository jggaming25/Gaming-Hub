using System.Diagnostics;
using System.Reflection;

namespace GamingHub.App.Services;

/// <summary>
/// Fordert über die Windows-UAC eine einfache Admin-Bestätigung an. Der
/// erhöhte Helfer beendet sich sofort wieder; die eigentliche Freigabe wird
/// anschließend von der normal laufenden App gesetzt.
/// </summary>
public static class ScreenTimeElevation
{
    public static async Task<bool> RequestAdminApprovalAsync()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
                return false;

            var startInfo = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            if (string.Equals(Path.GetFileNameWithoutExtension(exe), "dotnet", StringComparison.OrdinalIgnoreCase))
            {
                var assembly = Assembly.GetEntryAssembly()?.Location;
                if (string.IsNullOrWhiteSpace(assembly))
                    return false;
                startInfo.Arguments = $"\"{assembly}\" --screen-time-approve";
            }
            else
            {
                startInfo.Arguments = "--screen-time-approve";
            }

            using var process = Process.Start(startInfo);
            if (process is null) return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // UAC-Fenster wurde abgebrochen.
            return false;
        }
        catch
        {
            return false;
        }
    }
}
