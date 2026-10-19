using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using GamingHub.Core.Models;
using Microsoft.Win32;

namespace GamingHub.Core.Services;

/// <summary>
/// Reads live CPU / RAM / disk / network usage, running processes and static
/// CPU / GPU information for a Task-Manager-like system overview.
/// </summary>
public sealed class SystemMonitorService
{
    private readonly int _ourPid = Environment.ProcessId;

    private ulong _prevIdle, _prevKernel, _prevUser;
    private bool _hasPrev;

    private Dictionary<int, (TimeSpan Cpu, DateTime At)> _prevProcessTimes = new();
    private readonly Dictionary<string, (long Received, long Sent, DateTime At)> _netPrev = new();

    /// <summary>Zähler für die Drosselung der (teuren) Prozess-Abfrage.</summary>
    private int _processTick;

    /// <summary>Serialisiert parallele Sample()-Aufrufe (WMI + Prozess-Enumeration),
    /// damit der veränderliche Messzustand nicht durcheinanderkommt.</summary>
    private readonly object _sampleLock = new();

    public string CpuName { get; private set; } = "Unbekannte CPU";
    public string GpuName { get; private set; } = "Unbekannte GPU";
    public ulong GpuVramBytes { get; private set; }
    public int CpuCores { get; private set; }
    public int CpuThreads { get; private set; }

    /// <summary>Anmeldename des aktuell angemeldeten Windows-Benutzers.</summary>
    public string CurrentUserName { get; } = Environment.UserName;

    /// <summary>Domäne bzw. Computername des angemeldeten Benutzers.</summary>
    public string CurrentUserDomain { get; } = Environment.UserDomainName;

    /// <summary>Anzahl der Prozesse aus der letzten Messung (falls erhebbar).</summary>
    public int LastProcessCount { get; private set; }

    public SystemMonitorService()
    {
        LoadStaticCpuInfo();
        LoadStaticGpuInfo();
    }

