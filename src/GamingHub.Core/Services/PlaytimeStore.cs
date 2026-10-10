using System.Text.Json;

namespace GamingHub.Core.Services;

public sealed class PlaytimeRecord
{
    /// <summary>Playtime tracked by Gaming Hub itself.</summary>
    public double TrackedSeconds { get; set; }

    /// <summary>Already-existing playtime (e.g. imported from Steam), not re-counted.</summary>
    public double BaselineSeconds { get; set; }

    public DateTime? LastPlayedUtc { get; set; }

    public double TotalSeconds => BaselineSeconds + TrackedSeconds;
}

/// <summary>Persists cumulative playtime per game/app identifier.</summary>
public sealed class PlaytimeStore
{
    private readonly string _path;
    private readonly Dictionary<string, PlaytimeRecord> _records = new(StringComparer.OrdinalIgnoreCase);

    public PlaytimeStore(string dataDirectory)
    {
        _path = Path.Combine(dataDirectory, "playtime.json");
        Load();
    }

    public PlaytimeRecord Get(string id)
    {
        if (!_records.TryGetValue(id, out var rec))
        {
            rec = new PlaytimeRecord();
            _records[id] = rec;
        }
        return rec;
    }

    public void AddSeconds(string id, double seconds, DateTime playedUtc)
    {
        if (seconds <= 0)
            return;
        var rec = Get(id);
        rec.TrackedSeconds += seconds;
        rec.LastPlayedUtc = playedUtc;
    }

    /// <summary>Seeds already-existing playtime (e.g. Steam) so it counts, never decreases.</summary>
    public void SeedBaseline(string id, double baselineSeconds, DateTime? lastPlayedUtc)
    {
        if (baselineSeconds <= 0 && lastPlayedUtc is null)
            return;
        var rec = Get(id);
        if (baselineSeconds > rec.BaselineSeconds)
            rec.BaselineSeconds = baselineSeconds;
        if (lastPlayedUtc is { } lp && (rec.LastPlayedUtc is null || lp > rec.LastPlayedUtc))
            rec.LastPlayedUtc = lp;
    }

    private sealed class FileEntry
    {
        public double TrackedSeconds { get; set; }
        public double BaselineSeconds { get; set; }
        public double TotalSeconds { get; set; } // legacy field, migrated once
        public DateTime? LastPlayedUtc { get; set; }
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(_path))
                return;
            var dict = JsonSerializer.Deserialize<Dictionary<string, FileEntry>>(File.ReadAllText(_path));
            if (dict is null)
                return;
            _records.Clear();
            foreach (var kv in dict)
            {
                _records[kv.Key] = new PlaytimeRecord
                {
                    TrackedSeconds = (kv.Value.TrackedSeconds > 0 || kv.Value.BaselineSeconds > 0)
                        ? kv.Value.TrackedSeconds
                        : kv.Value.TotalSeconds, // migrate legacy data
                    BaselineSeconds = kv.Value.BaselineSeconds,
                    LastPlayedUtc = kv.Value.LastPlayedUtc
                };
            }
        }
        catch
        {
            // ignore
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            var dict = _records.ToDictionary(
                kv => kv.Key,
                kv => new FileEntry
                {
                    TrackedSeconds = kv.Value.TrackedSeconds,
                    BaselineSeconds = kv.Value.BaselineSeconds,
                    LastPlayedUtc = kv.Value.LastPlayedUtc
                },
                StringComparer.OrdinalIgnoreCase);
            File.WriteAllText(_path, JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // ignore
        }
    }
}