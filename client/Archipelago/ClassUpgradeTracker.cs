using System.Linq;

namespace StarshipTitanicAp;

/// <summary>
/// Interprets received "Progressive Passenger Class Upgrade" items into
/// the engine's PassengerClass value - driven entirely by the apworld's
/// own slot_data (fill_slot_data() in __init__.py), not hardcoded here:
///   - progressive_class_upgrade_item: the item's display name
///   - second_class_tier / first_class_tier: how many copies of that item
///     need to have been received to reach Second/First class
///
/// Takes plain item-name strings (see
/// ArchipelagoConnection.GetReceivedItemNames) rather than talking to the
/// AP session/library types directly, since ItemInfo.ItemName is already
/// resolved for us there - no need for this class to know anything about
/// the AP library's types at all.
/// </summary>
public static class ClassUpgradeTracker
{
    /// <summary>
    /// Computes the PassengerClass value implied by the items received so
    /// far (engine enum: 1=First, 2=Second, 3=Third, 4=None), or null if
    /// either slot data doesn't have what's needed to compute it (e.g.
    /// connected to a different/older world version) or not enough
    /// upgrade items have been received yet to warrant a change. Callers
    /// should leave the in-game class alone whenever this returns null -
    /// null is not "downgrade to nothing", it's "nothing to do".
    /// </summary>
    public static int? ComputeClass(IReadOnlyCollection<string> receivedItemNames, IReadOnlyDictionary<string, object> slotData)
    {
        if (!TryGetString(slotData, "progressive_class_upgrade_item", out string itemName))
            return null;
        if (!TryGetInt(slotData, "second_class_tier", out int secondTier))
            return null;
        if (!TryGetInt(slotData, "first_class_tier", out int firstTier))
            return null;

        int count = receivedItemNames.Count(n => string.Equals(n, itemName, StringComparison.Ordinal));

        if (count >= firstTier)
            return (int)PassengerClass.First;
        if (count >= secondTier)
            return (int)PassengerClass.Second;

        return null; // hasn't received enough yet
    }

    private static bool TryGetString(IReadOnlyDictionary<string, object> data, string key, out string value)
    {
        value = "";
        if (data.TryGetValue(key, out object? raw) && raw is string s)
        {
            value = s;
            return true;
        }
        return false;
    }

    private static bool TryGetInt(IReadOnlyDictionary<string, object> data, string key, out int value)
    {
        value = 0;
        if (data.TryGetValue(key, out object? raw))
        {
            try
            {
                value = Convert.ToInt32(raw);
                return true;
            }
            catch
            {
                // fall through - malformed slot data, treat as missing
            }
        }
        return false;
    }
}
