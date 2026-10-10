using System.Text.RegularExpressions;
using GamingHub.Core.Models;

namespace GamingHub.Core.Services;

/// <summary>
/// Estimates whether a game will run well on the current hardware and suggests a
/// graphics preset. Known games use a curated per-game requirement database with
/// precise single-component comparisons; unknown games fall back to a general
/// heuristic estimate.
/// </summary>
public sealed class CompatibilityService
{
    private readonly record struct GameSpec(
        string Key, string Display,
        int GpuMin, int CpuMin, double RamMinGb,
        int GpuRec, int CpuRec, double RamRecGb);

    private static readonly GameSpec[] Specs =
    {
        new("cyberpunk", "Cyberpunk 2077", 50, 46, 12, 70, 64, 16),
        new("gtav", "GTA V", 40, 40, 8, 60, 58, 16),
        new("grandtheftauto", "GTA V", 40, 40, 8, 60, 58, 16),
        new("minecraft", "Minecraft", 24, 30, 4, 52, 52, 8),
        new("valorant", "Valorant", 26, 30, 4, 46, 46, 8),
        new("cs2", "Counter-Strike 2", 40, 40, 8, 62, 58, 16),
        new("counterstrike", "Counter-Strike 2", 40, 40, 8, 62, 58, 16),
        new("fortnite", "Fortnite", 46, 46, 8, 68, 64, 16),
        new("omsi", "OMSI 2", 16, 40, 4, 36, 58, 8),
        new("fallguys", "Fall Guys", 30, 32, 4, 50, 48, 8),
        new("eldenring", "Elden Ring", 50, 52, 12, 66, 62, 16),
        new("callofduty", "Call of Duty", 52, 54, 12, 70, 68, 16),
        new("cod", "Call of Duty", 52, 54, 12, 70, 68, 16),
        new("warzone", "Call of Duty: Warzone", 52, 54, 12, 70, 68, 16),
        new("eafc", "EA Sports FC", 30, 36, 8, 48, 50, 8),
        new("fifa", "EA Sports FC", 30, 36, 8, 48, 50, 8),
        new("witcher", "The Witcher 3", 42, 44, 6, 62, 60, 16),
        new("rust", "Rust", 52, 58, 16, 72, 72, 32),
        new("citiesskylinesii", "Cities: Skylines II", 48, 62, 16, 72, 76, 32),
        new("citiesskylines", "Cities: Skylines", 40, 52, 8, 58, 72, 16),
        new("rocketleague", "Rocket League", 34, 36, 4, 52, 50, 8),
        new("leagueoflegends", "League of Legends", 26, 28, 4, 42, 42, 8),
        new("overwatch", "Overwatch 2", 34, 38, 8, 52, 52, 8),
        new("sims", "Die Sims 4", 28, 30, 4, 44, 44, 8),
        new("battlefield", "Battlefield", 56, 58, 16, 72, 70, 16),
        new("hogwarts", "Hogwarts Legacy", 48, 54, 16, 72, 72, 32),
        new("rdr2", "Read Dead Redemption 2", 54, 56, 8, 72, 68, 16),
        new("reddead", "Red Dead Redemption 2", 54, 56, 8, 72, 68, 16),
        new("gran turismo", "Gran Turismo", 44, 46, 8, 64, 60, 16),
        new("skyrim", "Skyrim", 30, 32, 4, 52, 48, 8),
        new("fallout", "Fallout", 42, 44, 8, 62, 56, 16),
        new("doom", "DOOM / DOOM Eternal", 44, 46, 8, 64, 60, 16),
        new("stardew", "Stardew Valley", 14, 16, 2, 26, 26, 4),
        new("terraria", "Terraria", 14, 18, 2, 26, 30, 4),
        new("pubg", "PUBG", 46, 50, 8, 68, 66, 16),
        new("eurotruck", "Euro Truck Simulator 2", 26, 34, 4, 46, 52, 8),
        new("american truck", "American Truck Simulator", 26, 34, 4, 46, 52, 8),
    };

    public GameCompatResult Evaluate(string gameName, string cpuName, string gpuName, ulong ramBytes)
    {
        var gpu = ScoreGpu(gpuName);
        var cpu = ScoreCpu(cpuName);
        var ramGb = ramBytes / 1024.0 / 1024.0 / 1024.0;
        var ram = ScoreRam(ramGb);

        var norm = Normalize(gameName);
        var spec = Specs.FirstOrDefault(s => norm.Contains(s.Key));

        var result = new GameCompatResult { GameName = gameName };

        if (spec.Key is not null)
        {
            FillKnownGame(result, spec, gpu, cpu, ramGb, gpuName, cpuName);
        }
        else
        {
            FillHeuristic(result, gpu, cpu, ram, ramGb, gpuName, cpuName);
        }

        result.Notes.Add(string.Empty);
        result.Notes.Add("GPU-Skala: 0–110 (RTX 4090 ≈ 104, RTX 3060 ≈ 62, GTX 1050 ≈ 30)");
        result.Notes.Add("CPU-Skala: 0–100 (i9-13900K ≈ 97, i5-12400 ≈ 70, i5-8600 ≈ 60)");
        result.Notes.Add("Basis: deine Hardware-Scores gegen die Datenbankwerte des Spiels.");

        return result;
    }

