using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using GamingHub.Core.Models;

namespace GamingHub.Core.Services;

public sealed class DuplicateScanOptions
{
    public List<string> Roots { get; set; } = new();
    public long MinFileSizeBytes { get; set; } = 1024 * 1024;
    public bool ExcludeSystemDirectories { get; set; } = true;
}

public sealed class DuplicateScanProgress
{
    public string Phase { get; set; } = string.Empty;
    public long FilesInspected { get; set; }

    /// <summary>Gesamtzahl der Dateien in der aktuellen Phase (0 = unbekannt).
    /// Ermöglicht eine präzise Fortschritts-/Restzeitschätzung.</summary>
    public long TotalFiles { get; set; }

    public string? CurrentPath { get; set; }
    public int GroupsFound { get; set; }

    /// <summary>Best-effort completion 0-100, or -1 while unknown.</summary>
    public int Percent { get; set; } = -1;
}

/// <summary>
/// Findet doppelte Dateien bewusst konservativ:
///  – nur innerhalb desselben Verzeichnisses,
///  – nur bei identischer Dateigröße,
///  – nur bei gleichem Namen, wobei typische Kopie-Suffixe wie
///    „(1)“, „_1“, „ 2“, „- Kopie“ oder „ Copy“ ignoriert werden,
///  – und nur wenn der Inhalt per SHA-256 tatsächlich identisch ist.
/// Dadurch werden keine Dateien erfasst, die nur zufällig gleich groß sind
/// oder an anderer Stelle liegen.
/// </summary>
public sealed class DuplicateFinderService
{
    private static readonly string[] ExcludedDirNames =
    {
        "Windows", "System Volume Information", "$Recycle.Bin", "$WinREAgent",
        "ProgramData", "AppData", "node_modules", ".git", "Temp", "assembly", "WinSxS"
    };

    /// <summary>Erkennt Kopie-Zusätze am Ende eines Dateinamens, z. B.
    /// „Foto (1)“, „Foto_1“, „Foto 2“, „Foto - Kopie“ oder „Foto Copy (2)“.</summary>
    private static readonly Regex CopySuffixPattern = new(
        @"(?:\s*[\(\[]\s*(?:\d+|kopie|copy|duplikat|kopia)\s*[\)\]]" +
        @"|[\s_-]+(?:kopie|copy|duplikat|kopia)(?:[\s_-]*\d+)?" +
        @"|[\s_-]+\d+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public Task<List<DuplicateGroup>> FindDuplicatesAsync(
        DuplicateScanOptions options,
        IProgress<DuplicateScanProgress>? progress,
        CancellationToken ct)
    {
        return Task.Run(() => FindCore(options, progress, ct), ct);
    }

    private static List<DuplicateGroup> FindCore(
        DuplicateScanOptions options,
        IProgress<DuplicateScanProgress>? progress,
        CancellationToken ct)
    {
        // Phase 1: collect all candidate paths so we can report a real percentage.
        var allFiles = new List<string>();
        foreach (string root in options.Roots)
        {
            if (!Directory.Exists(root))
                continue;

            var enumOptions = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
            };

            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(root, "*", enumOptions); }
            catch { continue; }

