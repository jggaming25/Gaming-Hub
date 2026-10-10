using System.Management;
using System.Runtime.InteropServices;
using GamingHub.Core.Models;
using Microsoft.Win32;

namespace GamingHub.Core.Services;

/// <summary>Reads live CPU / RAM usage and static CPU / GPU information.</summary>
public sealed class SystemMonitorService
{
    private ulong _prevIdle, _prevKernel, _prevUser;
    private bool _hasPrev;

    public string CpuName { get; private set; } = "Unbekannte CPU";
    public string GpuName { get; private set; } = "Unbekannte GPU";
    public ulong GpuVramBytes { get; private set; }
    public int CpuCores { get; private set; }
    public int CpuThreads { get; private set; }

    public SystemMonitorService()
    {
        LoadStaticCpuInfo();
        LoadStaticGpuInfo();
    }

    public SystemSnapshot Sample()
    {
        double cpu = GetCpuUsage();
        GetMemory(out ulong total, out ulong used);

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
            GpuUsagePercent = -1,
            TimestampUtc = DateTime.UtcNow
        };
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