    private static void FillKnownGame(GameCompatResult r, GameSpec s, int gpu, int cpu, double ramGb, string gpuName, string cpuName)
    {
        double gpuMinRatio = gpu / (double)s.GpuMin;
        double cpuMinRatio = cpu / (double)s.CpuMin;
        double ramMinRatio = ramGb / s.RamMinGb;
        double minFit = Math.Min(Math.Min(gpuMinRatio, cpuMinRatio), ramMinRatio);

        double idx = gpu / (double)s.GpuRec * 0.55
                   + cpu / (double)s.CpuRec * 0.25
                   + Math.Min(1.5, ramGb / s.RamRecGb) * 0.20;

        r.Recommended = idx switch
        {
            >= 1.40 => GraphicsPreset.Ultra,
            >= 1.00 => GraphicsPreset.High,
            >= 0.70 => GraphicsPreset.Medium,
            >= 0.45 => GraphicsPreset.Low,
            _ => GraphicsPreset.Unplayable
        };
        // Hard gate: below minimum spec can never show a "High/Ultra" wish.
        if (minFit < 1.0 && r.Recommended >= GraphicsPreset.Medium)
            r.Recommended = minFit >= 0.8 ? GraphicsPreset.Low : GraphicsPreset.Unplayable;

        r.Score = Math.Clamp((int)Math.Round(idx * 100), 1, 100);

        r.Notes.Add($"Spiel: {s.Display} (Datenbankeintrag gefunden)");
        r.Notes.Add($"GPU: {gpuName} → {gpu}/110 (Min. {s.GpuMin}, Empf. {s.GpuRec})");
        r.Notes.Add($"CPU: {cpuName} → {cpu}/100 (Min. {s.CpuMin}, Empf. {s.CpuRec})");
        r.Notes.Add($"RAM: {ramGb:0.#} GB (Min. {s.RamMinGb:0.#} GB, Empf. {s.RamRecGb:0.#} GB)");
        r.Notes.Add(string.Empty);
        r.Notes.Add($"Mindestanforderung: {minFit * 100:0} % erfüllt");
        r.Notes.Add($"Leistung vs. Empfehlung: {idx * 100:0} %");

        AddPreciseBottleneck(r, s, gpu, cpu, ramGb);

        r.Summary = r.Recommended switch
        {
            GraphicsPreset.Ultra =>
                $"{s.Display} läuft deutlich über der empfohlenen Hardware – maximale Einstellungen sind drin.",
            GraphicsPreset.High =>
                $"{s.Display} läuft auf Höhe der empfohlenen Hardware – hohe Einstellungen mit guten FPS.",
            GraphicsPreset.Medium =>
                $"{s.Display} liegt unter der Empfehlung, aber über der Mindestanforderung – mittlere Einstellungen sind realistisch.",
            GraphicsPreset.Low =>
                minFit < 1.0
                    ? $"{s.Display} liegt knapp unter der Mindestanforderung – nur niedrige Einstellungen."
                    : $"{s.Display} läuft, aber deutlich unter der Empfehlung – niedrige Einstellungen.",
            _ =>
                $"{s.Display} erfüllt die Mindestanforderung nicht – das Spiel wird vermutlich nicht sinnvoll laufen (Empfehlung: {minFit * 100:0} %)."
        };
    }

    private static void AddPreciseBottleneck(GameCompatResult r, GameSpec s, int gpu, int cpu, double ramGb)
    {
        var weakest = Math.Min(Math.Min(gpu / (double)s.GpuRec, cpu / (double)s.CpuRec), ramGb / s.RamRecGb);
        if (weakest >= 0.9)
            return;

        double gpuRec = gpu / (double)s.GpuRec * 100;
        double cpuRec = cpu / (double)s.CpuRec * 100;
        double ramRec = ramGb / s.RamRecGb * 100;

        if (gpuRec == weakest)
            r.Bottlenecks.Add($"Flaschenhals: Grafikkarte ({gpu}/110). Liegt bei {gpuRec:0} % der Empfehlung – ein GPU-Upgrade (oder niedrigere Auflösung/Details) hilft am meisten.");
        else if (cpuRec == weakest)
            r.Bottlenecks.Add($"Flaschenhals: CPU ({cpu}/100). Liegt bei {cpuRec:0} % der Empfehlung – CPU-lastige Szenen (viele Objekte/Siedlungen) werden stark bremsen.");
        else
            r.Bottlenecks.Add($"Flaschenhals: Arbeitsspeicher ({ramGb:0.#} GB). Liegt bei {ramRec:0} % der Empfehlung – mehr RAM verbessert besonders Ladezeiten und offene Welten.");
    }

