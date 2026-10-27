namespace StarshipTitanicAp;

/// <summary>How a Mover's destination room is resolved.</summary>
public enum MoverKind
{
    Fixed,           // destination hardcoded in C++, known at scan time (FixedDestRoom)
    DestinationField, // CMovePlayerTo::_destination - read live, since CMovePlayerTo::ActMsg can retarget it
                      // (e.g. CreatorsChamber's lever switches TopOfWell's door from CreatorsChamberOn to CreatorsChamber)
    ExitLift,        // depends on the PET's lift floor/elevator numbers
    ExitPellerator,  // depends on the Pellerator's current stop (the "ExitPellerator" object - every stop but the Bar)
    ExitPellerator2, // the Bar-side "ExitPellerator2" object - only exits at the Bar stop
    MultiMove,       // CMultiMove::_viewNames - the first view outside the mover's own room (e.g. TopOfWell -> Dome)
}

/// <summary>A clickable game object that moves the player into another room when clicked.</summary>
public readonly record struct Mover(long Address, int OwnRoom, MoverKind Kind, int FixedDestRoom);

/// <summary>Finds the scripted doors (game objects, not CLinkItems) whose destination room can be resolved ahead of
/// a click, so RoomGateHook can show CURSOR_INVALID on the ones leading into locked rooms. CArboretumGate and the
/// broken pellerators aren't covered yet.</summary>
public static class MoverDestinations
{
    // Classes whose MouseButtonDownMsg moves to CMovePlayerTo::_destination
    private static readonly HashSet<string> DestinationFieldClasses = new()
    {
        "CMovePlayerTo", "CRestrictedMove", "CMovePlayerToFrom", "CTripDownCanal",
        "CExitStateRoom", "CEnterBombRoom", "CExitBridge",
    };

    // Classes whose MouseButtonDownMsg moves to a room hardcoded in C++
    private static readonly Dictionary<string, string> FixedDestinationClasses = new()
    {
        ["CTOWParrotNav"] = "ParrotLobby",
    };

    // CExitPellerator's stop number -> destination room name (stop 6 is resolved separately, by season)
    private static readonly Dictionary<int, string> PelleratorStops = new()
    {
        [0] = "PromenadeDeck",
        [1] = "MusicRoomLobby",
        [4] = "TopOfWell",
        [5] = "1stClassRestaurant",
    };
    private const int PelleratorBarStop = 2;
    private const int PelleratorArboretumStop = 6;

    private const int MaxNodes = 10000;

    /// <summary>Walks the whole tree under _project, returning every mover whose destination can be resolved.</summary>
    public static List<Mover> FindAll(MemoryReader mem, long project)
    {
        var movers = new List<Mover>();
        var visited = new HashSet<long>();
        var stack = new Stack<(long Addr, int Room)>();
        long? firstChild = mem.ReadInt64(project + GameOffsets.FirstChild);
        if (firstChild is not null)
            stack.Push((firstChild.Value, -1));

        while (stack.Count > 0 && visited.Count < MaxNodes)
        {
            (long addr, int room) = stack.Pop();
            if (!GameState.LooksLikeHeapPointer(addr) || !visited.Add(addr))
                continue;

            long? nextSibling = mem.ReadInt64(addr + GameOffsets.NextSibling);
            if (nextSibling is not null)
                stack.Push((nextSibling.Value, room));

            string? className = GameState.TryGetClassName(mem, addr);
            if (className == "CRoomItem")
                room = mem.ReadInt32(addr + GameOffsets.RoomItemRoomNumberOffset) ?? -1;
            else if (room >= 0 && TryClassify(mem, addr, room, className) is { } mover)
                movers.Add(mover);

            long? child = mem.ReadInt64(addr + GameOffsets.FirstChild);
            if (child is not null)
                stack.Push((child.Value, room));
        }

        return movers;
    }

    /// <summary>Resolves where clicking the mover would currently take the player, or null if nowhere (e.g. a
    /// Pellerator exit on the wrong side for the current stop). petControl is CPetControl's address, needed for
    /// ExitLift movers.</summary>
    public static int? ResolveDestRoom(MemoryReader mem, Mover mover, long? petControl) => mover.Kind switch
    {
        MoverKind.Fixed => mover.FixedDestRoom,
        MoverKind.DestinationField => TryGetViewRoom(
            GameState.ReadCString(mem, mover.Address + GameOffsets.MovePlayerToDestinationOffset), out int destRoom)
            ? destRoom
            : null,
        MoverKind.ExitLift => ResolveExitLift(mem, mover.Address, petControl),
        MoverKind.ExitPellerator or MoverKind.ExitPellerator2 => ResolveExitPellerator(mem, mover.Kind),
        MoverKind.MultiMove => ResolveMultiMove(mem, mover),
        _ => null,
    };

