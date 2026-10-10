using System.Text.RegularExpressions;
using GamingHub.Core.Models;
using Microsoft.Win32;

namespace GamingHub.Core.Services;

/// <summary>Detects installed Steam games by reading library folders and app manifests.</summary>
public sealed class SteamScanner
{
    public IEnumerable<GameInfo> Scan()
    {
        var results = new List<GameInfo>();
        string? steamPath = GetSteamInstallPath();
        if (steamPath is null)
            return results;

        var libraries = GetLibraryFolders(steamPath);
        foreach (string library in libraries)
        {
            string steamApps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamApps))
                continue;

            foreach (string acf in SafeEnumerate(steamApps, "appmanifest_*.acf"))
            {
                try
                {
                    var kv = ParseVdf(File.ReadAllText(acf));
                    if (!kv.TryGetValue("appid", out string? appId) ||
                        !kv.TryGetValue("name", out string? name))
                        continue;

                    kv.TryGetValue("installdir", out string? installDir);
                    long size = 0;
                    if (kv.TryGetValue("SizeOnDisk", out string? sizeStr))
                        long.TryParse(sizeStr, out size);

                    string? fullPath = installDir is not null
                        ? Path.Combine(steamApps, "common", installDir)
                        : null;

                    results.Add(new GameInfo
                    {
                        Id = "steam:" + appId,
                        Name = name,
                        Source = GameSource.Steam,
                        InstallPath = fullPath,
                        LaunchUri = $"steam://rungameid/{appId}",
                        ProcessName = installDir is not null ? SanitizeProcessName(installDir) : null,
                        SizeOnDisk = size
                    });
                }
                catch
                {
                    // skip broken manifest
                }
            }
        }

        return results;
    }

    private static string? SanitizeProcessName(string installDir)
    {
        // Steam install dirs often match the exe name closely, but not reliably.
        return null;
    }

    private static string? GetSteamInstallPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            string? path = key?.GetValue("SteamPath") as string
                           ?? key?.GetValue("InstallPath") as string;
            if (!string.IsNullOrWhiteSpace(path))
                return path.Replace('/', '\\');

            using var key32 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
            path = key32?.GetValue("InstallPath") as string;
            return string.IsNullOrWhiteSpace(path) ? null : path.Replace('/', '\\');
        }
        catch
        {
            return null;
        }
    }

    private static List<string> GetLibraryFolders(string steamPath)
    {
        var libraries = new List<string> { steamPath };
        try
        {
            string vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\""))
                {
                    string p = m.Groups[1].Value.Replace("\\\\", "\\");
                    if (Directory.Exists(p) && !libraries.Contains(p, StringComparer.OrdinalIgnoreCase))
                        libraries.Add(p);
                }
            }
        }
        catch
        {
            // ignore
        }

        return libraries;
    }

    private static IEnumerable<string> SafeEnumerate(string dir, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static Dictionary<string, string> ParseVdf(string content)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(content, "\"([^\"]+)\"\\s+\"([^\"]*)\""))
        {
            string key = m.Groups[1].Value;
            string value = m.Groups[2].Value;
            // Only keep the first occurrence for a given key at the manifest level.
            dict.TryAdd(key, value);
        }
        return dict;
    }
}
