namespace StarshipTitanicAp;

/// <summary>Maps this app's internal engine concepts to the AP location name strings defined in the .apworld.</summary>
public static class LocationChecks
{
    // engine room name (RoomNames.cs) -> AP location name (locations.py)
    private static readonly Dictionary<string, string> RoomToLocationName = new(StringComparer.OrdinalIgnoreCase)
    {
        // Always available
        ["EmbLobby"] = "Embarkation Lobby - Visited",
        ["MoonEmbLobby"] = "Embarkation Lobby - Visited",  // the EmbLobby before the opening credits
        ["TopOfWell"] = "Top of the Well - Visited",
        ["BottomOfWell"] = "Bottom of the Well - Visited",
        ["ParrotLobby"] = "Parrot Lobby - Visited",
        ["BilgeRoom"] = "Bilge Room - Visited",
        ["BilgeRoomWith"] = "Bilge Room - Visited",
        ["Titania"] = "Titania's Room - Visited",
        ["CreatorsChamber"] = "Creator's Chamber - Visited",
        ["CreatorsChamberOn"] = "Creator's Chamber - Visited",
        ["SculptureChamber"] = "Sculpture Chamber - Visited",
        // SGT Class
        ["SgtLobby"] = "SGT Class Lobby - Visited",
        ["SGTLittleLift"] = "SGT Class Lobby - Visited",
        ["SGTLeisure"] = "SGT Class Lobby - Visited",
        ["SGTState"] = "SGT Class Stateroom - Visited",
        // 2nd Class
        ["secClassState"] = "2nd Class Stateroom - Visited",
        ["2ndClassLobby"] = "2nd Class Lobby - Visited",
        ["SecClassLittleLift"] = "2nd Class Lobby - Visited",
        ["Bar"] = "Bar - Visited",
        ["MusicRoom"] = "Music Room - Visited",
        ["MusicRoomLobby"] = "Music Room - Visited",
        ["PromenadeDeck"] = "Promenade Deck - Visited",
        // 1st Class
        ["1stClassState"] = "1st Class Stateroom - Visited",
        ["1stClassLobby"] = "1st Class Lobby - Visited",
        ["Arboretum"] = "Arboretum - Visited",
        ["FrozenArboretum"] = "Arboretum - Visited",
        ["1stClassRestaurant"] = "1st Class Restaurant - Visited",
        // After Titania Repair
        ["Bridge"] = "Bridge - Visited",
        ["TheEnd"] = "The End - Return Home",
    };

    // Point-of-interest locations, keyed by the exact (Room, Node, View)
    private static readonly Dictionary<RoomNodeView, string> PointOfInterestLocationName = new()
    {
        [new RoomNodeView(45, 2, 2)] = "Bilge Room - Succ-U-Bus (Mother)",  // Bilge Room without body
        [new RoomNodeView(47, 1, 2)] = "Bilge Room - Succ-U-Bus (Mother)",  // Bilge Room with body
        [new RoomNodeView(2, 2, 4)]  = "Embarkation Lobby - Succ-U-Bus",
        [new RoomNodeView(5, 2, 4)]  = "Embarkation Lobby - Succ-U-Bus",   // MoonEmbLobby
        [new RoomNodeView(9, 2, 1)]  = "Parrot Lobby - Succ-U-Bus",
        [new RoomNodeView(38, 6, 1)] = "Bottom of the Well - Succ-U-Bus",
        [new RoomNodeView(11, 3, 3)] = "SGT Class Lobby - Succ-U-Bus",
        [new RoomNodeView(16, 3, 1)] = "Promenade Deck - Succ-U-Bus",
        [new RoomNodeView(53, 2, 1)] = "Music Room - Succ-U-Bus",
        [new RoomNodeView(31, 5, 1)] = "Bar - Succ-U-Bus",
        [new RoomNodeView(49, 6, 1)] = "1st Class Restaurant - Succ-U-Bus",
        [new RoomNodeView(48, 8, 1)] = "Arboretum - Succ-U-Bus",          // Arboretum (unfrozen)
        [new RoomNodeView(52, 7, 1)] = "Arboretum - Succ-U-Bus",          // Frozen Arboretum
        [new RoomNodeView(39, 3, 1)] = "Creator's Chamber - Succ-U-Bus",
        [new RoomNodeView(42, 3, 1)] = "Creator's Chamber - Succ-U-Bus",  // pre red fuse and lever-pull
        [new RoomNodeView(44, 8, 1)] = "Sculpture Chamber - Succ-U-Bus",
        [new RoomNodeView(37, 5, 3)] = "Bomb Room - Succ-U-Bus",
        [new RoomNodeView(6, 2, 1)]  = "2nd Class Stateroom - Succ-U-Bus",
        [new RoomNodeView(34, 9, 1)] = "2nd Class Lobby - Succ-U-Bus",
        [new RoomNodeView(7, 8, 1)]  = "1st Class Stateroom - Succ-U-Bus",
        [new RoomNodeView(20, 8, 1)] = "1st Class Lobby - Succ-U-Bus",
    };

