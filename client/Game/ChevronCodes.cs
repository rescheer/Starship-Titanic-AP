namespace StarshipTitanicAp;

/// <summary>Maps room names to their fixed SuccUBus/transport room-flags codes.</summary>
public static class ChevronCodes
{
    // Room names that are actually SuccUBus mail stations (per the fixed RoomNodeView
    // list in LocationChecks.cs). Rooms present in Codes below but NOT in this set
    // have a known fixed room-flags code (useful for displaying where an item sits)
    // but are not a place mail can be sent to/from.
    private static readonly HashSet<string> MailStationNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ParrotLobby", "SculptureChamber", "Bar", "EmbLobby", "MoonEmbLobby",
        "MusicRoom", "MusicRoomLobby", "BottomOfWell", "Arboretum",
        "PromenadeDeck", "1stClassRestaurant", "CreatorsChamber", "CreatorsChamberOn",
        "BilgeRoom", "BilgeRoomWith", "Titania",
    };

    // Mail stations that exist in these rooms too, but whose room-flags code is a
    // per-player dynamically computed value (see RoomFlags.IsNamedRoom) rather than
    // a fixed constant - there is no single hex code to put in Codes below. Their
    // code can only be read live off CPetControl while the player is standing there
    // (GameState.ReadCurrentRoomFlags), so TryGetCode needs that value passed in.
    //
    // SgtLobby is here (not in MailStationNames) because each player's SGT Lobby lives
    // on their own assigned floor: a live capture there decoded via RoomFlags.Decode()
    // to (elevator 1, class 3, floor 30, room 0) - the per-stateroom Compute() shape,
    // not one of the fixed named-room constants (which all have their low bit set).
    private static readonly HashSet<string> DynamicMailStationNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "secClassState", "2ndClassLobby", "1stClassState", "1stClassLobby", "SgtLobby",
    };

    private static readonly Dictionary<string, uint> Codes = new(StringComparer.OrdinalIgnoreCase)
    {
        // --- SuccUBus (mail station) rooms ---
        ["ParrotLobby"] = 0x1D0D9,           // Third Class
        ["SculptureChamber"] = 0x465FB,       // Second Class
        ["Bar"] = 0xB3D97,                    // Second Class
        ["EmbLobby"] = 0xCC971,               // Third Class
        ["MoonEmbLobby"] = 0xCC971,           // Third Class
        ["MusicRoom"] = 0xF34DB,              // Second Class
        ["MusicRoomLobby"] = 0xF34DB,         // Second Class
        ["Titania"] = 0x8A397,                // Third Class
        ["BottomOfWell"] = 0x59FAD,           // Third Class
        ["Arboretum"] = 0x4D6AF,              // First Class
        ["PromenadeDeck"] = 0x79C45,          // Second Class
        ["1stClassRestaurant"] = 0x896B9,     // First Class
        ["CreatorsChamber"] = 0x2F86D,        // Second Class
        ["CreatorsChamberOn"] = 0x2F86D,      // Second Class
        ["BilgeRoom"] = 0x3D94B,              // Third Class
        ["BilgeRoomWith"] = 0x3D94B,          // Third Class
        ["Bridge"] = 0x39FCB,                 // Third Class

        // --- Transport rooms (not mail stations) ---
        ["TopOfWell"] = 0xDF4D1,
        ["Pellerator"] = 0xC95E9,
        ["Dome"] = 0xAD171,
        ["Lift"] = 0x96E45,
        ["SGTLeisure"] = 0x5D3AD,
        ["ServiceElevator"] = 0x68797,
    };

    private static readonly Dictionary<uint, string> ReverseCodes =
        Codes.GroupBy(kv => kv.Value).ToDictionary(g => g.Key, g => g.First().Key);

    public static bool HasStation(string roomName) =>
        MailStationNames.Contains(roomName) || DynamicMailStationNames.Contains(roomName);

    /// <summary>
    /// Resolves the mail-station code for a room. For dynamic rooms (staterooms/their
    /// lobbies), pass the live room-flags value read off CPetControl while the player is
    /// standing in that room (GameState.ReadCurrentRoomFlags) - there's no fixed code for those.
    /// </summary>
    public static bool TryGetCode(string roomName, uint? liveRoomFlags, out uint code)
    {
        if (MailStationNames.Contains(roomName))
            return Codes.TryGetValue(roomName, out code);

        if (DynamicMailStationNames.Contains(roomName) && liveRoomFlags is not null)
        {
            code = liveRoomFlags.Value;
            return true;
        }

        code = 0;
        return false;
    }

    public static bool TryGetCode(string roomName, out uint code) => TryGetCode(roomName, null, out code);

    /// <summary>Returns the room name for a room-flags value, or null if not found.</summary>
    public static string? TryGetRoomName(uint code) =>
        ReverseCodes.TryGetValue(code, out string? name) ? name : null;
}
