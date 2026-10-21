using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GamingHub.Core.Models;

namespace GamingHub.Core.Services;

/// <summary>
/// Aggregates all game scanners, merges with persisted playtime, detects running
/// instances and provides launch / stop / restart control.
///
/// Das teure Scannen der Bibliotheken wird in einem lokalen Cache
/// (games-cache.json) gespeichert. Beim nächsten Programmstart werden nur die
/// „Markierungen“ der installierten Bibliotheken verglichen (Steam-/Epic-
/// Manifeste und Registry-Uninstall-Einträge); hat sich nichts geändert, wird
/// einfach der Cache geladen und nur die Änderungen (neue/entfernte Titel)
/// übernommen – ein vollständiger Rescan ist nur nach echten Änderungen oder
/// per „Vollständiger Scan“-Knopf nötig.
/// </summary>
public sealed class GameLibraryService
{
    private readonly SteamScanner _steam = new();
    private readonly EpicScanner _epic = new();
    private readonly RegistryAppScanner _registry = new();
    private readonly PlaytimeStore _playtime;
    private readonly WebResearchService _research = new();
    private readonly string _cachePath;

    private DateTime _lastTickUtc;
    private DateTime _lastSaveUtc = DateTime.UtcNow;
    private string _markers = string.Empty;

    public List<GameInfo> Games { get; private set; } = new();

    public GameLibraryService(string dataDirectory)
    {
        _playtime = new PlaytimeStore(dataDirectory);
        _cachePath = Path.Combine(dataDirectory, "games-cache.json");
        _iconCacheDir = Path.Combine(dataDirectory, "game-icons");
        Directory.CreateDirectory(_iconCacheDir);
        _lastTickUtc = DateTime.UtcNow;
    }

    private readonly string _iconCacheDir;

    /// <summary>Gibt an, ob bereits ein Scan-Cache aus einem früheren Lauf vorliegt.</summary>
    public bool HasCache => File.Exists(_cachePath);

