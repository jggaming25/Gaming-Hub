using System.Diagnostics;
using GamingHub.Core.Models;

namespace GamingHub.Core.Services;

/// <summary>
/// Aggregates all game scanners, merges with persisted playtime, detects running
/// instances and provides launch / stop / restart control.
/// </summary>
public sealed class GameLibraryService
{
    private readonly SteamScanner _steam = new();
    private readonly EpicScanner _epic = new();
    private readonly RegistryAppScanner _registry = new();
    private readonly PlaytimeStore _playtime;

    private DateTime _lastTickUtc;
    private DateTime _lastSaveUtc = DateTime.UtcNow;

    public List<GameInfo> Games { get; private set; } = new();

    public GameLibraryService(string dataDirectory)
    {
        _playtime = new PlaytimeStore(dataDirectory);
        _lastTickUtc = DateTime.UtcNow;
    }

    /// <summary>Runs all scanners and merges playtime data. Should run off the UI thread.</summary>
    public Task<List<GameInfo>> RefreshAsync()
    {
        return Task.Run(() =>
        {
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

            foreach (var game in merged)
            {
                var rec = _playtime.Get(game.Id);
                game.TotalPlaytime = TimeSpan.FromSeconds(rec.TotalSeconds);
                game.LastPlayedUtc = rec.LastPlayedUtc;
            }

            Games = merged;
            UpdateRunningStates();
            return merged;
        });
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
