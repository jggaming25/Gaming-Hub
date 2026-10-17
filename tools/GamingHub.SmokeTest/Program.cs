using GamingHub.Core.Services;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("=== Gaming Hub Smoke Test ===\n");

// 1) System monitor
var monitor = new SystemMonitorService();
Console.WriteLine($"[CPU] {monitor.CpuName} ({monitor.CpuCores} Kerne / {monitor.CpuThreads} Threads)");
Console.WriteLine($"[GPU] {monitor.GpuName} VRAM={DiskUsageService.FormatBytes((long)monitor.GpuVramBytes)}");
Thread.Sleep(600);
var s1 = monitor.Sample();
Thread.Sleep(600);
var s2 = monitor.Sample();
Console.WriteLine($"[RAM] {DiskUsageService.FormatBytes((long)s2.RamTotalBytes)} total / {DiskUsageService.FormatBytes((long)s2.RamUsedBytes)} benutzt ({s2.RamUsagePercent:0.0}%)");
Console.WriteLine($"[CPU-Auslastung] {s2.CpuUsagePercent:0.0}%\n");

// 2) Game scanners
var steam = new SteamScanner().Scan().ToList();
var epic = new EpicScanner().Scan().ToList();
var reg = new RegistryAppScanner().Scan().ToList();
Console.WriteLine($"[Steam]  {steam.Count} Spiele");
foreach (var g in steam.Take(3)) Console.WriteLine($"   - {g.Name} ({DiskUsageService.FormatBytes(g.SizeOnDisk)})");
Console.WriteLine($"[Epic]   {epic.Count} Spiele");
foreach (var g in epic.Take(3)) Console.WriteLine($"   - {g.Name}");
Console.WriteLine($"[Registry] {reg.Count} Apps");
foreach (var g in reg.Take(3)) Console.WriteLine($"   - {g.Name}");

// 3) Disk volumes
Console.WriteLine("\n[Laufwerke]");
foreach (var v in new DiskUsageService().GetVolumes())
    Console.WriteLine($"   {v.Root} {v.Label} {DiskUsageService.FormatBytes(v.UsedBytes)}/{DiskUsageService.FormatBytes(v.TotalBytes)} ({v.UsedPercent:0.0}%)");

// 4) Duplicate finder
string temp = Path.Combine(Path.GetTempPath(), "gh_smoke_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
File.WriteAllText(Path.Combine(temp, "a.txt"), new string('x', 5000));
File.WriteAllText(Path.Combine(temp, "b_copy.txt"), new string('x', 5000));
File.WriteAllText(Path.Combine(temp, "c_unique.txt"), "different content here");
Console.WriteLine($"\n[Cleanup] Testordner: {temp}");
var finder = new DuplicateFinderService();
var groups = await finder.FindDuplicatesAsync(new DuplicateScanOptions
{
    Roots = { temp },
    MinFileSizeBytes = 10,
    ExcludeSystemDirectories = false
}, null, CancellationToken.None);
Console.WriteLine($"[Cleanup] {groups.Count} Duplikat-Gruppe(n) gefunden");
foreach (var g in groups)
    Console.WriteLine($"   Hash {g.Hash[..8]}… · {g.Files.Count}× · freigebbar {DiskUsageService.FormatBytes(g.WastedBytes)}");

Directory.Delete(temp, true);

// 5) OpenRGB SDK (nur wenn der SDK-Server läuft)
Console.WriteLine("\n[OpenRGB]");
if (RgbService.IsServerRunning())
{
    var rgbDir = Path.Combine(Path.GetTempPath(), "gh_rgb_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(rgbDir);
    var rgb = new RgbService(rgbDir);
    var connected = await rgb.ConnectAsync(launchIfMissing: false);
    Console.WriteLine($"   Verbunden: {connected} · Protokoll v{rgb.ProtocolVersion}");
    foreach (var d in rgb.Devices.Values)
        Console.WriteLine($"   - {d.Name} [{d.Vendor}] · LEDs={d.LedCount} · Zonen={d.Zones.Count} · Modi={d.Modes.Count} · aktiv={d.CurrentEffect}");
    if (rgb.Devices.Count == 0)
        Console.WriteLine("   (keine Geräte gemeldet)");
    rgb.Disconnect();
    try { Directory.Delete(rgbDir, true); } catch { }
}
else
{
    Console.WriteLine("   SDK-Server (127.0.0.1:6742) nicht erreichbar – übersprungen.");
}

Console.WriteLine("\n=== Fertig ===");
