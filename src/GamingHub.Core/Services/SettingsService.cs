using System.Text.Json;
using System.Text.Json.Serialization;
using GamingHub.Core.Models;

namespace GamingHub.Core.Services;

/// <summary>
/// Loads and persists application settings to %AppData%\GamingHub\settings.json.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string DataDirectory { get; }
    public string SettingsPath { get; }
    public AppSettings Settings { get; private set; } = new();

    public SettingsService()
    {
        DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GamingHub");
        Directory.CreateDirectory(DataDirectory);
        SettingsPath = Path.Combine(DataDirectory, "settings.json");
        Load();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                Settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch
        {
            Settings = new AppSettings();
        }

        if (Settings.FirstEverLaunchUtc is null)
        {
            Settings.FirstEverLaunchUtc = Settings.FirstRunUtc == default ? DateTime.UtcNow : Settings.FirstRunUtc;
            Save();
        }
    }

    public void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(Settings, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // ignore persistence errors
        }
    }
}
