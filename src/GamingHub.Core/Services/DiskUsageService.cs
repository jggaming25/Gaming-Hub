using System.Diagnostics;

namespace GamingHub.Core.Services;

/// <summary>Directory size and drive volume helpers.</summary>
public sealed class DiskUsageService
{
    public long GetDirectorySize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return 0;

        long total = 0;
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        try
        {
            foreach (string file in Directory.EnumerateFiles(path, "*", options))
            {
                try { total += new FileInfo(file).Length; }
                catch { /* skip locked file */ }
            }
        }
        catch
        {
            // ignore top-level access failures
        }

        return total;
    }

    public IReadOnlyList<Models.DiskVolume> GetVolumes()
    {
        var list = new List<Models.DiskVolume>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady)
                    continue;
                list.Add(new Models.DiskVolume
                {
                    Root = drive.RootDirectory.FullName,
                    Label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.Name : drive.VolumeLabel,
                    DriveFormat = drive.DriveFormat,
                    DriveType = drive.DriveType.ToString(),
                    TotalBytes = drive.TotalSize,
                    FreeBytes = drive.TotalFreeSpace
                });
            }
            catch
            {
                // skip
            }
        }
        return list;
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
        double value = bytes;
        int i = 0;
        while (value >= 1024 && i < units.Length - 1)
        {
            value /= 1024;
            i++;
        }
        return $"{value:0.##} {units[i]}";
    }
}
