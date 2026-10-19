using System.Text.RegularExpressions;
using GamingHub.Core.Models;

namespace GamingHub.Core.Services;

/// <summary>
/// Rule-based ("AI") classifier that sorts detected programs into categories
/// such as Games, Game-Tools, Office, Creator, Antivirus, AI-Tools etc. so that
/// the games list only shows actual games.
/// </summary>
public static class GameClassifier
{
    private static readonly List<(string[] Keywords, GameCategory Category)> Rules = new()
    {
        // Additional content for games (DLC, Packs, Content, Mods, Redistributables)
        (["dlc", "content pack", "additional content", "add-on", "addon", "mod pack", "modpack", "workshop", "asset pack", "sdk", "toolbox", "soundtrack", "sound pack", "extra content", "map pack", "level pack", "skin pack", "bundle", "microsoft visual c++", "visual c++"], GameCategory.GameContent),

        // Development tools (must come before Games/Creator so e.g. "Roblox Studio"
        // and "Unreal Engine" are not treated as games/creative apps)
        (["roblox studio", "unreal engine", "unreal editor", "visual studio code", "vs code", "microsoft visual studio", "visual studio", "unity", "godot", "jetbrains", "intellij", "pycharm", "webstorm", "rider", "android studio", "xamarin", "gitkraken", "github desktop", "sourcetree"], GameCategory.Development),

        // Launchers & gamer helper tools
        (["fab ue plugin", "fab plugin", "steamworks common redistributables", "train sim world 5 pc editor", "train sim world 4 pc editor", "train sim world 3 pc editor", "cesium for unreal", "ea app", "origin client", "ubisoft connect", "battle.net", "battlenet", "gog galaxy", "playnite", "razer cortex", "game booster", "game bar", "msi afterburner", "rtss", "riva tuner", "overwolf", "curseforge", "geforce experience", "nvidia app", "xbox app", "omsi-tools", "omsi tool", "omsi tools"], GameCategory.GameTool),

        // Communication & messaging
        (["discord", "whatsapp", "teamspeak", "microsoft teams", "slack", "zoom", "skype", "telegram", "signal", "messenger", "google meet", "webex", "ventrilo", "mumble", "element", "threema", "line"], GameCategory.Communication),

        // Spiele (auch Launcher/Titel, die der Nutzer als Spiel führt)
        (["bus manager", "epic games launcher", "blender", "roblox player", "roblox"], GameCategory.Game),

        // AI tools
        (["chatgpt", "chat gpt", "copilot", "github copilot", "claude", "claude code", "gemini", "midjourney", "stable diffusion", "autogpt", "llama", "ollama", "whisper", "elevenlabs", "runway", "ai studio", "notebooklm", "grok", "perplexity", "jan ai", "lm studio", "opencode", "open code", "open-code", "cursor", "windsurf", "aider", "continue ai", "ollama"], GameCategory.AiTool),

        // Office & productivity
        (["microsoft office", "word", "excel", "powerpoint", "outlook", "onenote", "access", "publisher", "libreoffice", "onlyoffice", "openoffice", "notion", "obsidian", "evernote", "todoist", "pomodoro", "adobe acrobat", "foxit", "ghostscript", "windows editor"], GameCategory.Office),

        // Antivirus & security
        (["antivirus", "defender", "avast", "avg", "norton", "mcafee", "kaspersky", "malwarebytes", "bitdefender", "eset", "g data", "clario", "hitmanpro"], GameCategory.Antivirus),

        // Browser (eigene Kategorie)
        (["chrome", "firefox", "edge", "opera", "brave", "tor", "vivaldi", "safari", "waterfox", "librewolf", "arc", "chromium"], GameCategory.Browser),

        // Creator / streaming / production / game dev
        (["obs", "streamlabs", "xsplit", "stream deck", "premiere", "after effects", "photoshop", "illustrator", "lightroom", "davinci", "resolve", "audacity", "reaper", "fl studio", "ableton", "cubase", "maya", "3ds max", "autocad", "camtasia", "filmora", "twitch", "bandicam"], GameCategory.Creator),

        // System services, drivers and utility software (not wanted in games list)
        (["chipset", "driver", "drivers", "updater", "amd software", "radeon", "intel", "realtek", "nvidia", "geforce", "directx", "redistributable", "samsung magician", "corsair", "icue", "logitech", "razer", "synapse", "steelseries", "hwinfo", "cpu-z", "gpu-z", "coretemp", "hwmonitor", "f.lux", "7-zip", "winrar", "winzip", "peazip", "everything", "notepad++", "sublime", "paint.net", "gimp", "vlc", "mpc-hc", "potplayer", "python", "node.js", "docker", "kodi", "spotify", "yt-dlp", "teamviewer", "anydesk", "putty", "filezilla", "powershell", "calculator", "control panel", "task manager", "performance monitor"], GameCategory.SystemService),

        // Other recognizable tool words
        (["tool", "utility", "configurator", "optimizer", "commander", "benchmark", "overlay", "recorder", "manager"], GameCategory.GameTool)
    };

    public static GameCategory Classify(string name, GameSource source)
    {
        if (string.IsNullOrWhiteSpace(name))
            return GameCategory.Other;

        string n = name.Trim();

        foreach (var (keywords, category) in Rules)
        {
            foreach (string keyword in keywords)
                if (Matches(n, keyword))
                    return category;
        }

        return source is GameSource.Registry or GameSource.Manual
            ? GameCategory.Other
            : GameCategory.Game;
    }

    public static bool IsGame(string name, GameSource source)
        => Classify(name, source) == GameCategory.Game;

    public static string CategoryDisplayName(GameCategory c) => c switch
    {
        GameCategory.Game => "Spiele",
        GameCategory.GameTool => "Game-Tools",
        GameCategory.GameContent => "Zusätzliche Inhalte",
        GameCategory.Communication => "Kommunikation",
        GameCategory.Office => "Office",
        GameCategory.Development => "Entwicklung",
        GameCategory.Browser => "Browser",
        GameCategory.Utility => "Dienstprogramme",
        GameCategory.Creator => "Kreativ & Streaming",
        GameCategory.Antivirus => "Antivirus",
        GameCategory.AiTool => "KI-Tools",
        GameCategory.SystemService => "System & Dienstprogramme",
        _ => "Sonstige"
    };

    private static bool Matches(string name, string keyword)
    {
        if (keyword.IndexOf(' ') >= 0)
            return name.Contains(keyword, StringComparison.OrdinalIgnoreCase);

        return Regex.IsMatch(name, $@"\b{Regex.Escape(keyword)}\b", RegexOptions.IgnoreCase);
    }
}