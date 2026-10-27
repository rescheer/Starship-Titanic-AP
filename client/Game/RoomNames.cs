namespace StarshipTitanicAp;

/// <summary>Maps room IDs to their display names.</summary>
public static class RoomNames
{
    private static readonly Dictionary<int, string> Names = new()
    {
        // Unused rooms commented out
        [2] = "EmbLobby",
        [3] = "Home",
        [5] = "MoonEmbLobby",
        [6] = "secClassState",
        [7] = "1stClassState",
        [9] = "ParrotLobby",
        [11] = "SgtLobby",
        [12] = "MusicRoom",
        [16] = "PromenadeDeck",
        [18] = "TheEnd",
        [20] = "1stClassLobby",
        [21] = "Lift",
        [22] = "SGTLittleLift",
        [24] = "SGTLeisure",
        [27] = "SGTState",
        [30] = "ServiceElevator",
        [31] = "Bar",
        [33] = "Pellerator",
        [34] = "2ndClassLobby",
        [36] = "TopOfWell",
        [37] = "Titania",
        [38] = "BottomOfWell",
        [39] = "CreatorsChamber",
        // [40] = "TestRoom - Adam",
        [42] = "CreatorsChamberOn",
        [43] = "Bridge",
        [44] = "SculptureChamber",
        [45] = "BilgeRoom",
        [46] = "Dome",
        [47] = "BilgeRoomWith",
        [48] = "Arboretum",
        [49] = "1stClassRestaurant",
        [50] = "HiddenRoom",
        // [51] = "Please delete yarda yarda",
        [52] = "FrozenArboretum",
        [53] = "MusicRoomLobby",
        [55] = "SecClassLittleLift",
        [56] = "Cheat Room",
        // [57] = "Canal",
    };

    /// <summary>Returns the room's display name, or a fallback string if the ID isn't recognized.</summary>
    public static string GetName(int roomId) =>
        Names.TryGetValue(roomId, out string? name) ? name : $"(unknown room {roomId})";

    // Rooms that are two states of one location - the game itself gives each pair one location ID
    // (CRoomItem::getSoundId) and moves the player between them by script (CreatorsChamber's lever cutscene,
    // the Arboretum's season change, the Bilge succubus's sneeze).
    private static readonly int[][] Variants =
    {
        new[] { 2, 5 },   // EmbLobby, MoonEmbLobby
        new[] { 39, 42 }, // CreatorsChamber, CreatorsChamberOn
        new[] { 48, 52 }, // Arboretum, FrozenArboretum
        new[] { 45, 47 }, // BilgeRoom, BilgeRoomWith
    };

    /// <summary>Returns every state of the room's location, including the room itself.</summary>
    public static IReadOnlyList<int> GetVariants(int roomId) =>
        Array.Find(Variants, group => group.Contains(roomId)) ?? new[] { roomId };

    /// <summary>Looks up a room ID by its name (case-insensitive).</summary>
    public static bool TryGetRoomId(string name, out int roomId)
    {
        foreach ((int id, string roomName) in Names)
        {
            if (string.Equals(roomName, name, StringComparison.OrdinalIgnoreCase))
            {
                roomId = id;
                return true;
            }
        }

        roomId = 0;
        return false;
    }
}
