using System.Text.Json;

namespace StarshipTitanicAp;

/// <summary>
/// Queues location checks that couldn't be sent (not connected, or the
/// send itself failed) and retries them whenever a connection becomes
/// available. Persisted to disk so a check survives the app being closed
/// and reopened while still disconnected, not just a same-run reconnect.
///
/// Location IDs are naturally deduplicated (backed by a HashSet) - AP
/// treats resending an already-completed check as a no-op anyway, so
/// there's no reason to track the same id twice even locally.
/// </summary>
public sealed class LocationCheckQueue
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "StarshipTitanicAp",
        "pending_checks.json");

    private readonly object _lock = new();
    private readonly HashSet<long> _pending;

    public LocationCheckQueue()
    {
        _pending = new HashSet<long>(Load());
    }

    public int Count
    {
        get { lock (_lock) return _pending.Count; }
    }

    /// <summary>
    /// Adds a location id to the queue if it isn't already there, and
    /// persists immediately - so a check made right before the app is
    /// killed or crashes isn't lost.
    /// </summary>
    public void Enqueue(long locationId)
    {
        lock (_lock)
        {
            if (_pending.Add(locationId))
                Save();
        }
    }

    /// <summary>
    /// Point-in-time snapshot of every queued id, for a caller that wants
    /// to attempt sending them itself (e.g. asynchronously, one at a time,
    /// off the calling thread) rather than handing this class a
    /// synchronous send callback.
    /// </summary>
    public long[] Snapshot()
    {
        lock (_lock)
            return _pending.ToArray();
    }

    /// <summary>
    /// Removes a single id once its send is confirmed successful. No-op,
    /// not an error, if it's already gone (e.g. removed by a concurrent
    /// attempt, or never queued in the first place).
    /// </summary>
    public void Remove(long locationId)
    {
        lock (_lock)
        {
            if (_pending.Remove(locationId))
                Save();
        }
    }

    /// <summary>
    /// Drops every queued check without sending it. For clearing out
    /// stale entries after a server reset during testing/development -
    /// the queue isn't scoped to a particular server/seed, so checks
    /// queued against one session will happily get replayed against
    /// whatever session is connected next, which is exactly wrong after
    /// a deliberate reset.
    /// </summary>
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

    private static List<long> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new List<long>();

            string json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<long>>(json) ?? new List<long>();
        }
        catch
        {
            return new List<long>();
        }
    }

    /// <summary>Caller must hold _lock.</summary>
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
            // Best-effort - see ConnectionSettings.cs for the same reasoning.
        }
    }
}
