using System.Text;

namespace GamingHub.Core.Services;

/// <summary>
/// Findet die Start-EXE eines installierten Spiels, auch wenn die Quelle
/// (z. B. Steam) keinen direkten Pfad liefert.
/// </summary>
public static class GameExecutableLocator
{
    private static readonly string[] IgnoredWords =
    {
        "the", "of", "and", "a", "an", "der", "die", "das", "und",
        "edition", "definitive", "remastered", "remake", "deluxe", "ultimate",
        "game", "goty", "complete", "collection", "hd", "vr"
    };

    private static readonly string[] IgnoredExeParts =
    {
        "unins", "uninstall", "setup", "install", "crash", "report", "dxsetup",
        "vcredist", "dotnet", "redist", "launcher_installer", "update", "patch",
        "helper", "touchup", "cleanup", "diagnostic"
    };

    private static readonly string[] CandidateSubFolders =
    {
        "bin", "binaries", "win64", "win32", "x64", "x86", "game", "retail",
        "release", "system", "engine", "client"
    };

    public static string? Find(string? installPath, string? gameName)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
            return null;

        string installDirName = Path.GetFileName(installPath.TrimEnd('\\', '/'));
        var tokens = BuildTokens(gameName, installDirName);

        try
        {
            var candidates = EnumerateExecutables(installPath, SearchOption.TopDirectoryOnly)
                .Where(IsPlausible)
                .Select(path => (Path: path, Score: Score(path, tokens, installDirName)))
                .OrderByDescending(c => c.Score)
                .ThenByDescending(c => SafeLength(c.Path))
                .ToList();

            if (candidates.Count > 0 && candidates[0].Score > 0)
                return candidates[0].Path;

            // In typischen Unterordnern (bin, binaries, win64 …) suchen.
            foreach (string sub in CandidateSubFolders)
            {
                string dir = Path.Combine(installPath, sub);
                if (!Directory.Exists(dir))
                    continue;

                var nested = EnumerateExecutables(dir, SearchOption.AllDirectories)
                    .Where(IsPlausible)
                    .Select(path => (Path: path, Score: Score(path, tokens, installDirName)))
                    .OrderByDescending(c => c.Score)
                    .ThenByDescending(c => SafeLength(c.Path))
                    .FirstOrDefault();

                if (nested.Path is not null && nested.Score > 0)
                    return nested.Path;
            }

            // Letzter Fall: größte plausible EXE im Hauptordner.
            return candidates.FirstOrDefault().Path;
        }
        catch
        {
            return null;
        }
    }

    public static string? FindRecursive(string? installPath, string? gameName, int maxDepth = 3)
    {
        string? direct = Find(installPath, gameName);
        if (direct is not null || string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
            return direct;

        var tokens = BuildTokens(gameName, Path.GetFileName(installPath.TrimEnd('\\', '/')));
        try
        {
            foreach (string dir in EnumerateDirectoriesBreadthFirst(installPath, maxDepth))
            {
                var match = EnumerateExecutables(dir, SearchOption.TopDirectoryOnly)
                    .Where(IsPlausible)
                    .Select(path => (Path: path, Score: Score(path, tokens, string.Empty)))
                    .Where(c => c.Score >= 40)
                    .OrderByDescending(c => c.Score)
                    .FirstOrDefault();

                if (match.Path is not null)
                    return match.Path;
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    /// <summary>Berechnet die Ordnergröße (rekursiv, ohne Reparse-Punkte).</summary>
    public static long GetDirectorySize(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return 0;

        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                ReturnSpecialDirectories = false
            };
            long total = 0;
            foreach (string file in Directory.EnumerateFiles(directory, "*", options))
            {
                try { total += new FileInfo(file).Length; }
                catch { }
            }
            return total;
        }
        catch
        {
            return 0;
        }
    }

    private static IEnumerable<string> EnumerateExecutables(string dir, SearchOption option)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*.exe", option);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> EnumerateDirectoriesBreadthFirst(string root, int maxDepth)
    {
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((root, 0));
        while (queue.Count > 0)
        {
            var (path, depth) = queue.Dequeue();
            yield return path;
            if (depth >= maxDepth)
                continue;
            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(path);
            }
            catch
            {
                continue;
            }
            foreach (string child in children)
            {
                string name = Path.GetFileName(child);
                if (name.Equals("redist", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("support", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("uninstall", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("docs", StringComparison.OrdinalIgnoreCase))
                    continue;
                queue.Enqueue((child, depth + 1));
            }
        }
    }

    private static bool IsPlausible(string path)
    {
        string file = Path.GetFileName(path);
        string lower = file.ToLowerInvariant();
        if (IgnoredExeParts.Any(p => lower.Contains(p)))
            return false;
        return SafeLength(path) >= 16 * 1024;
    }

    private static int Score(string path, List<string> tokens, string installDirName)
    {
        string file = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        string compactFile = Compact(file);
        string compactDir = Compact(installDirName);
        int score = 0;

        if (compactDir.Length > 2 && compactFile == compactDir)
            score += 90;
        else if (compactDir.Length > 2 &&
                 (compactFile.Contains(compactDir) || compactDir.Contains(compactFile)))
            score += 45;

        if (tokens.Count > 0)
        {
            int matches = tokens.Count(t => compactFile.Contains(t));
            score += matches * 25;
            if (matches == tokens.Count)
                score += 40;
        }

        if (compactFile.Contains("launcher"))
            score -= 30;
        if (compactFile.Contains("client"))
            score += 5;

        string full = path.ToLowerInvariant();
        if (full.Contains("\\redist\\") || full.Contains("\\support\\") ||
            full.Contains("\\engine\\") || full.Contains("\\tools\\"))
            score -= 25;

        return score;
    }

    private static List<string> BuildTokens(string? gameName, string? installDirName)
    {
        var tokens = new List<string>();
        foreach (string? raw in new string?[] { gameName, installDirName })
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            string cleaned = raw;
            foreach (string suffix in new[]
                     {
                         "®", "™", "©", " - ", ": ", ":"
                     })
                cleaned = cleaned.Replace(suffix, " ", StringComparison.OrdinalIgnoreCase);

            foreach (string word in cleaned.Split(
                         new[] { ' ', '-', '_', '.', ',', '(', ')', '[', ']', '\'', '’' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                string token = Compact(word);
                if (token.Length < 3 || IgnoredWords.Contains(token) || token.All(char.IsDigit))
                    continue;
                if (!tokens.Contains(token))
                    tokens.Add(token);
            }
        }
        return tokens;
    }

    private static string Compact(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }
}