    /// <summary>Runs all scanners (or the cache when nothing changed) and merges
    /// playtime data. Should run off the UI thread.</summary>
    public Task<List<GameInfo>> RefreshAsync(bool forceRescan = false)
    {
        return Task.Run(() =>
        {
            string markers = ComputeMarkers();

            // Nur Änderungen anwenden: Cache laden, wenn sich nichts geändert hat.
            if (!forceRescan && TryLoadCache(markers, out var cached))
            {
                _markers = markers;
                bool enriched = false;
                foreach (var game in cached)
                {
                    // EXE/Ordner/Größe für ältere Cache-Einträge nachrüsten.
                    enriched |= EnrichGame(game);
                    // Icons ggf. auf höhere Auflösung nachrüsten (alte 32×32-Caches).
                    ExtractIconForGame(game);
                    MergePlaytime(game);
                }
                if (enriched)
                    SaveCache(cached, markers);
                Games = cached;
                UpdateRunningStates();
                return cached;
            }

            var all = new List<GameInfo>();
            SafeAdd(all, _steam.Scan);
            SafeAdd(all, _epic.Scan);
            SafeAdd(all, _registry.Scan);

            // Merge duplicates by name where multiple sources report the same title.
            var merged = all
                .GroupBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.SizeOnDisk).First())
                .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Extract icons for games with executable paths
            foreach (var game in merged)
            {
                game.Category = GameClassifier.Classify(game.Name, game.Source);
                EnrichGame(game);
                ExtractIconForGame(game);
                MergePlaytime(game);
            }

            Games = merged;
            _markers = markers;
            SaveCache(merged, markers);
            UpdateRunningStates();
            return merged;
        });
    }

    /// <summary>
    /// Ordnet „Sonstige“-Einträge im Hintergrund per Web-Recherche ein. Die
    /// Abfragen laufen mit begrenzter Parallelität; das Ergebnis wird im Cache
    /// gespeichert, damit nicht bei jedem Start erneut gesucht wird.
    /// </summary>
    public async Task<int> ResearchUnknownAsync(CancellationToken ct = default)
    {
        var targets = Games
            .Where(g => g.Category == GameCategory.Other && !g.CategoryResearched)
            .ToList();
        if (targets.Count == 0)
            return 0;

        int changed = 0;
        using var limit = new SemaphoreSlim(3);
        var tasks = targets.Select(async game =>
        {
            await limit.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var category = await _research.ResearchCategoryAsync(game.Name, ct).ConfigureAwait(false);
                game.CategoryResearched = true;
                if (category is { } value && value != GameCategory.Other)
                {
                    game.Category = value;
                    Interlocked.Increment(ref changed);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Einzelne Fehler dürfen die übrigen Einordnungen nicht stoppen.
            }
            finally
            {
                limit.Release();
            }
        });

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Beim Beenden der App ist ein Abbruch in Ordnung.
        }

        if (targets.Count > 0)
            SaveCache(Games, _markers);

        return changed;
    }

    private void MergePlaytime(GameInfo game)
    {
        // Import already-existing playtime (e.g. Steam) as baseline so it counts.
        if (game.TotalPlaytime.TotalSeconds > 0)
            _playtime.SeedBaseline(game.Id, game.TotalPlaytime.TotalSeconds, game.LastPlayedUtc);

        var rec = _playtime.Get(game.Id);
        game.TotalPlaytime = TimeSpan.FromSeconds(rec.TotalSeconds);
        game.LastPlayedUtc = rec.LastPlayedUtc;
    }

    /// <summary>Ergänzt fehlende EXE-, Ordner- und Größenangaben. Gibt true zurück,
    /// wenn sich der Datensatz geändert hat (dann muss der Cache neu geschrieben werden).</summary>
    private static bool EnrichGame(GameInfo game)
    {
        bool changed = false;

        if (string.IsNullOrWhiteSpace(game.ExecutablePath) &&
            !string.IsNullOrWhiteSpace(game.InstallPath) &&
            Directory.Exists(game.InstallPath))
        {
            string? exe = GameExecutableLocator.Find(game.InstallPath, game.Name);
            if (!string.IsNullOrWhiteSpace(exe))
            {
                game.ExecutablePath = exe;
                game.ProcessName ??= Path.GetFileNameWithoutExtension(exe);
                changed = true;
            }
        }

        if (string.IsNullOrWhiteSpace(game.InstallPath) &&
            !string.IsNullOrWhiteSpace(game.ExecutablePath))
        {
            string? dir = Path.GetDirectoryName(game.ExecutablePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                game.InstallPath = dir;
                changed = true;
            }
        }

        if (game.SizeOnDisk <= 0 &&
            !string.IsNullOrWhiteSpace(game.InstallPath) &&
            Directory.Exists(game.InstallPath) &&
            !IsSharedSystemFolder(game.InstallPath))
        {
            long size = GameExecutableLocator.GetDirectorySize(game.InstallPath);
            if (size > 0)
            {
                game.SizeOnDisk = size;
                changed = true;
            }
        }

        return changed;
    }

    private static bool IsSharedSystemFolder(string path)
    {
        string trimmed = path.TrimEnd('\\', '/');
        if (trimmed.Length <= 3)
            return true;
        string name = Path.GetFileName(trimmed);
        return name.Equals("Program Files", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Program Files (x86)", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Windows", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("steamapps", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("common", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Steam", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Extracts the associated icon from the game's executable and saves it as PNG.</summary>
    private void ExtractIconForGame(GameInfo game)
    {
        try
        {
            string? exe = game.ExecutablePath;
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                return;

            string cacheFile = Path.Combine(_iconCacheDir, $"{game.Id}.png");

            // Vorhandenes, ausreichend hochauflösendes Icon wiederverwenden.
            if (AppIconExtractor.IsHighResolution(cacheFile))
            {
                game.IconPath = cacheFile;
                game.ThumbnailPath = cacheFile;
                return;
            }

            if (AppIconExtractor.TryExtract(exe, cacheFile))
            {
                game.IconPath = cacheFile;
                game.ThumbnailPath = cacheFile;
            }
        }
        catch
        {
            // ignore icon extraction failures
        }
    }

    private void SaveCache(List<GameInfo> games, string markers)
    {
        try
        {
            var payload = new GameCacheFile { Markers = markers, Games = games };
            File.WriteAllText(_cachePath,
                JsonSerializer.Serialize(payload, CacheOptions),
                Encoding.UTF8);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("games-cache save failed: " + ex.Message);
        }
    }

    private bool TryLoadCache(string markers, out List<GameInfo> games)
    {
        games = new List<GameInfo>();
        try
        {
            if (!File.Exists(_cachePath)) return false;
            var payload = JsonSerializer.Deserialize<GameCacheFile>(File.ReadAllText(_cachePath), CacheOptions);
            if (payload?.Games is null) return false;
            if (!string.Equals(payload.Markers, markers, StringComparison.Ordinal)) return false;
            games = payload.Games;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static readonly JsonSerializerOptions CacheOptions = new() { WriteIndented = true };

    /// <summary>Berechnet schnell änderbare „Fingerabdrücke“ der installierten
    /// Bibliotheken, damit ein vollständiger Rescan nur bei echten Änderungen läuft.</summary>
    private static string ComputeMarkers()
    {
        var markers = new List<string>();
        try { markers.AddRange(FindSteamManifests()); } catch { }
        try { markers.AddRange(FindEpicManifests()); } catch { }
        try { markers.AddRange(FindRegistryMarkers()); } catch { }
        markers.Sort(StringComparer.OrdinalIgnoreCase);

        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join('\n', markers))));
    }

    private static IEnumerable<string> FindSteamManifests()
    {
        var steamapps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddFromKey(Microsoft.Win32.RegistryKey? key)
        {
            if (key?.GetValue("SteamPath") is string p && !string.IsNullOrWhiteSpace(p))
                steamapps.Add(Path.Combine(p.Trim(), "steamapps"));
        }

        AddFromKey(Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"));
        AddFromKey(Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"));

        foreach (var baseDir in steamapps.ToList())
        {
            string vdf = Path.Combine(baseDir, "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            foreach (var line in File.ReadAllLines(vdf))
            {
                var m = System.Text.RegularExpressions.Regex.Match(line,
                    "\"\\w+\"\\s+\"(.+?)\"\\s*(?://.*)?$");
                if (m.Success)
                {
                    string value = m.Groups[1].Value.Replace(@"\\", @"\").Trim('\\');
                    if (value.Length >= 2 && value[1] == ':')
                        steamapps.Add(Path.Combine(value, "steamapps"));
                }
            }
        }

        var files = new List<string>();
        foreach (var d in steamapps.Where(Directory.Exists))
            files.AddRange(Directory.GetFiles(d, "appmanifest_*.acf", SearchOption.TopDirectoryOnly));
        return files;
    }

    private static IEnumerable<string> FindEpicManifests()
    {
        var files = new List<string>();
        foreach (string? path in new[]
                 {
                     Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\EpicGames\EpicGamesLauncher")?.GetValue("AppDataPath") as string,
                     Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\EpicGames\EpicGamesLauncher")?.GetValue("AppDataPath") as string,
                 })
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            string manifests = Path.Combine(path.Trim(), "Manifests");
            if (Directory.Exists(manifests))
                files.AddRange(Directory.GetFiles(manifests, "*.item", SearchOption.TopDirectoryOnly));
        }
        return files;
    }

    private static IEnumerable<string> FindRegistryMarkers()
    {
        var markers = new List<string>();
        foreach (Microsoft.Win32.RegistryKey? key in new[]
                 {
                     Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                     Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
                     Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                     Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"),
                 })
        {
            using (key)
            {
                if (key is null) continue;
                foreach (var name in key.GetSubKeyNames())
                    markers.Add(name);
            }
        }
        return markers.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static void SafeAdd(List<GameInfo> target, Func<IEnumerable<GameInfo>> scanner)
    {
        try { target.AddRange(scanner()); }
        catch { /* ignore scanner failures */ }
    }

    /// <summary>Detects which games are currently running and accumulates playtime.</summary>
    public void UpdateRunningStates()
    {
        var processes = GetProcessSnapshot();
        DateTime now = DateTime.UtcNow;
        double delta = (now - _lastTickUtc).TotalSeconds;
        _lastTickUtc = now;

        foreach (var game in Games)
        {
            bool running = IsGameRunning(game, processes);
            game.IsRunning = running;

            if (running && delta is > 0 and < 300)
                _playtime.AddSeconds(game.Id, delta, now);

            var rec = _playtime.Get(game.Id);
            game.TotalPlaytime = TimeSpan.FromSeconds(rec.TotalSeconds);
            game.LastPlayedUtc = rec.LastPlayedUtc;
        }

        if ((now - _lastSaveUtc).TotalSeconds > 30)
        {
            _playtime.Save();
            _lastSaveUtc = now;
        }
    }

    public void SavePlaytime() => _playtime.Save();

    private static bool IsGameRunning(GameInfo game, List<(string Name, string? Path)> processes)
    {
        foreach (var (name, path) in processes)
        {
            if (!string.IsNullOrWhiteSpace(game.ProcessName) &&
                string.Equals(name, game.ProcessName, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrWhiteSpace(game.InstallPath) && path is not null &&
                path.StartsWith(game.InstallPath, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static List<(string Name, string? Path)> GetProcessSnapshot()
    {
        var list = new List<(string, string?)>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                string? path = null;
                try { path = p.MainModule?.FileName; } catch { /* access denied */ }
                list.Add((p.ProcessName, path));
            }
            catch
            {
                // process exited
            }
            finally
            {
                p.Dispose();
            }
        }
        return list;
    }

    public bool Launch(GameInfo game)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(game.LaunchUri))
            {
                Process.Start(new ProcessStartInfo(game.LaunchUri) { UseShellExecute = true });
                return true;
            }
            if (!string.IsNullOrWhiteSpace(game.ExecutablePath) && File.Exists(game.ExecutablePath))
            {
                Process.Start(new ProcessStartInfo(game.ExecutablePath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(game.ExecutablePath)
                });
                return true;
            }
        }
        catch
        {
            // ignore launch failure
        }
        return false;
    }

    public int Stop(GameInfo game)
    {
        int killed = 0;
        var processes = Process.GetProcesses();
        foreach (var p in processes)
        {
            try
            {
                string? path = null;
                try { path = p.MainModule?.FileName; } catch { }
                bool match = (!string.IsNullOrWhiteSpace(game.ProcessName) &&
                              string.Equals(p.ProcessName, game.ProcessName, StringComparison.OrdinalIgnoreCase))
                             || (!string.IsNullOrWhiteSpace(game.InstallPath) && path is not null &&
                                 path.StartsWith(game.InstallPath, StringComparison.OrdinalIgnoreCase));
                if (match)
                {
                    p.Kill(entireProcessTree: true);
                    killed++;
                }
            }
            catch
            {
                // ignore
            }
            finally
            {
                p.Dispose();
            }
        }
        return killed;
    }

    public async Task<bool> RestartAsync(GameInfo game)
    {
        Stop(game);
        await Task.Delay(2500);
        return Launch(game);
    }
}

/// <summary>Persisted cache of a previous library scan (avoids full rescans).</summary>
public sealed class GameCacheFile
{
    public string Markers { get; set; } = string.Empty;
    public List<GameInfo> Games { get; set; } = new();
}
