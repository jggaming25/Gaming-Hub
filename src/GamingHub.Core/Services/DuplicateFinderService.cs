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
    public string? CurrentPath { get; set; }
    public int GroupsFound { get; set; }
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
        var bySize = new Dictionary<long, List<string>>();
        long inspected = 0;

        // Phase 1: index files by size
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

                    inspected++;
                    if (inspected % 200 == 0)
                        progress?.Report(new DuplicateScanProgress { Phase = "Dateien werden indiziert", FilesInspected = inspected, CurrentPath = file });
                }
                catch
                {
                    // skip inaccessible file
                }
            }
        }

        var candidates = bySize.Where(kv => kv.Value.Count > 1).ToList();
        var result = new List<DuplicateGroup>();

        // Phase 2: partial hash, then full hash
        foreach (var (size, files) in candidates)
        {
            ct.ThrowIfCancellationRequested();

            var byPartial = new Dictionary<string, List<string>>();
            foreach (string file in files)
            {
                ct.ThrowIfCancellationRequested();
                string? partial = PartialHash(file, 131072);
                if (partial is null) continue;
                if (!byPartial.TryGetValue(partial, out var l))
                    byPartial[partial] = l = new List<string>();
                l.Add(file);
            }

            foreach (var group in byPartial.Where(kv => kv.Value.Count > 1))
            {
                ct.ThrowIfCancellationRequested();

                var byFull = new Dictionary<string, List<string>>();
                foreach (string file in group.Value)
                {
                    ct.ThrowIfCancellationRequested();
                    string? full = FullHash(file);
                    if (full is null) continue;
                    if (!byFull.TryGetValue(full, out var l))
                        byFull[full] = l = new List<string>();
                    l.Add(file);
                }

                foreach (var dup in byFull.Where(kv => kv.Value.Count > 1))
                {
                    var g = new DuplicateGroup { Hash = dup.Key, FileSize = size };
                    foreach (string f in dup.Value)
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

            progress?.Report(new DuplicateScanProgress
            {
                Phase = "Duplikate werden geprüft",
                FilesInspected = inspected,
                GroupsFound = result.Count
            });
        }

        return result.OrderByDescending(g => g.WastedBytes).ToList();
    }

    private static bool IsExcluded(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => ExcludedDirNames.Contains(p, StringComparer.OrdinalIgnoreCase));
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
