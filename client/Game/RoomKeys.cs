namespace StarshipTitanicAp;

/// <summary>Rooms locked by RoomGateHook until their matching AP Access Card has been received - only for the
/// cards listed in the slot data's "room_access_cards" (the cards the player's YAML options put in the pool). Rooms not listed here are
/// always open - including every room the game moves the player through by script (Lift, Pellerator, the
/// mini-lifts, ServiceElevator, Home, EmbLobby, TheEnd, ...) and small rooms only reachable through a keyed one
/// (SGTLeisure, the LittleLifts). List one state of a multi-state location (e.g. CreatorsChamber) -
/// MainForm.SyncRoomGate locks every variant (RoomNames.GetVariants).</summary>
public static class RoomKeys
{
    public const string SlotDataKey = "room_access_cards";

    // AP item (items.py) -> engine room names (RoomNames.cs) it unlocks - the same rooms as each region's
    // "Visited" check in LocationChecks.cs
    private static readonly Dictionary<string, string[]> Cards = new()
    {
        ["Top of the Well Access Card"] = new[] { "TopOfWell" },
        ["Bottom of the Well Access Card"] = new[] { "BottomOfWell" },
        ["Parrot Lobby Access Card"] = new[] { "ParrotLobby" },
        ["Bilge Room Access Card"] = new[] { "BilgeRoom" },
        ["Titania's Room Access Card"] = new[] { "Titania", "Dome" },
        ["Creator's Chamber Access Card"] = new[] { "CreatorsChamber" },
        ["Sculpture Chamber Access Card"] = new[] { "SculptureChamber" },
        ["Promenade Deck Access Card"] = new[] { "PromenadeDeck" },
        ["Arboretum Access Card"] = new[] { "Arboretum" },
        ["Bar Access Card"] = new[] { "Bar" },
        ["Music Room Access Card"] = new[] { "MusicRoom", "MusicRoomLobby" }, // the Succ-U-Bus is in the Lobby
        ["1st Class Restaurant Access Card"] = new[] { "1stClassRestaurant" },
        ["Bridge Access Card"] = new[] { "Bridge" },
        ["SGT Class Staterooms Access Card"] = new[] { "SgtLobby", "SGTState" },
        ["2nd Class Staterooms Access Card"] = new[] { "2ndClassLobby", "secClassState" },
        ["1st Class Staterooms Access Card"] = new[] { "1stClassLobby", "1stClassState" },
    };

    // room ID -> the card that unlocks it, resolved from Cards once
    private static readonly Dictionary<int, string> CardForRoom = Cards
        .SelectMany(card => card.Value.Select(room => (RoomNames.TryGetRoomId(room, out int id) ? id : -1, card.Key)))
        .Where(entry => entry.Item1 >= 0)
        .ToDictionary(entry => entry.Item1, entry => entry.Key);

    /// <summary>The Access Cards the connected seed requires, or none for seeds without the slot data key.</summary>
    public static HashSet<string> GetRequiredCards(IReadOnlyDictionary<string, object>? slotData)
    {
        var cards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (slotData is not null && slotData.TryGetValue(SlotDataKey, out object? raw) && raw is IEnumerable<object> list)
        {
            foreach (object? entry in list)
            {
                if (entry?.ToString() is { Length: > 0 } card)
                    cards.Add(card);
            }
        }
        return cards;
    }

    /// <summary>Room IDs whose card is required but isn't in receivedItems yet.</summary>
    public static IEnumerable<int> GetLockedRooms(IReadOnlySet<string> requiredCards, string[] receivedItems)
    {
        foreach ((int roomId, string card) in CardForRoom)
        {
            if (requiredCards.Contains(card) && !receivedItems.Contains(card, StringComparer.OrdinalIgnoreCase))
                yield return roomId;
        }
    }

    /// <summary>Looks up the Access Card that unlocks the given room (or another state of its location).</summary>
    public static bool TryGetKeyItem(int roomId, out string keyItem)
    {
        foreach (int variant in RoomNames.GetVariants(roomId))
        {
            if (CardForRoom.TryGetValue(variant, out keyItem!))
                return true;
        }

        keyItem = "";
        return false;
    }
}