    private static void FillHeuristic(GameCompatResult r, int gpu, int cpu, int ram, double ramGb, string gpuName, string cpuName)
    {
        double score = gpu * 0.50 + cpu * 0.30 + ram * 0.20;

        r.Recommended = score switch
        {
            >= 85 => GraphicsPreset.Ultra,
            >= 68 => GraphicsPreset.High,
            >= 48 => GraphicsPreset.Medium,
            >= 30 => GraphicsPreset.Low,
            _ => GraphicsPreset.Unplayable
        };
        r.Score = (int)Math.Round(Math.Clamp(score, 1, 100));

        r.Notes.Add("Kein Datenbankeintrag für dieses Spiel gefunden – allgemeine Schätzung.");
        r.Notes.Add($"CPU: {cpuName} (Leistung {cpu}/100)");
        r.Notes.Add($"GPU: {gpuName} (Leistung {gpu}/110)");
        r.Notes.Add($"RAM: {ramGb:0.#} GB (Leistung {ram}/100)");

        if (gpu < 30) r.Bottlenecks.Add("Die Grafikkarte ist der wahrscheinlichste Flaschenhals.");
        if (cpu < 30) r.Bottlenecks.Add("Die CPU ist der wahrscheinlichste Flaschenhals.");
        if (ramGb < 16) r.Bottlenecks.Add("Weniger als 16 GB RAM kann moderne Titel ausbremsen.");

        r.Summary = r.Recommended switch
        {
            GraphicsPreset.Ultra => "Dein System schafft dieses Spiel voraussichtlich auf hohen bis maximalen Einstellungen.",
            GraphicsPreset.High => "Hohe Einstellungen sollten problemlos möglich sein.",
            GraphicsPreset.Medium => "Mittlere Einstellungen bieten die beste Balance aus Optik und FPS.",
            GraphicsPreset.Low => "Nur niedrige Einstellungen dürften flüssig laufen.",
            _ => "Dein System ist für dieses Spiel wahrscheinlich zu schwach oder nicht geeignet."
        };
    }

    private static int ScoreRam(double gb) => gb switch
    {
        >= 32 => 100,
        >= 16 => 75,
        >= 8 => 45,
        >= 4 => 20,
        _ => 5
    };

    private static string Normalize(string s)
        => Regex.Replace(s.ToLowerInvariant(), @"[^a-z0-9]", string.Empty);

    // GPU tier table – entries are checked from best to worst, first hit wins.
    private static readonly (string Token, int Score)[] GpuTable =
    {
        ("RTX5090", 112), ("RTX5080", 100), ("RTX5070TI", 92), ("RTX5070", 86),
        ("RTX4090", 104), ("RTX4080SUPER", 96), ("RTX4080", 94), ("RTX4070TISUPER", 87), ("RTX4070TI", 85), ("RTX4070SUPER", 82), ("RTX4070", 80),
        ("RTX4060TI", 66), ("RTX4060", 58), ("RTX4050", 46), ("RTX5060", 62), ("RTX5060TI", 68),
        ("RTX3090TI", 96), ("RTX3090", 92), ("RTX3080TI", 89), ("RTX3080", 85), ("RTX3070TI", 78), ("RTX3070", 74), ("RTX3060TI", 70), ("RTX3060", 62), ("RTX3050", 48),
        ("RTX2080TI", 74), ("RTX2080SUPER", 70), ("RTX2080", 66), ("RTX2070SUPER", 64), ("RTX2070", 60), ("RTX2060SUPER", 58), ("RTX2060", 52),
        ("GTX1660TI", 56), ("GTX1660SUPER", 54), ("GTX1660", 49), ("GTX1650SUPER", 42), ("GTX1650TI", 40), ("GTX1650", 36), ("GTX1630", 26),
        ("GTX1080TI", 72), ("GTX1080", 66), ("GTX1070TI", 63), ("GTX1070", 58), ("GTX1060", 50), ("GTX1050TI", 34), ("GTX1050", 30), ("GTX1030", 24),
        ("GTX980TI", 50), ("GTX980", 44), ("GTX970", 40), ("GTX960", 32), ("GTX950", 26), ("GTX750TI", 22), ("GTX750", 18), ("GTX580", 18), ("GTX570", 15), ("GTX560", 13), ("GTX460", 12),
        ("RX7900XTX", 98), ("RX7900XT", 92), ("RX7900GRE", 88), ("RX7900", 86), ("RX7800XT", 82), ("RX7700XT", 76), ("RX7600XT", 64), ("RX7600", 58), ("RX7500XT", 48),
        ("RX6950XT", 90), ("RX6900XT", 87), ("RX6800XT", 82), ("RX6800", 78), ("RX6750XT", 70), ("RX6700XT", 66), ("RX6700", 62), ("RX6650XT", 58), ("RX6600XT", 54), ("RX6600", 50), ("RX6500XT", 36), ("RX6400", 28),
        ("RX5700XT", 50), ("RX5700", 46), ("RX5600XT", 40), ("RX5600", 34), ("RX5500XT", 30), ("RX580", 26), ("RX570", 22), ("RX590", 28), ("RX560", 18), ("RX550", 14), ("RX480", 26), ("RX470", 22),
        ("RADEONVII", 44), ("VEGA64", 40), ("VEGA56", 34),
        ("ARCB580", 62), ("ARCB570", 55), ("ARCA770", 60), ("ARCA750", 52), ("ARCA580", 44), ("ARCA380", 34), ("ARCA310", 22),
        ("IRISXE", 24), ("IRISX", 22), ("IRIS", 18),
        ("UHDGRAPHICS", 14), ("HDGRAPHICS", 10),
        ("RADEONGRAPHICS", 16), ("RADEONVEGA", 20), ("VEGAGRAPHICS", 20), ("RADEON", 18),
        ("GEFORCE", 20),
    };

