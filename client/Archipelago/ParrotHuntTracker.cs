using System.Linq;
using Newtonsoft.Json.Linq;

namespace StarshipTitanicAp;

/// <summary>The Parrot Hunt goal's slot data: the stateroom the parrot was mailed to, and which part of its address
/// each clue reveals at which Progressive Clue count.</summary>
/// <param name="ClueOrder">"elevator", "floor" or "room", in the order the clues reveal them.</param>
/// <param name="Thresholds">The Progressive Clue count each clue in <paramref name="ClueOrder"/> is revealed at. The
/// Progressive Clues in between only give flavor text (see <see cref="ParrotHuntClueText"/>), which players aren't told.</param>
/// <param name="ClassIsFixed">Whether the seed's options only allowed one class, so the class isn't a secret.</param>
public sealed record ParrotHuntInfo(
    string ClueItemName, int Elevator, int Floor, int Room, int PassengerClass, bool ClassIsFixed,
    string[] ClueOrder, int[] Thresholds)
{
    /// <summary>The target stateroom's room flags, as the PET reports them while standing in it.</summary>
    public uint TargetRoomFlags => RoomFlags.Encode(Elevator, PassengerClass, Floor, Room);

    /// <summary>The engine room (see <see cref="RoomNames"/>) every stateroom of the target's class shares.</summary>
    public string TargetRoomName => PassengerClass == (int)StarshipTitanicAp.PassengerClass.First ? "1stClassState" : "secClassState";

    public int CountClues(IEnumerable<string> receivedItemNames) =>
        receivedItemNames.Count(n => string.Equals(n, ClueItemName, StringComparison.Ordinal));

    /// <summary>Whether the clue for <paramref name="part"/> has been revealed with <paramref name="clues"/> Progressive Clues.</summary>
    public bool IsRevealed(string part, int clues)
    {
        int index = Array.IndexOf(ClueOrder, part);
        return index >= 0 && index < Thresholds.Length && clues >= Thresholds[index];
    }

    /// <summary>The part revealed on reaching exactly <paramref name="clues"/> Progressive Clues, if any.</summary>
    public string? PartRevealedAt(int clues)
    {
        int index = Array.IndexOf(Thresholds, clues);
        return index >= 0 && index < ClueOrder.Length ? ClueOrder[index] : null;
    }

    public string ClassName => PassengerClass == (int)StarshipTitanicAp.PassengerClass.First ? "1st Class" : "2nd Class";

    /// <summary>One line summing up the clues so far, e.g. "2nd Class, Elevator ?, Floor 23, Room 2".</summary>
    public string DescribeProgress(int clues)
    {
        string Part(string part, string label, int value) => $"{label} {(IsRevealed(part, clues) ? value.ToString() : "?")}";
        string classText = ClassIsFixed || IsRevealed("floor", clues) ? $"{ClassName}, " : "";
        return $"{classText}{Part("elevator", "Elevator", Elevator)}, {Part("floor", "Floor", Floor)}, {Part("room", "Room", Room)}";
    }
}

/// <summary>Reads the Parrot Hunt goal's slot data (see apworld/starship_titanic/parrot_hunt.py).</summary>
public static class ParrotHuntTracker
{
    /// <summary>Locations the apworld leaves out under this goal: each needs the parrot, which stays in the mail.</summary>
    public static readonly IReadOnlySet<string> RemovedLocations = new HashSet<string>(StringComparer.Ordinal)
    {
        "Parrot Lobby - Feather",
        "Parrot Lobby - Titania's Core",
        "2nd Class Stateroom - Titania's Ear (Pistachio Bowl)",
    };

    /// <summary>Whether <paramref name="locationName"/> isn't in this seed because its goal is Parrot Hunt.</summary>
    public static bool IsRemovedLocation(IReadOnlyDictionary<string, object>? slotData, string locationName) =>
        RemovedLocations.Contains(locationName) && LocationChecks.IsParrotHuntGoal(slotData);

    /// <summary>The seed's Parrot Hunt data, or null for other goals or malformed slot data.</summary>
    public static ParrotHuntInfo? TryRead(IReadOnlyDictionary<string, object>? slotData)
    {
        if (!LocationChecks.IsParrotHuntGoal(slotData))
            return null;

        try
        {
            if (slotData!["parrot_room"] is not JObject room)
                return null;
            string[] clueOrder = ((IEnumerable<object>)slotData["clue_order"]).Select(o => o.ToString()!).ToArray();
            int[] thresholds = ((IEnumerable<object>)slotData["clue_thresholds"]).Select(o => Convert.ToInt32(o)).ToArray();

            return new ParrotHuntInfo(
                (string)slotData["clue_item"],
                (int)room["elevator"]!,
                (int)room["floor"]!,
                (int)room["room"]!,
                (int)room["class"]!,
                !string.Equals(slotData["parrot_hunt_stateroom_class"] as string, "either", StringComparison.Ordinal),
                clueOrder,
                thresholds);
        }
        catch
        {
            return null; // missing or malformed keys
        }
    }
}