            foreach (string file in files)
            {
                ct.ThrowIfCancellationRequested();
                allFiles.Add(file);
                if (allFiles.Count % 2000 == 0)
                    progress?.Report(new DuplicateScanProgress { Phase = "Dateien werden gesucht", FilesInspected = allFiles.Count, CurrentPath = file, Percent = -1 });
            }
        }

        long total = allFiles.Count;
        if (total == 0)
            return new List<DuplicateGroup>();

        // Phase 2: Nach Verzeichnis und Größe gruppieren. Duplikate zählen nur
        // innerhalb desselben Ordners – gleiche Dateien an anderer Stelle
        // (z. B. Backups) werden bewusst nicht als Duplikat betrachtet.
        var byDirectoryAndSize = new Dictionary<(string Directory, long Size), List<string>>();
        long inspected = 0;

        foreach (string file in allFiles)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(file);
                if (!info.Exists || info.Length < options.MinFileSizeBytes)
                    continue;
                if (options.ExcludeSystemDirectories && IsExcluded(file))
                    continue;

                string directory = Path.GetDirectoryName(file) ?? string.Empty;
                var key = (directory, info.Length);
                if (!byDirectoryAndSize.TryGetValue(key, out var list))
                    byDirectoryAndSize[key] = list = new List<string>();
                list.Add(file);
            }
            catch
            {
                // skip inaccessible file
            }

            inspected++;
            if (inspected % 500 == 0)
                progress?.Report(new DuplicateScanProgress { Phase = "Dateien werden indiziert", FilesInspected = inspected, TotalFiles = total, CurrentPath = file, Percent = (int)Math.Min(50, inspected * 50 / total) });
        }

        progress?.Report(new DuplicateScanProgress { Phase = "Dateien werden indiziert", FilesInspected = inspected, TotalFiles = total, Percent = 50 });

        // Phase 3: Innerhalb einer Verzeichnis-/Größen-Gruppe nur Dateien mit
        // demselben Basisnamen (ohne Kopie-Suffix) und derselben Endung prüfen.
        var candidates = new List<(long Size, string BaseName, List<string> Files)>();
        foreach (var ((_, size), files) in byDirectoryAndSize)
        {
            if (files.Count < 2)
                continue;

            foreach (var nameGroup in files.GroupBy(NameKey))
            {
                if (nameGroup.Count() < 2)
                    continue;

                string baseStem = NormalizeStem(Path.GetFileNameWithoutExtension(nameGroup.First()));
                candidates.Add((size, baseStem, nameGroup.ToList()));
            }
        }

        long candidateFiles = candidates.Sum(c => (long)c.Files.Count);
        long hashed = 0;
        var result = new List<DuplicateGroup>();

        // Phase 4: Vollständigen SHA-256-Hash bilden und identischen Inhalt bestätigen.
        foreach (var (size, baseName, files) in candidates)
        {
            ct.ThrowIfCancellationRequested();

            var fulls = new ConcurrentDictionary<string, string?>();
            Parallel.ForEach(files, new ParallelOptions { CancellationToken = ct }, file =>
            {
                fulls[file] = FullHash(file);
                long done = Interlocked.Increment(ref hashed);
                if (done % 50 == 0)
                {
                    int pct = (int)Math.Min(99, 50 + done * 49 / Math.Max(1, candidateFiles));
                    progress?.Report(new DuplicateScanProgress { Phase = "Dateien werden verglichen", FilesInspected = done, TotalFiles = candidateFiles, Percent = pct });
                }
            });

            foreach (var dup in fulls
                         .Where(kv => kv.Value is not null)
                         .GroupBy(kv => kv.Value!)
                         .Where(g => g.Count() > 1))
            {
                var g = new DuplicateGroup { Hash = dup.Key, FileSize = size };
                foreach (string f in dup.Select(kv => kv.Key))
                {
                    try
                    {
                        g.Files.Add(new DuplicateFile
                        {
                            Path = f,
                            Size = size,
                            LastWriteUtc = File.GetLastWriteTimeUtc(f)
                        });
                    }
                    catch { /* skip */ }
                }

                if (g.Files.Count <= 1)
                    continue;

                // Als Original gilt die Datei ohne Kopie-Suffix, sonst die älteste.
                g.OriginalPath = g.Files
                    .OrderBy(f => IsOriginalName(f, baseName) ? 0 : 1)
                    .ThenBy(f => f.FileName.Length)
                    .ThenBy(f => f.LastWriteUtc)
                    .First()
                    .Path;
                result.Add(g);
            }
        }

        progress?.Report(new DuplicateScanProgress { Phase = "Fertig", FilesInspected = inspected, GroupsFound = result.Count, Percent = 100 });
        return result.OrderByDescending(g => g.WastedBytes).ToList();
    }

    /// <summary>Gruppierungsschlüssel aus normalisiertem Basisnamen und Endung.</summary>
    private static string NameKey(string path)
    {
        string stem = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        return NormalizeStem(stem).ToLowerInvariant() + "|" + extension.ToLowerInvariant();
    }

    /// <summary>Entfernt typische Kopie-Zusätze wie „(1)“, „_2“, „ 3“,
    /// „- Kopie“ oder „ Copy“ vom Dateinamen (auch mehrfach hintereinander).</summary>
    private static string NormalizeStem(string stem)
    {
        string current = stem.Trim();
        for (int i = 0; i < 8; i++)
        {
            string next = CopySuffixPattern.Replace(current, string.Empty).TrimEnd(' ', '-', '_', '.');
            if (next.Length == 0 || string.Equals(next, current, StringComparison.Ordinal))
                return current;
            current = next;
        }
        return current;
    }

    private static bool IsOriginalName(DuplicateFile file, string baseName)
    {
        string stem = Path.GetFileNameWithoutExtension(file.Path).Trim();
        return string.Equals(stem, baseName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExcluded(string path)
    {
        foreach (string name in ExcludedDirNames)
        {
            if (path.Contains("\\" + name + "\\", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("\\" + name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string? FullHash(string file)
    {
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20, FileOptions.SequentialScan);
            return Convert.ToHexString(SHA256.HashData(fs));
        }
        catch
        {
            return null;
        }
    }
}
