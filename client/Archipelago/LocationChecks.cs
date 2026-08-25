namespace StarshipTitanicAp;

/// <summary>
/// Maps this app's RoomNames (internal engine room names, read from game
/// memory) to the "&lt;Region&gt; - Arrive for the First Time" location IDs
/// defined in the starship_titanic .apworld (locations.py, offsets
/// 200-218, one per AP region). Several engine rooms map to the same
/// region on purpose - e.g. all SGT Class Floor sub-rooms - matching the
/// world's "regions are coarser than physical rooms" design (see
/// regions.py).
///
/// A handful of engine rooms are deliberately left unmapped rather than
/// guessed - sending a wrong/premature check is worse than sending none.
/// See the NOTE below for what's missing and why.
/// </summary>
public static class LocationChecks
{
    private const long LocationIdBase = 771901000;

    // engine room name (RoomNames.cs) -> AP location id offset (locations.py)
    private static readonly Dictionary<string, long> RoomToLocationOffset = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EmbLobby"] = 200,
        ["MoonEmbLobby"] = 200,           // best-effort: presumed same physical lobby, later game state - verify
        ["TopOfWell"] = 201,
        ["ParrotLobby"] = 202,
        ["BilgeRoom"] = 203,
        ["BilgeRoomWith"] = 203,
        ["Titania"] = 204,
        ["CreatorsChamber"] = 205,
        ["CreatorsChamberOn"] = 205,
        ["SculptureChamber"] = 206,
        ["SgtLobby"] = 207,
        ["SGTLittleLift"] = 207,
        ["SGTLeisure"] = 207,
        ["SGTState"] = 207,
        ["secClassState"] = 208,
        ["2ndClassLobby"] = 208,
        ["SecClassLittleLift"] = 208,
        ["BottomOfWell"] = 209,
        ["Lift"] = 210,                   // best-effort: presumed "Broken Elevator" - verify
        ["1stClassState"] = 211,
        ["1stClassLobby"] = 211,
        ["PromenadeDeck"] = 213,
        ["Arboretum"] = 214,
        ["FrozenArboretum"] = 214,
        ["Bar"] = 215,
        ["MusicRoom"] = 216,
        ["MusicRoomLobby"] = 216,
        ["1stClassRestaurant"] = 217,
        ["Bridge"] = 218,

        // NOTE - deliberately NOT mapped, no confident match from room
        // names alone (cross-check against RoomNames.cs's full list):
        //   "Chevron Room" (AP region offset 212) has no obvious dedicated
        //     entry in RoomNames.cs - the chevron puzzle is likely reached
        //     by dialing a specific floor/elevator/room code into one of
        //     the generic state rooms (secClassState/1stClassState) rather
        //     than being its own distinct room id, so there's nothing
        //     reliable to key off yet.
        //   ServiceElevator, Pellerator, Dome, Canal, Home, TheEnd,
        //     "TestRoom - Adam", HiddenRoom, "Please delete yarda yarda",
        //     "Cheat Room" - either dev/cut-content rooms or ones with no
        //     obvious matching AP region.
        // Fill these in (and double check the "best-effort" ones above)
        // once confirmed against actual gameplay.
    };

    /// <summary>
    /// Looks up the AP location id for a room's "Arrive for the First
    /// Time" check. Returns false for rooms with no known mapping (see
    /// the NOTE above) - callers should just skip sending anything in
    /// that case rather than guess.
    /// </summary>
    public static bool TryGetLocationId(string roomName, out long locationId)
    {
        if (RoomToLocationOffset.TryGetValue(roomName, out long offset))
        {
            locationId = LocationIdBase + offset;
            return true;
        }

        locationId = 0;
        return false;
    }
}
