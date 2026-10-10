using System.Text.Json;
using GamingHub.Core.Models;

namespace GamingHub.App.Services;

/// <summary>Grants and persists achievement badges locally.</summary>
public sealed class BadgeService
{
    private const string EarlyBirdCutoff = "2026-12-31";

    private readonly string _path;
    private readonly HashSet<string> _earned = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _earnedDates = new(StringComparer.OrdinalIgnoreCase);

    public BadgeService(string dataDirectory)
    {
        _path = Path.Combine(dataDirectory, "badges.json");
        Load();
    }

    public IReadOnlyList<Badge> Catalog => new List<Badge>
    {
        new() { Id = "willkommen", Name = "Willkommen", Description = "Du hast Gaming Hub zum ersten Mal gestartet.", Glyph = "\uE8FB" },
        new() { Id = "earlybird", Name = "Früher Adopter", Description = "Gaming Hub 2026 oder früher zum ersten Mal genutzt.", Glyph = "\uE735" },
        new() { Id = "bibliothekar", Name = "Bibliothekar", Description = "Deine Spielebibliothek gescannt.", Glyph = "\uE8F1" },
        new() { Id = "aufraeumer", Name = "Aufräumer", Description = "Einen Disk-Cleanup durchgeführt.", Glyph = "\uE74D" },
        new() { Id = "systemprofi", Name = "Systemprofi", Description = "Deine System-Ressourcen geprüft.", Glyph = "\uE9D9" },
        new() { Id = "bugreporter", Name = "Bugreporter", Description = "Einen Bug gemeldet.", Glyph = "\uE7BA" },
        new() { Id = "premium_streak3", Name = "Premium-Veteran", Description = "3 Monate in Folge Premium – treue Unterstützung.", Glyph = "\uE735", EarnedUtc = null }
    };

    public bool Has(string id) => _earned.Contains(id);

    /// <summary>Wird ausgelöst, sobald ein neues Abzeichen erstmals vergeben wird
    /// (für Aktivitäts-Premium, z. B. Bibliothekar → +7 Tage).</summary>
    public event Action<string>? BadgeGranted;

    public void Grant(string id)
    {
        if (_earned.Add(id))
        {
            _earnedDates[id] = DateTime.UtcNow;
            Save();
            BadgeGranted?.Invoke(id);
        }
    }

    public void EvaluateAutomatic(DateTime? firstEverLaunchUtc)
    {
        Grant("willkommen");
        if (firstEverLaunchUtc is { } first && first <= DateTime.Parse(EarlyBirdCutoff))
            Grant("earlybird");
    }

    public List<Badge> GetStatus()
    {
        var list = new List<Badge>();
        foreach (var badge in Catalog)
        {
            bool earned = _earned.Contains(badge.Id);
            list.Add(new Badge
            {
                Id = badge.Id,
                Name = badge.Name,
                Description = badge.Description,
                Glyph = badge.Glyph,
                Earned = earned,
                EarnedUtc = _earnedDates.TryGetValue(badge.Id, out var d) ? d : null
            });
        }
        return list;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var dict = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(File.ReadAllText(_path));
            if (dict is null) return;
            foreach (var kv in dict)
            {
                _earned.Add(kv.Key);
                _earnedDates[kv.Key] = kv.Value;
            }
        }
        catch { /* ignore */ }
    }

    private void Save()
    {
        try { File.WriteAllText(_path, JsonSerializer.Serialize(_earnedDates)); }
        catch { /* ignore */ }
    }
}
