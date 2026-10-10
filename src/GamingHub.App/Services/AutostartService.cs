using Microsoft.Win32;

namespace GamingHub.App.Services;

/// <summary>
/// Regelt das automatische Starten von Gaming Hub beim Anmelden in Windows
/// (Registry-Eintrag unter HKCU). Beim Systemstart wird die App versteckt in
/// den System-Tray gelegt, damit sie im Hintergrund weiterlaufen kann.
/// </summary>
public static class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GamingHub";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string;
        }
        catch (Exception ex)
        {
            LogService.Warn("Autostart lesen fehlgeschlagen.", ex);
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null) return;

            if (enabled)
            {
                string exe = Environment.ProcessPath ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(exe))
                    key.SetValue(ValueName, $"\"{exe}\" --hidden");
                LogService.Info($"Autostart aktiviert ({exe}).");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                LogService.Info("Autostart deaktiviert.");
            }
        }
        catch (Exception ex)
        {
            LogService.Warn("Autostart aktivieren/deaktivieren fehlgeschlagen.", ex);
        }
    }
}