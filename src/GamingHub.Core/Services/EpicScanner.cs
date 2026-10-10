using System.Text.Json;
using GamingHub.Core.Models;

namespace GamingHub.Core.Services;

/// <summary>Detects installed Epic Games titles via Epic manifest files.</summary>
public sealed class EpicScanner
{
    public IEnumerable<GameInfo> Scan()
    {
        var results = new List<GameInfo>();
        string manifests = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");

        if (!Directory.Exists(manifests))
            return results;

        foreach (string file in SafeEnumerate(manifests, "*.item"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;

                string name = GetString(root, "DisplayName");
                string location = GetString(root, "InstallLocation");
                string executable = GetString(root, "LaunchExecutable");
                string appName = GetString(root, "AppName");
                long size = GetLong(root, "InstallSize");

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(location))
                    continue;

                string? exePath = string.IsNullOrWhiteSpace(executable)
                    ? null
                    : Path.Combine(location, executable.Replace('/', '\\'));

                results.Add(new GameInfo
                {
                    Id = "epic:" + (string.IsNullOrWhiteSpace(appName) ? name : appName),
                    Name = name,
                    Source = GameSource.Epic,
                    InstallPath = location,
                    ExecutablePath = exePath,
                    LaunchUri = string.IsNullOrWhiteSpace(appName)
                        ? null
                        : $"com.epicgames.launcher://apps/{appName}?action=launch&silent=true",
                    ProcessName = exePath is not null
                        ? Path.GetFileNameWithoutExtension(exePath)
                        : null,
                    SizeOnDisk = size
                });
            }
            catch
            {
                // skip broken manifest
            }
        }

        return results;
    }

    private static string GetString(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty
            : string.Empty;

    private static long GetLong(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v))
            return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt64(out long n) => n,
            JsonValueKind.String when long.TryParse(v.GetString(), out long n) => n,
            _ => 0
        };
    }

    private static IEnumerable<string> SafeEnumerate(string dir, string pattern)
    {
        try { return Directory.EnumerateFiles(dir, pattern); }
        catch { return Array.Empty<string>(); }
    }
}
