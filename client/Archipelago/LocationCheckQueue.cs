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
    /// Attempts to flush every queued check through the given send
    /// function. Only checks that actually succeed are removed - anything
    /// that fails stays queued for the next attempt. Stops at the first
    /// failure rather than churning through the whole batch against a
    /// connection that's likely already gone again. Returns how many were
    /// successfully sent.
    /// </summary>
    public int Flush(Func<long, bool> send)
    {
        lock (_lock)
        {
            if (_pending.Count == 0)
                return 0;

            var sent = new List<long>();
            foreach (long id in _pending)
            {
                if (!send(id))
                    break;
                sent.Add(id);
            }

            if (sent.Count > 0)
            {
                foreach (long id in sent)
                    _pending.Remove(id);
                Save();
            }

            return sent.Count;
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
