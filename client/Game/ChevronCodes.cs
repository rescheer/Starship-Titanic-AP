namespace StarshipTitanicAp;

/// <summary>
/// Chevron codes for the 13 SuccUBus-equipped rooms, taken directly from
/// CChevCode::GetChevCodeFromRoomNameMsg. These are the exact values
/// CGameObject::_roomFlags/_destRoomFlags use for mail routing.
///
/// Mapped from the engine's internal short room names to the display
/// names this app's RoomNames table uses. Three mappings are a
/// best-effort guess where our room list has more than one similarly-
/// named room (CreatorsChamber vs CreatorsChamberOn, BilgeRoom vs
/// BilgeRoomWith, FCRestrnt vs "1stClassRestaurant") - these are marked
/// below. If retargeting mail fails oddly in one of those specific
/// rooms, this mapping is the first thing to re-check.
/// </summary>
public static class ChevronCodes
{
    private static readonly Dictionary<string, uint> Codes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ParrotLobby"] = 0x1D0D9,
        ["SculptureChamber"] = 0x465FB,
        ["Bar"] = 0xB3D97,
        ["EmbLobby"] = 0xCC971,
        ["MusicRoom"] = 0xF34DB,
        ["Titania"] = 0x8A397,
        ["BottomOfWell"] = 0x59FAD,
        ["Arboretum"] = 0x4D6AF,
        ["PromenadeDeck"] = 0x79C45,
        ["1stClassRestaurant"] = 0x196D9,  // engine name "FCRestrnt" - best-effort mapping
        ["CreatorsChamber"] = 0x2F86D,     // engine name "CrtrsCham" - best-effort mapping (not CreatorsChamberOn)
        ["BilgeRoom"] = 0x3D94B,           // best-effort mapping (not BilgeRoomWith)
        ["Bridge"] = 0x39FCB,
    };

    private static readonly Dictionary<uint, string> ReverseCodes =
        Codes.GroupBy(kv => kv.Value).ToDictionary(g => g.Key, g => g.First().Key);

    public static bool HasStation(string roomName) => Codes.ContainsKey(roomName);

    public static bool TryGetCode(string roomName, out uint code) => Codes.TryGetValue(roomName, out code);

    /// <summary>Returns the room name for a chevron code, or null if it's not one of the 13 known stations.</summary>
    public static string? TryGetRoomName(uint code) =>
        ReverseCodes.TryGetValue(code, out string? name) ? name : null;
}
