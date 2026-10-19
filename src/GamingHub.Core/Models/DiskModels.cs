namespace GamingHub.Core.Models;

public sealed class DiskVolume
{
    public string Root { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string DriveFormat { get; set; } = string.Empty;
    public string DriveType { get; set; } = string.Empty;
    public long TotalBytes { get; set; }
    public long FreeBytes { get; set; }

    /// <summary>Anzahl der Rechte des angemeldeten Benutzers auf diesem
    /// Laufwerk (0 = kein Zugriff, 1 = nur Lesen, 2 = Lesen und Schreiben).</summary>
    public int AccessRightCount { get; set; }

    /// <summary>Kurztext für die Anzeige, z. B. "2/2 Rechte (Lesen, Schreiben)".</summary>
    public string AccessDisplay { get; set; } = string.Empty;

    public bool IsNetwork { get; set; }

    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);
    public double UsedPercent => TotalBytes <= 0 ? 0 : (double)UsedBytes / TotalBytes * 100.0;
}

public sealed class DuplicateFile
{
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime LastWriteUtc { get; set; }

    public string FileName => System.IO.Path.GetFileName(Path);
    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
}

public sealed class DuplicateGroup
{
    public string Hash { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public List<DuplicateFile> Files { get; set; } = new();

    /// <summary>Bytes that can be reclaimed by keeping exactly one copy.</summary>
    public long WastedBytes => FileSize * Math.Max(0, Files.Count - 1);
}
