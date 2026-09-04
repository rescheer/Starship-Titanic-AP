using System.Linq;

namespace StarshipTitanicAp;

/// <summary>Interprets received "Progressive Passenger Class Upgrade" items into the engine's PassengerClass value.</summary>
public static class ClassUpgradeTracker
{
    /// <summary>Computes the PassengerClass value implied by the items received so far, or null if it can't be determined.</summary>
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
