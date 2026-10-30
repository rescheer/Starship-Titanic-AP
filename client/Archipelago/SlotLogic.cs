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

    private abstract record Rule
    {
        public sealed record Constant(bool Value) : Rule;
        public sealed record Has(string Item, int Count) : Rule;
        public sealed record Region(string Name) : Rule;
        public sealed record AllOf(Rule[] Rules) : Rule;
        public sealed record AnyOf(Rule[] Rules) : Rule;
    }

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

    /// <summary>Where a set of received items leaves the slot's locations, checked or not.</summary>
    /// <param name="Reachable">The names of the locations reachable with these items.</param>
    /// <param name="MissingItems">For the rest, what's still needed, described for the player (progressive class and
    /// stateroom items as the tier they lead to). Events are followed through to the items behind them, and where a rule
    /// has alternatives this is the one needing the fewest items. Empty if nothing can open it.</param>
    public sealed record Evaluation(HashSet<string> Reachable, Dictionary<string, string[]> MissingItems);

    /// <summary>Works out an <see cref="Evaluation"/>. Pass duplicates for items received more than once.</summary>
    public Evaluation Evaluate(IEnumerable<string> receivedItems)
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
                if (!state.Regions.Contains(entrance.To) && state.Regions.Contains(entrance.From) && IsMet(entrance.Rule, state))
                {
                    state.Regions.Add(entrance.To);
                    changed = true;
                }
            }
            for (int i = pendingEvents.Count - 1; i >= 0; i--)
            {
                Spot spot = pendingEvents[i];
                if (state.Regions.Contains(spot.Region) && IsMet(spot.Rule, state))
                {
                    state.Collect(spot.Name);
                    pendingEvents.RemoveAt(i);
                    changed = true;
                }
            }
        }

        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var missingItems = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (Spot location in _locations)
        {
            if (state.Regions.Contains(location.Region) && IsMet(location.Rule, state))
                reachable.Add(location.Name);
            else
                missingItems[location.Name] = DescribeMissing(location, state);
        }
        return new Evaluation(reachable, missingItems);
    }

    private static bool IsMet(Rule rule, State state) => rule switch
    {
        Rule.Constant constant => constant.Value,
        Rule.Has has => state.Has(has.Item, has.Count),
        Rule.Region region => state.Regions.Contains(region.Name),
        Rule.AllOf allOf => allOf.Rules.All(child => IsMet(child, state)),
        Rule.AnyOf anyOf => anyOf.Rules.Any(child => IsMet(child, state)),
        _ => false,
    };

    private string[] DescribeMissing(Spot location, State state)
    {
        Dictionary<string, int>? missing = FindMissing(InRegion(location.Region, location.Rule), state, new HashSet<string>(StringComparer.Ordinal));
        if (missing is null)
            return Array.Empty<string>();
        return missing.Select(item => DescribeItem(item.Key, item.Value, state)).ToArray();
    }

    private static string DescribeItem(string item, int count, State state) => (item, count) switch
    {
        ("Progressive Passenger Class Upgrade", 1) => "2nd Class",
        ("Progressive Passenger Class Upgrade", 2) => "1st Class",
        ("Progressive Stateroom", 1) => "Assigned SGT Stateroom",
        ("Progressive Stateroom", 2) => "Assigned 2nd Class Stateroom",
        ("Progressive Stateroom", 3) => "Assigned 1st Class Stateroom",
        _ when count > 1 => $"{item} ({state.Items.GetValueOrDefault(item)}/{count})",
        _ => item,
    };

    /// <summary>The items, with their required counts, still needed to meet a rule: none if it's met, null if no items
    /// could. <paramref name="visiting"/> holds the regions and events being followed, to stop at cycles.</summary>
    private Dictionary<string, int>? FindMissing(Rule rule, State state, HashSet<string> visiting)
    {
        switch (rule)
        {
            case Rule.Constant constant:
                return constant.Value ? new Dictionary<string, int>(StringComparer.Ordinal) : null;

            case Rule.Has has when state.Has(has.Item, has.Count):
                return new Dictionary<string, int>(StringComparer.Ordinal);

            case Rule.Has has:
                Rule[] eventRoutes = _events.Where(e => e.Name == has.Item).Select(e => InRegion(e.Region, e.Rule)).ToArray();
                if (eventRoutes.Length > 0)
                    return FindCheapestRoute($"item {has.Item}", eventRoutes, state, visiting);
                return new Dictionary<string, int>(StringComparer.Ordinal) { [has.Item] = has.Count };

            case Rule.Region region when state.Regions.Contains(region.Name):
                return new Dictionary<string, int>(StringComparer.Ordinal);

            case Rule.Region region:
                Rule[] entranceRoutes = _entrances.Where(e => e.To == region.Name).Select(e => InRegion(e.From, e.Rule)).ToArray();
                return FindCheapestRoute($"region {region.Name}", entranceRoutes, state, visiting);

            case Rule.AllOf allOf:
                var combined = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (Rule child in allOf.Rules)
                {
                    Dictionary<string, int>? missing = FindMissing(child, state, visiting);
                    if (missing is null)
                        return null;
                    foreach ((string item, int count) in missing)
                        combined[item] = Math.Max(combined.GetValueOrDefault(item), count);
                }
                return combined;

            case Rule.AnyOf anyOf:
                return anyOf.Rules
                    .Select(child => FindMissing(child, state, visiting))
                    .Where(missing => missing is not null)
                    .OrderBy(missing => missing!.Count)
                    .FirstOrDefault();

            default:
                return null;
        }
    }

    private Dictionary<string, int>? FindCheapestRoute(string visitKey, Rule[] routes, State state, HashSet<string> visiting)
    {
        if (!visiting.Add(visitKey))
            return null;
        Dictionary<string, int>? missing = FindMissing(new Rule.AnyOf(routes), state, visiting);
        visiting.Remove(visitKey);
        return missing;
    }

    private static Rule InRegion(string region, Rule rule) => new Rule.AllOf(new[] { new Rule.Region(region), rule });

    private static Spot[] ParseSpots(JToken spots) =>
        spots.Select(s => new Spot((string)s[0]!, (string)s[1]!, ParseRule(s[2]!))).ToArray();

    private static Rule ParseRule(JToken token)
    {
        if (token.Type == JTokenType.Boolean)
            return new Rule.Constant((bool)token);

        string kind = (string)token[0]!;
        return kind switch
        {
            "has" => new Rule.Has((string)token[1]!, (int)token[2]!),
            "all" => new Rule.AllOf(token[1]!.Select(item => (Rule)new Rule.Has((string)item!, 1)).ToArray()),
            "region" => new Rule.Region((string)token[1]!),
            "and" => new Rule.AllOf(token[1]!.Select(ParseRule).ToArray()),
            "or" => new Rule.AnyOf(token[1]!.Select(ParseRule).ToArray()),
            _ => throw new FormatException($"Unknown rule \"{kind}\""),
        };
    }
}
