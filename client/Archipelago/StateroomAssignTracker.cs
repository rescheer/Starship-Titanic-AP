using System.Linq;

namespace StarshipTitanicAp;

/// <summary>Interprets received "Progressive Stateroom" items into the number of stateroom classes the player
/// should have been assigned so far (0=none, 1=SGT/Third, 2=Second, 3=First) - the same scale as
/// GameActions.GetAchievedStateroomClass, so MainForm.GameLogic.cs's sync loop can diff the two directly.</summary>
public static class StateroomAssignTracker
{
    /// <summary>Computes the assigned-stateroom count implied by the items received so far, or null if it can't
    /// be determined (slot data not loaded yet, or missing the expected keys).</summary>
    public static int? ComputeTargetAssignedCount(IReadOnlyCollection<string> receivedItemNames, IReadOnlyDictionary<string, object> slotData)
    {
        if (!TryGetString(slotData, "progressive_stateroom_item", out string itemName))
            return null;
        if (!TryGetInt(slotData, "sgt_stateroom_tier", out int sgtTier))
            return null;
        if (!TryGetInt(slotData, "second_stateroom_tier", out int secondTier))
            return null;
        if (!TryGetInt(slotData, "first_stateroom_tier", out int firstTier))
            return null;

        int count = receivedItemNames.Count(n => string.Equals(n, itemName, StringComparison.Ordinal));

        if (count >= firstTier)
            return 3;
        if (count >= secondTier)
            return 2;
        if (count >= sgtTier)
            return 1;

        return 0;
    }

    /// <summary>Maps an assigned-stateroom count (1-3) to the raw PassengerClass value petReassignRoom() expects
    /// for that stateroom (3=Third/SGT is assigned first, then 2=Second, then 1=First). Null outside 1-3.</summary>
    public static int? ClassForAssignedCount(int count) => count switch
    {
        1 => (int)PassengerClass.Third,
        2 => (int)PassengerClass.Second,
        3 => (int)PassengerClass.First,
        _ => null,
    };

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