    // DeskBot class-upgrade locations, keyed by the PassengerClass value (2=Second, 1=First)
    private static readonly Dictionary<int, string> ClassUpgradeLocationName = new()
    {
        [2] = "DeskBot - 2nd Class Upgrade",
        [1] = "DeskBot - 1st Class Upgrade",
    };

    // Stateroom-assigned event locations, keyed by achieved stateroom class (see
    // GameActions.GetAchievedStateroomClass: 1=SGT/3rd, 2=2nd, 3=1st)
    private static readonly Dictionary<int, string> StateroomAssignedLocationName = new()
    {
        [1] = "DeskBot - SGT Stateroom Assigned",
        [2] = "DeskBot - 2nd Class Stateroom Assigned",
        [3] = "DeskBot - 1st Class Stateroom Assigned",
    };

    public static readonly IReadOnlyCollection<string> SuccUBusStationLocationNames =
        PointOfInterestLocationName.Values
            .Where(name => name.Contains("Succ-U-Bus", StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToArray();

    /// <summary>Human-readable display name for an engine room name, for UI purposes only.</summary>
    public static string GetReadableRoomName(string roomName)
    {
        const string suffix = " - Visited";
        if (RoomToLocationName.TryGetValue(roomName, out string? locationName))
        {
            return locationName.EndsWith(suffix, StringComparison.Ordinal)
                ? locationName[..^suffix.Length]
                : locationName;
        }

        return roomName;
    }

    /// <summary>Looks up the AP location name for a room's "Visited" check.</summary>
    public static bool TryGetLocationName(string roomName, out string locationName)
    {
        if (RoomToLocationName.TryGetValue(roomName, out string? name))
        {
            locationName = name;
            return true;
        }

        locationName = "";
        return false;
    }

    /// <summary>Looks up the AP location name for visiting an exact (Room, Node, View) point of interest.</summary>
    public static bool TryGetPointOfInterestLocationName(RoomNodeView rnv, out string locationName)
    {
        if (PointOfInterestLocationName.TryGetValue(rnv, out string? name))
        {
            locationName = name;
            return true;
        }

        locationName = "";
        return false;
    }

    /// <summary>Looks up the AP location name for a blocked DeskBot class-upgrade attempt.</summary>
    public static bool TryGetClassUpgradeLocationName(int attemptedClass, out string locationName)
    {
        if (ClassUpgradeLocationName.TryGetValue(attemptedClass, out string? name))
        {
            locationName = name;
            return true;
        }

        locationName = "";
        return false;
    }

    /// <summary>Looks up the AP location name for first achieving a given stateroom class (see
    /// GameActions.GetAchievedStateroomClass).</summary>
    public static bool TryGetStateroomAssignedLocationName(int achievedClass, out string locationName)
    {
        if (StateroomAssignedLocationName.TryGetValue(achievedClass, out string? name))
        {
            locationName = name;
            return true;
        }

        locationName = "";
        return false;
    }

    /// <summary>Looks up the AP location name for picking up a tracked item.</summary>
    public static bool TryGetItemPickupLocationName(string itemName, out string locationName)
    {
        if (Items.TryGet(itemName, out ItemDefinition item) && item.PickupLocationName is not null)
        {
            locationName = item.PickupLocationName;
            return true;
        }

        locationName = "";
        return false;
    }

    /// <summary>Looks up the AP item name for a tracked item pickup.</summary>
    public static bool TryGetApItemName(string itemName, out string apItemName)
    {
        if (Items.TryGet(itemName, out ItemDefinition item) && item.ApItemName is not null)
        {
            apItemName = item.ApItemName;
            return true;
        }

        apItemName = "";
        return false;
    }

    /// <summary>Looks up the engine item name for a granted AP item name.</summary>
    public static bool TryGetEngineItemName(string apItemName, out string itemName) =>
        Items.TryGetForApItemName(apItemName, out itemName);
}
