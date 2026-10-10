namespace GamingHub.Core.Models;

public sealed class SystemSnapshot
{
    public string CpuName { get; set; } = "Unbekannte CPU";
    public int CpuCores { get; set; }
    public int CpuThreads { get; set; }
    public double CpuUsagePercent { get; set; }

    public ulong RamTotalBytes { get; set; }
    public ulong RamUsedBytes { get; set; }
    public double RamUsagePercent => RamTotalBytes == 0 ? 0 : (double)RamUsedBytes / RamTotalBytes * 100.0;

    public string GpuName { get; set; } = "Unbekannte GPU";
    public ulong GpuVramBytes { get; set; }

    /// <summary>GPU utilization if available, otherwise -1.</summary>
    public double GpuUsagePercent { get; set; } = -1;

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}

public enum GraphicsPreset
{
    Unplayable,
    Low,
    Medium,
    High,
    Ultra
}

public sealed class GameCompatResult
{
    public string GameName { get; set; } = string.Empty;
    public GraphicsPreset Recommended { get; set; } = GraphicsPreset.Medium;
    public int Score { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<string> Notes { get; set; } = new();
    public List<string> Bottlenecks { get; set; } = new();
}
