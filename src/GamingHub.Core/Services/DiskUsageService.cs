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
            bool isNetwork = drive.DriveType == DriveType.Network;
            try
            {
                if (!drive.IsReady)
                {
                    // Nicht verbundene Netzlaufwerke wenigstens auflisten, damit
                    // die Bibliotheksfilter-Auswahl vollständig bleibt.
                    if (isNetwork)
                        list.Add(new Models.DiskVolume
                        {
                            Root = drive.Name,
                            Label = "Netzlaufwerk (nicht verbunden)",
                            DriveType = drive.DriveType.ToString(),
                            IsNetwork = true,
                            AccessRightCount = 0,
                            AccessDisplay = "Nicht verbunden"
                        });
                    continue;
                }

                // Rechte nur bei Netzlaufwerken anzeigen/prüfen. Bei lokalen
                // Laufwerken wäre ein Schreibtest im Stammverzeichnis durch
                // UAC/Rechtevererbung irreführend.
                int rights = isNetwork ? DetectUserRights(drive.RootDirectory.FullName) : 0;
                list.Add(new Models.DiskVolume
                {
                    Root = drive.RootDirectory.FullName,
                    Label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.Name : drive.VolumeLabel,
                    DriveFormat = drive.DriveFormat,
                    DriveType = drive.DriveType.ToString(),
                    TotalBytes = drive.TotalSize,
                    FreeBytes = drive.TotalFreeSpace,
                    IsNetwork = isNetwork,
                    AccessRightCount = rights,
                    AccessDisplay = isNetwork ? AccessText(rights) : string.Empty
                });
            }
            catch
            {
                // skip
            }
        }
        return list;
    }

    /// <summary>Ermittelt die effektiven Rechte des angemeldeten Benutzers:
    /// 0 = kein Zugriff, 1 = nur Lesen, 2 = Lesen und Schreiben.</summary>
    private static int DetectUserRights(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return 0;

        int rights = 0;
        try
        {
            // Lesen: mindestens ein Eintrag lässt sich auflisten.
            using var enumerator = Directory.EnumerateFileSystemEntries(root).GetEnumerator();
            if (enumerator.MoveNext()) rights++;
            else rights++; // leeres, aber zugängliches Verzeichnis zählt als lesbar
        }
        catch
        {
            return 0;
        }

        string probe = Path.Combine(root, $".gaminghub_write_{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(probe, new byte[] { 0 });
            rights++;
        }
        catch
        {
            // keine Schreibrechte
        }
        finally
        {
            try { if (File.Exists(probe)) File.Delete(probe); } catch { }
        }

        return rights;
    }

    private static string AccessText(int rights) => rights switch
    {
        >= 2 => "2/2 Rechte (Lesen, Schreiben)",
        1 => "1/2 Rechte (Nur Lesen)",
        _ => "0/2 Rechte (Kein Zugriff)"
    };

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