    private static Mover? TryClassify(MemoryReader mem, long addr, int room, string? className)
    {
        switch (className)
        {
            case "CExitLift":
                return new Mover(addr, room, MoverKind.ExitLift, 0);
            case "CExitPellerator":
                string? name = GameState.ReadCString(mem, addr + GameOffsets.NamedItemNameOffset);
                return new Mover(addr, room, name == "ExitPellerator2" ? MoverKind.ExitPellerator2 : MoverKind.ExitPellerator, 0);
            case "CMultiMove":
                return new Mover(addr, room, MoverKind.MultiMove, 0);
        }

        if (className is null)
            return null;

        if (DestinationFieldClasses.Contains(className))
            return new Mover(addr, room, MoverKind.DestinationField, 0);

        if (FixedDestinationClasses.TryGetValue(className, out string? roomName)
            && RoomNames.TryGetRoomId(roomName, out int destRoom) && destRoom != room)
        {
            return new Mover(addr, room, MoverKind.Fixed, destRoom);
        }

        return null;
    }

    /// <summary>Mirrors CExitLift::MouseButtonDownMsg's floor/elevator -> exit view mapping. For combinations it
    /// doesn't map, the game reuses the object's previous _viewName, so this does too.</summary>
    private static int? ResolveExitLift(MemoryReader mem, long exitLift, long? petControl)
    {
        if (petControl is null)
            return null;

        int? floorNum = mem.ReadInt32(petControl.Value + GameOffsets.PetControlRoomsFloorNum);
        int? elevatorNum = mem.ReadInt32(petControl.Value + GameOffsets.PetControlRoomsElevatorNum);
        if (floorNum is not int floor || elevatorNum is not int elev)
            return null;

        bool westElevator = elev is 1 or 3;
        bool anyElevator = elev is >= 1 and <= 4;
        string? roomName = floor switch
        {
            39 => westElevator ? "BottomOfWell" : null,
            > 27 => westElevator ? "SgtLobby" : null,
            > 19 => anyElevator ? "2ndClassLobby" : null,
            > 1 => anyElevator ? "1stClassLobby" : null,
            _ => anyElevator ? "TopOfWell" : null,
        };

        if (roomName is not null)
            return RoomNames.TryGetRoomId(roomName, out int destRoom) ? destRoom : null;

        return TryGetViewRoom(GameState.ReadCString(mem, exitLift + GameOffsets.ExitLiftViewNameOffset), out int previousRoom)
            ? previousRoom
            : null;
    }

    /// <summary>Mirrors CExitPellerator::MouseButtonDownMsg's stop -> exit view mapping.</summary>
    private static int? ResolveExitPellerator(MemoryReader mem, MoverKind kind)
    {
        long? statics = mem.ReadInt64(mem.ModuleBase + GameOffsets.ExitPelleratorStaticsPtrStatic);
        if (statics is null || !GameState.LooksLikeHeapPointer(statics.Value))
            return null;

        int? stop = mem.ReadInt32(statics.Value + GameOffsets.ExitPelleratorStaticsStopOffset);
        if (stop is null)
            return null;

        string? roomName;
        if (kind == MoverKind.ExitPellerator2)
        {
            roomName = stop == PelleratorBarStop ? "Bar" : null;
        }
        else if (stop == PelleratorArboretumStop)
        {
            byte[]? isWinter = mem.ReadBytes(statics.Value + GameOffsets.ExitPelleratorStaticsIsWinterOffset, 1);
            roomName = isWinter is not null && isWinter[0] != 0 ? "FrozenArboretum" : "Arboretum";
        }
        else
        {
            roomName = PelleratorStops.GetValueOrDefault(stop.Value);
        }

        return roomName is not null && RoomNames.TryGetRoomId(roomName, out int destRoom) ? destRoom : null;
    }

    /// <summary>Mirrors CMultiMove::MouseButtonDownMsg's walk over _viewNames (stopping at "NULL"), returning the
    /// first room it leaves the mover's own room for.</summary>
    private static int? ResolveMultiMove(MemoryReader mem, Mover mover)
    {
        for (int i = 0; i < GameOffsets.MultiMoveViewNameCount; i++)
        {
            string? viewName = GameState.ReadCString(mem,
                mover.Address + GameOffsets.MultiMoveViewNamesOffset + i * GameOffsets.CStringSize);
            if (viewName is null or "NULL")
                break;
            if (TryGetViewRoom(viewName, out int room) && room != mover.OwnRoom)
                return room;
        }
        return null;
    }

    /// <summary>"Room.Node N.Dir" -> Room's ID.</summary>
    private static bool TryGetViewRoom(string? viewName, out int roomId)
    {
        roomId = 0;
        int dot = viewName?.IndexOf('.') ?? -1;
        return dot > 0 && RoomNames.TryGetRoomId(viewName![..dot], out roomId);
    }
}
