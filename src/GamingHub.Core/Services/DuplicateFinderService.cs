using System.Collections.Concurrent;
using System.Security.Cryptography;
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
/// Finds byte-identical duplicate files using a size pre-filter, a fast partial
/// hash and a final full SHA-256 confirmation (multi-stage, reliable).
/// </summary>
public sealed class DuplicateFinderService
{
    private static readonly string[] ExcludedDirNames =
    {
        "Windows", "System Volume Information", "$Recycle.Bin", "$WinREAgent",
        "ProgramData", "AppData", "node_modules", ".git", "Temp", "assembly", "WinSxS"
    };

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

        // Phase 2: index files by size (fast, uses cached metadata).
        var bySize = new Dictionary<long, List<string>>();
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

                if (!bySize.TryGetValue(info.Length, out var list))
                    bySize[info.Length] = list = new List<string>();
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

        var candidates = bySize.Where(kv => kv.Value.Count > 1).ToList();
        long candidateFiles = candidates.Sum(c => (long)c.Value.Count);
        long hashed = 0;
        var result = new List<DuplicateGroup>();

        // Phase 3: partial hash (parallel), then full hash (parallel) to confirm.
        foreach (var (size, files) in candidates)
        {
            ct.ThrowIfCancellationRequested();

            var partials = new ConcurrentDictionary<string, string?>();
            Parallel.ForEach(files, new ParallelOptions { CancellationToken = ct }, file =>
            {
                partials[file] = PartialHash(file, 131072);
                long done = Interlocked.Increment(ref hashed);
                if (done % 100 == 0)
                {
                    int pct = (int)Math.Min(95, 50 + done * 45 / Math.Max(1, candidateFiles));
                    progress?.Report(new DuplicateScanProgress { Phase = "Dateien werden verglichen", FilesInspected = done, TotalFiles = candidateFiles, Percent = pct });
                }
            });

            var partialGroups = partials
                .Where(kv => kv.Value is not null)
                .GroupBy(kv => kv.Value!)
                .Where(g => g.Count() > 1);

            foreach (var group in partialGroups)
            {
                ct.ThrowIfCancellationRequested();

                var fulls = new ConcurrentDictionary<string, string?>();
                Parallel.ForEach(group.Select(kv => kv.Key), new ParallelOptions { CancellationToken = ct }, file =>
                {
                    fulls[file] = FullHash(file);
                });

                foreach (var dup in fulls.Where(kv => kv.Value is not null).GroupBy(kv => kv.Value!).Where(g => g.Count() > 1))
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
                    if (g.Files.Count > 1)
                        result.Add(g);
                }
            }
        }

        progress?.Report(new DuplicateScanProgress { Phase = "Fertig", FilesInspected = inspected, GroupsFound = result.Count, Percent = 100 });
        return result.OrderByDescending(g => g.WastedBytes).ToList();
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

    private static string? PartialHash(string file, int bytes)
    {
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.SequentialScan);
            int toRead = (int)Math.Min(bytes, fs.Length);
            var buffer = new byte[toRead];
            int read = fs.Read(buffer, 0, toRead);
            return Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, read)));
        }
        catch
        {
            return null;
        }
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
