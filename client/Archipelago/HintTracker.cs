using Archipelago.MultiClient.Net.Enums;

namespace StarshipTitanicAp;

/// <summary>One of the slot's hints, with its names resolved.</summary>
/// <param name="ReceiverIsThisSlot">Whether the item is for this slot, including through an item link.</param>
/// <param name="FinderIsThisSlot">Whether the item is in one of this slot's checks.</param>
public sealed record HintInfo(
    string ItemName, string ReceivingSlot, bool ReceiverIsThisSlot,
    string LocationName, string FindingSlot, bool FinderIsThisSlot,
    int FindingPlayer, long LocationId, ItemFlags Flags, bool Found, string Entrance, HintStatus Status);

/// <summary>The slot's hints in both directions, found ones included, and the requests the server hasn't answered yet.
/// What to leave out is up to whatever displays them.</summary>
public sealed class HintTracker
{
    /// <summary>A refused request only comes back as text, so one that hasn't produced a hint by now is taken to have failed.</summary>
    private const long RequestTimeoutMs = 5000;

    private readonly object _lock = new();
    private HintInfo[] _all = Array.Empty<HintInfo>();
    private Dictionary<string, HintInfo[]> _unfoundByItem = new(StringComparer.Ordinal);
    private Dictionary<string, HintInfo> _byLocation = new(StringComparer.Ordinal);
    private readonly List<(string Item, long Expires)> _itemRequests = new();
    private readonly Dictionary<string, long> _locationRequests = new(StringComparer.Ordinal);

    /// <summary>Changes whenever anything here does, for callers caching what they've worked out from it.</summary>
    public int Revision { get; private set; }

    public HintInfo[] All
    {
        get
        {
            lock (_lock)
                return _all;
        }
    }

    /// <summary>The unfound hints for this slot's copies of an item.</summary>
    public HintInfo[] HintsFor(string item)
    {
        lock (_lock)
            return _unfoundByItem.GetValueOrDefault(item, Array.Empty<HintInfo>());
    }

    /// <summary>The hint on one of this slot's checks. A location holds one item, so there's at most one.</summary>
    public HintInfo? HintAt(string location)
    {
        lock (_lock)
            return _byLocation.GetValueOrDefault(location);
    }

    /// <summary>How many copies of an item are accounted for by unfound hints and unanswered requests.</summary>
    public int CopiesCovered(string item)
    {
        lock (_lock)
            return _unfoundByItem.GetValueOrDefault(item, Array.Empty<HintInfo>()).Length + _itemRequests.Count(r => r.Item == item);
    }

    /// <summary>Whether a request for this location's contents is unanswered.</summary>
    public bool IsRequested(string location)
    {
        lock (_lock)
            return _locationRequests.ContainsKey(location);
    }

    public void AddItemRequest(string item)
    {
        lock (_lock)
        {
            _itemRequests.Add((item, Environment.TickCount64 + RequestTimeoutMs));
            Revision++;
        }
    }

    public void AddLocationRequest(string location)
    {
        lock (_lock)
        {
            _locationRequests[location] = Environment.TickCount64 + RequestTimeoutMs;
            Revision++;
        }
    }

    /// <summary>Replaces the list with the server's latest, which answers any requests it has new hints for.</summary>
    public void Update(HintInfo[] hints)
    {
        lock (_lock)
        {
            Dictionary<string, HintInfo[]> unfoundByItem = hints
                .Where(hint => hint.ReceiverIsThisSlot && !hint.Found)
                .GroupBy(hint => hint.ItemName, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var byLocation = new Dictionary<string, HintInfo>(StringComparer.Ordinal);
            foreach (HintInfo hint in hints.Where(hint => hint.FinderIsThisSlot))
                byLocation[hint.LocationName] = hint;

            foreach ((string item, HintInfo[] unfound) in unfoundByItem)
            {
                int answered = unfound.Length - _unfoundByItem.GetValueOrDefault(item, Array.Empty<HintInfo>()).Length;
                for (int i = _itemRequests.Count - 1; i >= 0 && answered > 0; i--)
                {
                    if (_itemRequests[i].Item != item)
                        continue;
                    _itemRequests.RemoveAt(i);
                    answered--;
                }
            }
            foreach (string location in _locationRequests.Keys.Where(byLocation.ContainsKey).ToArray())
                _locationRequests.Remove(location);

            _all = hints;
            _unfoundByItem = unfoundByItem;
            _byLocation = byLocation;
            Revision++;
        }
    }

    /// <summary>Gives up on requests that have waited too long. True if there were any.</summary>
    public bool ExpireRequests()
    {
        lock (_lock)
        {
            long now = Environment.TickCount64;
            int removed = _itemRequests.RemoveAll(request => request.Expires <= now);
            foreach (string location in _locationRequests.Where(request => request.Value <= now).Select(request => request.Key).ToArray())
            {
                _locationRequests.Remove(location);
                removed++;
            }

            if (removed == 0)
                return false;
            Revision++;
            return true;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _all = Array.Empty<HintInfo>();
            _unfoundByItem = new(StringComparer.Ordinal);
            _byLocation = new(StringComparer.Ordinal);
            _itemRequests.Clear();
            _locationRequests.Clear();
            Revision++;
        }
    }
}
