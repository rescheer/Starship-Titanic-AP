using Newtonsoft.Json.Linq;

namespace StarshipTitanicAp;

/// <summary>A slot's access rules, exported into its slot data at generation (apworld/starship_titanic/logic_export.py).
/// Answers what's reachable with a set of received items, the same way Archipelago itself would. Seeds generated before
/// the export was added don't have it.</summary>
public sealed class SlotLogic
{
    private const string StartRegion = "Menu";

    /// <summary>What a rule can see: the items collected so far and the regions reached so far.</summary>
    private sealed class State
    {
        public readonly Dictionary<string, int> Items = new(StringComparer.Ordinal);
        public readonly HashSet<string> Regions = new(StringComparer.Ordinal) { StartRegion };

        public void Collect(string item) => Items[item] = Items.GetValueOrDefault(item) + 1;

        public bool Has(string item, int count) => Items.GetValueOrDefault(item) >= count;
    }

    private delegate bool Rule(State state);

    private readonly record struct Entrance(string From, string To, Rule Rule);
    private readonly record struct Spot(string Name, string Region, Rule Rule);

    private readonly Entrance[] _entrances;
    private readonly Spot[] _locations;
    private readonly Spot[] _events; // Name is the event's item

    private SlotLogic(Entrance[] entrances, Spot[] locations, Spot[] events)
    {
        _entrances = entrances;
        _locations = locations;
        _events = events;
    }

    /// <summary>The slot's logic, or null for older seeds and malformed slot data.</summary>
    public static SlotLogic? TryRead(IReadOnlyDictionary<string, object>? slotData)
    {
        if (slotData is null || !slotData.TryGetValue("logic", out object? raw) || raw is not JObject logic)
            return null;

        try
        {
            return new SlotLogic(
                logic["entrances"]!.Select(e => new Entrance((string)e[0]!, (string)e[1]!, ParseRule(e[2]!))).ToArray(),
                ParseSpots(logic["locations"]!),
                ParseSpots(logic["events"]!));
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or ArgumentException or NullReferenceException)
        {
            return null;
        }
    }

    /// <summary>The names of the slot's locations reachable with these items, checked or not. Pass duplicates for
    /// items received more than once.</summary>
    public HashSet<string> GetReachableLocations(IEnumerable<string> receivedItems)
    {
        var state = new State();
        foreach (string item in receivedItems)
            state.Collect(item);

        // Reaching a region or collecting an event can open up more of either, so repeat until nothing changes
        var pendingEvents = new List<Spot>(_events);
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (Entrance entrance in _entrances)
            {
                if (!state.Regions.Contains(entrance.To) && state.Regions.Contains(entrance.From) && entrance.Rule(state))
                {
                    state.Regions.Add(entrance.To);
                    changed = true;
                }
            }
            for (int i = pendingEvents.Count - 1; i >= 0; i--)
            {
                Spot spot = pendingEvents[i];
                if (state.Regions.Contains(spot.Region) && spot.Rule(state))
                {
                    state.Collect(spot.Name);
                    pendingEvents.RemoveAt(i);
                    changed = true;
                }
            }
        }

        var reachable = new HashSet<string>(StringComparer.Ordinal);
        foreach (Spot location in _locations)
        {
            if (state.Regions.Contains(location.Region) && location.Rule(state))
                reachable.Add(location.Name);
        }
        return reachable;
    }

    private static Spot[] ParseSpots(JToken spots) =>
        spots.Select(s => new Spot((string)s[0]!, (string)s[1]!, ParseRule(s[2]!))).ToArray();

    private static Rule ParseRule(JToken token)
    {
        if (token.Type == JTokenType.Boolean)
        {
            bool value = (bool)token;
            return _ => value;
        }

        string kind = (string)token[0]!;
        switch (kind)
        {
            case "has":
                string item = (string)token[1]!;
                int count = (int)token[2]!;
                return state => state.Has(item, count);
            case "all":
                string[] items = token[1]!.Select(i => (string)i!).ToArray();
                return state => items.All(i => state.Has(i, 1));
            case "region":
                string region = (string)token[1]!;
                return state => state.Regions.Contains(region);
            case "and":
                Rule[] allOf = token[1]!.Select(ParseRule).ToArray();
                return state => allOf.All(rule => rule(state));
            case "or":
                Rule[] anyOf = token[1]!.Select(ParseRule).ToArray();
                return state => anyOf.Any(rule => rule(state));
            default:
                throw new FormatException($"Unknown rule \"{kind}\"");
        }
    }
}
