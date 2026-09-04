using System.Text.Json;

namespace StarshipTitanicAp;

/// <summary>A single queued-but-unsent location check, paired with the AP seed_name it was queued under.</summary>
public readonly record struct PendingCheck(string LocationName, string? SeedName);

/// <summary>Queues location checks that couldn't be sent and retries them whenever a connection becomes available.</summary>
public sealed class LocationCheckQueue
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "StarshipTitanicAp",
        "pending_checks.json");

    private readonly object _lock = new();
    private readonly Dictionary<string, string?> _pending;

    public LocationCheckQueue()
    {
        _pending = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (PendingCheck check in Load())
            _pending[check.LocationName] = check.SeedName;
    }

    public int Count
    {
        get { lock (_lock) return _pending.Count; }
    }

    /// <summary>Adds a location name to the queue if it isn't already there and persists immediately.</summary>
    public bool Enqueue(string locationName, string? seedName)
    {
        lock (_lock)
        {
            if (_pending.ContainsKey(locationName))
                return false;
            _pending[locationName] = seedName;
            Save();
            return true;
        }
    }

    /// <summary>Point-in-time snapshot of every queued check.</summary>
    public PendingCheck[] Snapshot()
    {
        lock (_lock)
        {
            var result = new PendingCheck[_pending.Count];
            int i = 0;
            foreach (KeyValuePair<string, string?> kvp in _pending)
                result[i++] = new PendingCheck(kvp.Key, kvp.Value);
            return result;
        }
    }

    /// <summary>Removes a single name once its send is confirmed successful.</summary>
    public void Remove(string locationName)
    {
        lock (_lock)
        {
            if (_pending.Remove(locationName))
                Save();
        }
    }

    /// <summary>Drops every queued check without sending it.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            if (_pending.Count == 0)
                return;
            _pending.Clear();
            Save();
        }
    }

    private static List<PendingCheck> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new List<PendingCheck>();

            string json = File.ReadAllText(FilePath);
            Dictionary<string, string?>? dict = JsonSerializer.Deserialize<Dictionary<string, string?>>(json);
            if (dict is null)
                return new List<PendingCheck>();

            var result = new List<PendingCheck>(dict.Count);
            foreach (KeyValuePair<string, string?> kvp in dict)
                result.Add(new PendingCheck(kvp.Key, kvp.Value));
            return result;
        }
        catch
        {
            return new List<PendingCheck>();
        }
    }

    private void Save()
    {
        try
        {
            string? dir = Path.GetDirectoryName(FilePath);
            if (dir is not null)
                Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(_pending);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Best-effort.
        }
    }
}
