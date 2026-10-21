using System.Management;
using System.Security.Principal;
using System.Text.Json;

namespace GamingHub.Core.Services;

public sealed class InstalledDriverInfo
{
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceClass { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string DriverDate { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
}

public sealed class DriverUpdateInfo
{
    public string UpdateId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DriverClass { get; set; } = string.Empty;
    public string DriverProvider { get; set; } = string.Empty;
    public string DriverVersion { get; set; } = string.Empty;
    public string DriverDate { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}

public sealed class DriverInstallResult
{
    public bool Success { get; set; }
    public bool RebootRequired { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Liest installierte PnP-Treiber über WMI und sucht/installiert Treiber-Updates
/// über den Windows Update Agent (WUA).
/// </summary>
public static class DriverUpdateService
{
    public static bool IsAdministrator
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    public static List<InstalledDriverInfo> GetInstalledDrivers()
    {
        var list = new List<InstalledDriverInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceName, DeviceClass, DriverProviderName, DriverVersion, DriverDate, DeviceID " +
                "FROM Win32_PnPSignedDriver");
            foreach (var obj in searcher.Get())
            {
                string name = (obj["DeviceName"] as string)?.Trim() ?? string.Empty;
                string version = (obj["DriverVersion"] as string)?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version))
                    continue;

                string date = string.Empty;
                if (obj["DriverDate"] is DateTime parsedDate)
                {
                    date = parsedDate.ToString("dd.MM.yyyy");
                }
                else if (obj["DriverDate"] is string raw && raw.Length >= 8)
                {
                    // WMI liefert z. B. 20240115000000.000000-000
                    string compact = raw.Length >= 14 ? raw[..14] : raw;
                    if (DateTime.TryParseExact(compact, "yyyyMMddHHmmss", null,
                            System.Globalization.DateTimeStyles.None, out var parsed))
                        date = parsed.ToString("dd.MM.yyyy");
                }

                list.Add(new InstalledDriverInfo
                {
                    DeviceName = name,
                    DeviceClass = (obj["DeviceClass"] as string)?.Trim() ?? string.Empty,
                    Provider = (obj["DriverProviderName"] as string)?.Trim() ?? string.Empty,
                    Version = version,
                    DriverDate = date,
                    DeviceId = (obj["DeviceID"] as string)?.Trim() ?? string.Empty
                });
            }
        }
        catch
        {
            // WMI kann auf manchen Systemen eingeschränkt sein.
        }

        return list
            .GroupBy(d => d.DeviceName + "|" + d.Version, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(d => d.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<DriverUpdateInfo> SearchDriverUpdates(out string? error)
    {
        error = null;
        var list = new List<DriverUpdateInfo>();
        try
        {
            dynamic session = CreateCom("Microsoft.Update.Session");
            dynamic searcher = session.CreateUpdateSearcher();
            dynamic result = searcher.Search("IsInstalled=0 and Type='Driver'");

            foreach (dynamic update in result.Updates)
            {
                try
                {
                    list.Add(new DriverUpdateInfo
                    {
                        UpdateId = update.Identity.UpdateID as string ?? string.Empty,
                        Title = update.Title as string ?? string.Empty,
                        Description = update.Description as string ?? string.Empty,
                        DriverClass = SafeGet(() => update.DriverClass as string),
                        DriverProvider = SafeGet(() => update.DriverProvider as string),
                        DriverVersion = SafeGet(() => update.DriverVersion as string),
                        DriverDate = FormatDate(SafeGet(() => update.DriverVerDate?.ToString())),
                        SizeBytes = SafeGetLong(() => update.MaxDownloadSize)
                    });
                }
                catch
                {
                    // Einzelne Updates dürfen die Liste nicht stoppen.
                }
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        return list.OrderBy(u => u.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Lädt ein Treiber-Update herunter und installiert es.
    /// Muss mit Administratorrechten ausgeführt werden.</summary>
    public static DriverInstallResult InstallUpdate(string updateId)
    {
        if (string.IsNullOrWhiteSpace(updateId))
            return new DriverInstallResult { Message = "Ungültige Update-ID." };

        try
        {
            dynamic session = CreateCom("Microsoft.Update.Session");
            dynamic searcher = session.CreateUpdateSearcher();
            string escaped = updateId.Replace("'", "''");
            dynamic found = searcher.Search($"UpdateID='{escaped}'");
            if ((int)found.Updates.Count == 0)
                return new DriverInstallResult { Message = "Das Update wurde nicht gefunden." };

            dynamic update = found.Updates.Item(0);
            dynamic collection = CreateCom("Microsoft.Update.UpdateColl");
            collection.Add(update);

            if (!(bool)update.EulaAccepted)
                update.AcceptEula();

            if (!(bool)update.IsDownloaded)
            {
                dynamic downloader = session.CreateUpdateDownloader();
                downloader.Updates = collection;
                dynamic downloadResult = downloader.Download();
                if ((int)downloadResult.ResultCode is not (2 or 3))
                    return new DriverInstallResult
                    {
                        Message = "Der Download ist fehlgeschlagen (Code " + downloadResult.ResultCode + ")."
                    };
            }

            dynamic installer = session.CreateUpdateInstaller();
            installer.Updates = collection;
            dynamic installResult = installer.Install();
            int code = installResult.ResultCode;
            bool reboot = installResult.RebootRequired;

            return new DriverInstallResult
            {
                Success = code is 2 or 3,
                RebootRequired = reboot,
                Message = code switch
                {
                    2 => "Treiber erfolgreich installiert.",
                    3 => "Treiber installiert – ein Neustart ist erforderlich.",
                    _ => "Installation fehlgeschlagen (Code " + code + ")."
                }
            };
        }
        catch (Exception ex)
        {
            return new DriverInstallResult { Message = "Fehler: " + ex.Message };
        }
    }

    public static void WriteResult(string path, DriverInstallResult result)
    {
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(result));
        }
        catch
        {
            // Das Ergebnis ist nur für die Rückmeldung an die App nötig.
        }
    }

    public static DriverInstallResult? ReadResult(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<DriverInstallResult>(File.ReadAllText(path))
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static object CreateCom(string progId)
    {
        var type = Type.GetTypeFromProgID(progId, throwOnError: true);
        return Activator.CreateInstance(type!)!;
    }

    private static string SafeGet(Func<string?> getter)
    {
        try { return getter() ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static long SafeGetLong(Func<object?> getter)
    {
        try { return Convert.ToInt64(getter() ?? 0); }
        catch { return 0; }
    }

    private static string FormatDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        return DateTime.TryParse(raw, out var date)
            ? date.ToString("dd.MM.yyyy")
            : raw;
    }
}
