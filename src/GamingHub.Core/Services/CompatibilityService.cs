using System.Text.RegularExpressions;
using GamingHub.Core.Models;

namespace GamingHub.Core.Services;

/// <summary>
/// Estimates whether a game will run well on the current hardware and suggests a
/// graphics preset. This is a heuristic estimate, not an official benchmark.
/// </summary>
public sealed class CompatibilityService
{
    public GameCompatResult Evaluate(string gameName, string cpuName, string gpuName, ulong ramBytes)
    {
        var gpu = ScoreGpu(gpuName);
        var cpu = ScoreCpu(cpuName);
        var ramGb = ramBytes / 1024.0 / 1024.0 / 1024.0;
        var ram = ScoreRam(ramGb);

        double score = gpu * 0.50 + cpu * 0.30 + ram * 0.20;

        var result = new GameCompatResult
        {
            GameName = gameName,
            Score = (int)Math.Round(score)
        };

        result.Recommended = score switch
        {
            >= 85 => GraphicsPreset.Ultra,
            >= 68 => GraphicsPreset.High,
            >= 48 => GraphicsPreset.Medium,
            >= 30 => GraphicsPreset.Low,
            _ => GraphicsPreset.Unplayable
        };

        result.Notes.Add($"CPU: {cpuName} (Leistung {cpu}/100)");
        result.Notes.Add($"GPU: {gpuName} (Leistung {gpu}/100)");
        result.Notes.Add($"RAM: {ramGb:0.#} GB (Leistung {ram}/100)");

        if (gpu < 30) result.Bottlenecks.Add("Die Grafikkarte ist der wahrscheinlichste Flaschenhals.");
        if (cpu < 30) result.Bottlenecks.Add("Die CPU ist der wahrscheinlichste Flaschenhals.");
        if (ramGb < 16) result.Bottlenecks.Add("Weniger als 16 GB RAM kann moderne Titel ausbremsen.");

        result.Summary = result.Recommended switch
        {
            GraphicsPreset.Ultra => "Dein System schafft dieses Spiel voraussichtlich auf hohen bis maximalen Einstellungen.",
            GraphicsPreset.High => "Hohe Einstellungen sollten problemlos möglich sein.",
            GraphicsPreset.Medium => "Mittlere Einstellungen bieten die beste Balance aus Optik und FPS.",
            GraphicsPreset.Low => "Nur niedrige Einstellungen dürften flüssig laufen.",
            _ => "Dein System ist für dieses Spiel wahrscheinlich zu schwach oder nicht geeignet."
        };

        return result;
    }

    private static int ScoreGpu(string name)
    {
        string n = name.ToUpperInvariant();

        if (n.Contains("UHD") || n.Contains("HD GRAPHICS") || n.Contains("BASIC"))
            return 8;
        if (n.Contains("IRIS") || n.Contains("VEGA") || n.Contains("RADON GRAPHICS") || n.Contains("RADEON GRAPHICS"))
            return 20;

        int gen = 0;
        var ryzen = Regex.Match(n, @"RX\s*(\d{4})");
        if (ryzen.Success) gen = int.Parse(ryzen.Groups[1].Value.Substring(0, 1)) * 10;

        var rtx = Regex.Match(n, @"RTX\s*(\d{4})");
        if (rtx.Success) gen = int.Parse(rtx.Groups[1].Value.Substring(0, 2)) * 12;

        var gtx = Regex.Match(n, @"GTX\s*(\d{3,4})");
        if (gtx.Success) gen = int.Parse(gtx.Groups[1].Value.Substring(0, 1)) * 10 + 15;

        if (n.Contains("ARC"))
            return 55;

        int score = gen switch
        {
            >= 140 => 98,
            >= 100 => 90,
            >= 70 => 80,
            >= 60 => 70,
            >= 50 => 60,
            >= 40 => 50,
            >= 30 => 40,
            >= 20 => 30,
            > 0 => 22,
            _ => 25
        };
        return Math.Clamp(score, 1, 100);
    }

    private static int ScoreCpu(string name)
    {
        string n = name.ToUpperInvariant();
        int baseScore;

        if (n.Contains("I9") || n.Contains("RYZEN 9")) baseScore = 85;
        else if (n.Contains("I7") || n.Contains("RYZEN 7")) baseScore = 72;
        else if (n.Contains("I5") || n.Contains("RYZEN 5")) baseScore = 58;
        else if (n.Contains("I3") || n.Contains("RYZEN 3")) baseScore = 42;
        else if (n.Contains("CELERON") || n.Contains("PENTIUM") || n.Contains("ATHLON")) baseScore = 20;
        else baseScore = 45;

        // Generation bump for Intel 12xxx+ or Ryzen 5xxx+
        var big = Regex.Match(n, @"\b(1[2-9]\d{3}|2\d{3})\b");
        if (big.Success) baseScore += 10;

        return Math.Clamp(baseScore, 1, 100);
    }

    private static int ScoreRam(double gb) => gb switch
    {
        >= 32 => 100,
        >= 16 => 75,
        >= 8 => 45,
        >= 4 => 20,
        _ => 5
    };
}