    public SystemSnapshot Sample()
    {
        lock (_sampleLock)
        {
        double cpu = GetCpuUsage();
        GetMemory(out ulong total, out ulong used);
        GetDiskThroughput(out double diskRead, out double diskWrite);
        double gpu = GetGpuUsage();

        return new SystemSnapshot
        {
            CpuName = CpuName,
            CpuCores = CpuCores,
            CpuThreads = CpuThreads,
            CpuUsagePercent = cpu,
            RamTotalBytes = total,
            RamUsedBytes = used,
            GpuName = GpuName,
            GpuVramBytes = GpuVramBytes,
            GpuUsagePercent = gpu,
            Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64),
            OsDescription = RuntimeInformation.OSDescription.Trim(),
            MachineName = Environment.MachineName,
            UserName = CurrentUserName,
            UserDomain = CurrentUserDomain,
            DiskReadBytesPerSec = diskRead,
            DiskWriteBytesPerSec = diskWrite,
            Adapters = SampleNetwork(),
            Processes = SampleProcesses(),
            ProcessCount = LastProcessCount > 0 ? LastProcessCount : Process.GetProcesses().Length,
            TimestampUtc = DateTime.UtcNow
        };
        }
    }

    /// <summary>Listet die lokalen Windows-Benutzerkonten (für die Premium-Ansicht).</summary>
    public List<LocalUserInfo> GetLocalUsers()
    {
        var list = new List<LocalUserInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, FullName, Disabled, LocalAccount FROM Win32_UserAccount");
            foreach (var obj in searcher.Get())
            {
                list.Add(new LocalUserInfo
                {
                    Name = (obj["Name"] as string)?.Trim() ?? string.Empty,
                    FullName = (obj["FullName"] as string)?.Trim() ?? string.Empty,
                    Disabled = obj["Disabled"] is bool d && d,
                    LocalAccount = obj["LocalAccount"] is bool la && la
                });
            }
        }
        catch { }
        return list.OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Listet die lokalen Windows-Benutzergruppen (für die Premium-Ansicht).</summary>
    public List<LocalGroupInfo> GetLocalGroups()
    {
        var list = new List<LocalGroupInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, Description FROM Win32_Group");
            foreach (var obj in searcher.Get())
            {
                list.Add(new LocalGroupInfo
                {
                    Name = (obj["Name"] as string)?.Trim() ?? string.Empty,
                    Description = (obj["Description"] as string)?.Trim() ?? string.Empty
                });
            }
        }
        catch { }
        return list.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Liest die GPU-Auslastung über den WMI-Leistungsindikator „GPU Engine“
    /// (korrekte Klasse: Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine).
    /// Liefert -1, wenn kein Wert erhebbar ist.
    /// </summary>
    private static double GetGpuUsage()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine " +
                "WHERE InstanceName LIKE '%engtype_3D%'");
            double max = -1;
            foreach (var obj in searcher.Get())
            {
                double v = Convert.ToDouble(obj["UtilizationPercentage"] ?? 0.0);
                if (!double.IsFinite(v)) continue;
                v = Math.Max(0, v);
                if (v > max) max = v;
            }
            if (max < 0)
            {
                // Fallback: ohne engtype-Filter (alle Engines) den Max-Wert nehmen.
                using var any = new ManagementObjectSearcher(
                    "SELECT UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
                foreach (var obj in any.Get())
                {
                    double v = Convert.ToDouble(obj["UtilizationPercentage"] ?? 0.0);
                    if (!double.IsFinite(v)) continue;
                    v = Math.Max(0, v);
                    if (v > max) max = v;
                }
            }
            return max < 0 ? -1 : Math.Clamp(max, 0, 100);
        }
        catch
        {
            return -1;
        }
    }

    private static void GetDiskThroughput(out double read, out double write)
    {
        read = 0;
        write = 0;
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DiskReadBytesPersec, DiskWriteBytesPersec FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk WHERE Name='_Total'");
            foreach (var obj in searcher.Get())
            {
                read = Math.Max(0, Convert.ToDouble(obj["DiskReadBytesPersec"] ?? 0.0));
                write = Math.Max(0, Convert.ToDouble(obj["DiskWriteBytesPersec"] ?? 0.0));
                break;
            }
        }
        catch { }
    }

    private List<NetworkAdapterSample> SampleNetwork()
    {
        var result = new List<NetworkAdapterSample>();
        var now = DateTime.UtcNow;
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;

                IPv4InterfaceStatistics stats;
                try { stats = ni.GetIPv4Statistics(); }
                catch { continue; }

                long received = stats.BytesReceived;
                long sent = stats.BytesSent;
                double down = 0, up = 0;
                if (_netPrev.TryGetValue(ni.Id, out var prev))
                {
                    double sec = Math.Max(0.001, (now - prev.At).TotalSeconds);
                    down = Math.Max(0, (received - prev.Received) / sec);
                    up = Math.Max(0, (sent - prev.Sent) / sec);
                }
                _netPrev[ni.Id] = (received, sent, now);

                result.Add(new NetworkAdapterSample
                {
                    Name = ni.Name,
                    DownloadBytesPerSec = down,
                    UploadBytesPerSec = up
                });
            }
        }
        catch
        {
            // ignore
        }

        return result;
    }

    private List<ProcessSample> _lastProcessSamples = new();

    private List<ProcessSample> SampleProcesses()
    {
        // RAM-schonend: die (teure) Prozess-Enumeration nur bei jedem 2. Aufruf,
        // dazwischen wird das letzte Ergebnis wiederverwendet.
        _processTick++;
        if (_processTick % 2 == 0 && _lastProcessSamples.Count > 0)
            return _lastProcessSamples;

        var current = new Dictionary<int, (TimeSpan Cpu, DateTime At)>();
        var samples = new List<ProcessSample>();
        var now = DateTime.UtcNow;
        int count = 0;

        try
        {
            foreach (var p in Process.GetProcesses())
            {
                count++;
                try
                {
                    var cpu = p.TotalProcessorTime;
                    current[p.Id] = (cpu, now);

                    if (_prevProcessTimes.TryGetValue(p.Id, out var prev))
                    {
                        double delta = Math.Max(0, (cpu - prev.Cpu).TotalSeconds);
                        double elapsed = Math.Max(0.001, (now - prev.At).TotalSeconds);
                        double percent = delta / elapsed / Math.Max(1, Environment.ProcessorCount) * 100.0;
                        if (percent >= 0)
                        {
                            samples.Add(new ProcessSample
                            {
                                Name = string.IsNullOrWhiteSpace(p.ProcessName) ? "unbekannt" : p.ProcessName,
                                Pid = p.Id,
                                CpuPercent = percent,
                                WorkingSetBytes = p.WorkingSet64,
                                IsOurs = p.Id == _ourPid
                            });
                        }
                    }
                }
                catch
                {
                    // skip unreadable process
                }
            }
        }
        catch
        {
            // ignore
        }

        _prevProcessTimes = current;
        LastProcessCount = count;
        _lastProcessSamples = samples
            .OrderByDescending(x => x.CpuPercent)
            .ThenByDescending(x => x.WorkingSetBytes)
            .ToList();
        return _lastProcessSamples;
    }

    private void LoadStaticCpuInfo()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (key?.GetValue("ProcessorNameString") is string name)
                CpuName = name.Trim();
        }
        catch { }

        try
        {
            foreach (var p in new ManagementObjectSearcher("SELECT NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor").Get())
            {
                if (p["NumberOfCores"] is not null) CpuCores = Convert.ToInt32(p["NumberOfCores"]);
                if (p["NumberOfLogicalProcessors"] is not null) CpuThreads = Convert.ToInt32(p["NumberOfLogicalProcessors"]);
                break;
            }
        }
        catch { }

        if (CpuThreads == 0) CpuThreads = Environment.ProcessorCount;
        if (CpuCores == 0) CpuCores = Math.Max(1, CpuThreads / 2);
    }

    private void LoadStaticGpuInfo()
    {
        try
        {
            foreach (var gpu in new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController").Get())
            {
                string? name = gpu["Name"] as string;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                // Prefer the first non-basic display adapter.
                if (GpuName == "Unbekannte GPU" || !name.Contains("Basic", StringComparison.OrdinalIgnoreCase))
                {
                    GpuName = name.Trim();
                    if (gpu["AdapterRAM"] is not null)
                        GpuVramBytes = Convert.ToUInt64(Math.Max(0, Convert.ToInt64(gpu["AdapterRAM"])));
                }
                if (!name.Contains("Basic", StringComparison.OrdinalIgnoreCase))
                    break;
            }
        }
        catch { }

        // Registry gives real VRAM (AdapterRAM is 32-bit limited).
        TryReadVramFromRegistry();
    }

    private void TryReadVramFromRegistry()
    {
        try
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (baseKey is null) return;

            foreach (string sub in baseKey.GetSubKeyNames())
            {
                if (!sub.All(char.IsDigit)) continue;
                using var gpuKey = baseKey.OpenSubKey(sub);
                object? qw = gpuKey?.GetValue("HardwareInformation.qwMemorySize");
                if (qw is not null)
                {
                    ulong vram = Convert.ToUInt64(Math.Max(0, Convert.ToInt64(qw)));
                    if (vram > GpuVramBytes)
                        GpuVramBytes = vram;
                }
            }
        }
        catch { }
    }

    private double GetCpuUsage()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
            return 0;

        ulong idleT = ToUlong(idle), kernelT = ToUlong(kernel), userT = ToUlong(user);
        if (!_hasPrev)
        {
            _prevIdle = idleT; _prevKernel = kernelT; _prevUser = userT; _hasPrev = true;
            return 0;
        }

        ulong idleDelta = idleT - _prevIdle;
        ulong kernelDelta = kernelT - _prevKernel;
        ulong userDelta = userT - _prevUser;
        ulong total = kernelDelta + userDelta; // kernel includes idle

        _prevIdle = idleT; _prevKernel = kernelT; _prevUser = userT;
        if (total == 0)
            return 0;
        double usage = (1.0 - (double)idleDelta / total) * 100.0;
        return Math.Clamp(usage, 0, 100);
    }

    private static ulong ToUlong(FILETIME ft) => ((ulong)ft.dwHighDateTime << 32) | ft.dwLowDateTime;

    private static void GetMemory(out ulong total, out ulong used)
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref status))
        {
            total = status.ullTotalPhys;
            used = status.ullTotalPhys - status.ullAvailPhys;
        }
        else
        {
            total = 0; used = 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME { public uint dwLowDateTime; public uint dwHighDateTime; }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}