    private static int ScoreGpu(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return 15;

        string n = Regex.Replace(name.ToUpperInvariant(), @"[^A-Z0-9]", string.Empty);

        int score = 15;
        foreach (var (token, s) in GpuTable)
        {
            if (n.Contains(token))
            {
                score = s;
                break;
            }
        }

        // Mobile/notebook GPUs are typically ~15 % weaker than their desktop siblings.
        if (n.Contains("LAPTOP") || n.Contains("MAXQ") || n.Contains("MOBILE") || n.Contains("NBM"))
            score = (int)(score * 0.85);

        return Math.Clamp(score, 1, 115);
    }

    private static int ScoreCpu(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return 40;

        string n = Regex.Replace(name.ToUpperInvariant(), @"\s+", " ");

        var intel = Regex.Match(n, @"\bI\s*([3579])[-\s]?(\d{4,5})");
        if (intel.Success)
        {
            int tier = int.Parse(intel.Groups[1].Value);
            int model = int.Parse(intel.Groups[2].Value);
            int gen = model >= 10000 ? model / 1000 : model / 100;
            int baseScore = tier switch { 9 => 90, 7 => 76, 5 => 64, _ => 48 };
            int genAdj = gen switch
            {
                >= 14 => +8,
                13 => +7,
                12 => +6,
                11 => +3,
                10 or 9 => 0,
                8 => -4,
                7 => -6,
                6 => -10,
                5 => -14,
                _ => -20
            };
            int score = baseScore + genAdj;
            return Math.Clamp(ApplyCpuMobilePenalty(n, score, intel.Groups[2].Value), 10, 100);
        }

        var ryzen = Regex.Match(n, @"RYZEN\s*([3579])(?:[\s-]?(\d{4}))?");
        if (ryzen.Success)
        {
            int tier = int.Parse(ryzen.Groups[1].Value);
            string modelStr = ryzen.Groups[2].Value;
            int baseScore = tier switch { 9 => 88, 7 => 74, 5 => 60, _ => 44 };
            int genAdj = modelStr.Length == 4 ? (int.Parse(modelStr.Substring(0, 1)) switch
            {
                9 or 8 => +8,
                7 => +7,
                6 => +3,
                5 => +5,
                4 => +2,
                3 => 0,
                2 => -6,
                _ => -10
            }) : 0;
            if (n.Contains("X3D")) genAdj += 6;
            int score = baseScore + genAdj;
            return Math.Clamp(ApplyCpuMobilePenalty(n, score, modelStr), 10, 100);
        }

        if (n.Contains("THREADRIPPER")) return 92;
        if (n.Contains("XEON")) return 70;
        if (n.Contains("PENTIUM")) return 26;
        if (n.Contains("CELERON")) return 22;
        if (n.Contains("ATHLON")) return 28;
        if (n.Contains("SEMPRON")) return 14;
        if (n.Contains("CORE2")) return 32;
        return 45;
    }

    private static int ApplyCpuMobilePenalty(string upperName, int score, string modelSuffix)
    {
        if (upperName.EndsWith(" U") || modelSuffix.EndsWith("U"))
            return (int)(score * 0.78);
        if (upperName.Contains("HX") || upperName.Contains("HQ") || upperName.EndsWith(" H"))
            return (int)(score * 0.95);
        if (upperName.Contains("G7") || modelSuffix.Contains("G7"))
            return (int)(score * 0.82);
        return score;
    }
}