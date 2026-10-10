using System.Text.Json;

namespace GamingHub.Core.Services;

public sealed class PlaytimeRecord
{
    public double TotalSeconds { get; set; }
    public DateTime? LastPlayedUtc { get; set; }
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
        rec.TotalSeconds += seconds;
        rec.LastPlayedUtc = playedUtc;
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(_path))
                return;
            var dict = JsonSerializer.Deserialize<Dictionary<string, PlaytimeRecord>>(File.ReadAllText(_path));
            if (dict is null)
                return;
            _records.Clear();
            foreach (var kv in dict)
                _records[kv.Key] = kv.Value;
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
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_records, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // ignore
        }
    }
}
