using GamingHub.Core.Models;
using Microsoft.Win32;

namespace GamingHub.Core.Services;

/// <summary>
/// Enumerates launchable applications from the Windows uninstall registry keys.
/// This surfaces installed games/apps that are not managed by Steam or Epic.
/// </summary>
public sealed class RegistryAppScanner
{
    private static readonly string[] UninstallPaths =
    {
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    };

    public IEnumerable<GameInfo> Scan()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<GameInfo>();

        foreach (var (hive, path) in EnumerateKeys())
        {
            using var root = hive.OpenSubKey(path);
            if (root is null)
                continue;

            foreach (string subName in root.GetSubKeyNames())
            {
                try
                {
                    using var sub = root.OpenSubKey(subName);
                    if (sub is null)
                        continue;

                    string? displayName = sub.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(displayName))
                        continue;

                    // Skip updates/patches and non-app entries.
                    if (displayName.StartsWith("Update for", StringComparison.OrdinalIgnoreCase) ||
                        displayName.StartsWith("Security Update", StringComparison.OrdinalIgnoreCase) ||
                        displayName.StartsWith("Hotfix", StringComparison.OrdinalIgnoreCase) ||
                        displayName.StartsWith("KB", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (sub.GetValue("SystemComponent") is int sc && sc == 1)
                        continue;

                    string? rawInstall = sub.GetValue("InstallLocation") as string;
                    string? installLocation = CleanPath(rawInstall);
                    string? icon = sub.GetValue("DisplayIcon") as string;
                    string? exe = ResolveExecutable(icon, installLocation);
                    if (string.IsNullOrWhiteSpace(exe) && !string.IsNullOrWhiteSpace(installLocation))
                        exe = GameExecutableLocator.Find(installLocation, displayName) ??
                              GameExecutableLocator.FindRecursive(installLocation, displayName);
                    if (string.IsNullOrWhiteSpace(installLocation) && !string.IsNullOrWhiteSpace(exe))
                        installLocation = Path.GetDirectoryName(exe);

                    if (exe is null && string.IsNullOrWhiteSpace(installLocation))
                        continue;
                    if (!seen.Add(displayName))
                        continue;

                    results.Add(new GameInfo
                    {
                        Id = "reg:" + subName,
                        Name = displayName,
                        Source = GameSource.Registry,
                        InstallPath = installLocation,
                        ExecutablePath = exe,
                        ProcessName = exe is not null ? Path.GetFileNameWithoutExtension(exe) : null
                    });
                }
                catch
                {
                    // skip
                }
            }
        }

        return results;
    }

    private static IEnumerable<(RegistryKey Hive, string Path)> EnumerateKeys()
    {
        foreach (string p in UninstallPaths)
        {
            yield return (Registry.LocalMachine, p);
            yield return (Registry.CurrentUser, p);
        }
    }

    private static string? ResolveExecutable(string? icon, string? installLocation)
    {
        if (!string.IsNullOrWhiteSpace(icon))
        {
            // DisplayIcon may look like: "C:\path\app.exe,0" or "C:\path\app.exe,-101"
            string candidate = icon.Split(',')[0].Trim().Trim('"');
            if (candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(candidate))
                return candidate;
        }

        if (!string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation))
        {
            try
            {
                var exe = Directory.EnumerateFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly)
                    .Where(f => !Path.GetFileName(f).StartsWith("unins", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => new FileInfo(f).Length)
                    .FirstOrDefault();
                if (exe is not null)
                    return exe;
            }
            catch
            {
                // ignore access errors
            }
        }

        return null;
    }

    private static string? CleanPath(string? path)
        => string.IsNullOrWhiteSpace(path) ? null : path.Trim().Trim('"');
}
