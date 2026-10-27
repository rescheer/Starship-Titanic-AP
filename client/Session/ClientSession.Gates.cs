namespace StarshipTitanicAp;

internal sealed partial class ClientSession
{
    private const string TableAccessItemName = "Restaurant Table Reservation";

    /// <summary>Grants access to the Maitre'D's table (the effect MaitreDHook blocks) once the "Table Access"
    /// AP item is received, regardless of whether the MaitreD was ever defeated naturally.</summary>
    private void SyncTableAccessFromItems()
    {
        if (_ctx.Project is not { } project)
            return;

        string[] receivedItems = _apConnection.GetReceivedItemNames();
        if (receivedItems.Length == _lastTableAccessItemsCount)
            return;
        _lastTableAccessItemsCount = receivedItems.Length;

        if (!receivedItems.Contains(TableAccessItemName, StringComparer.OrdinalIgnoreCase))
            return;

        long? tableAddr = GameState.FindScraliontisTable(_mem, project);
        if (tableAddr is null)
            return;

        int? currentCursorId = _mem.ReadInt32(tableAddr.Value + GameOffsets.GameObjectCursorIdOffset);
        if (currentCursorId == GameOffsets.ScraliontisTableEnterableCursorId)
            return;

        bool ok = GameActions.GrantScraliontisTableAccess(_mem, tableAddr.Value);
        ReportAction(ok, "Table Access granted -> Maitre'D's table unlocked");
    }

    /// <summary>Keeps CTelevision::_channel4Glyph pinned to true, so taking Eye1 never takes channel 4's picture
    /// off the television. Only writes when the game has cleared it.</summary>
    private void KeepTelevisionChannel4GlyphOn()
    {
        long addr = _mem.ModuleBase + GameOffsets.TelevisionChannel4GlyphStatic;
        byte[]? current = _mem.ReadBytes(addr, 1);
        if (current is null || current[0] != 0)
            return;

        bool ok = _mem.WriteByte(addr, 1);
        AppendLog(ok
            ? "CTelevision::_channel4Glyph was cleared - reset to true"
            : "CTelevision::_channel4Glyph was cleared - failed to reset it");
    }

    /// <summary>Stand-in written over CGetLiftEye2::_destObject's "NULL". Not the name of any real game object.</summary>
    private const string LiftEyeDestObjectPlaceholder = "APNoEye";

    /// <summary>Keeps CGetLiftEye2::_destObject from reading "NULL", so the television's channel 5 always shows the
    /// lift's floor (CTelevision::PETActivateMsg only does while it's != "NULL"). Only writes when the game has
    /// set it to "NULL", and only into the CString's own inline buffer.</summary>
    private void KeepLiftEyeDestObjectNonNull()
    {
        long? strAddr = _mem.ReadInt64(_mem.ModuleBase + GameOffsets.GetLiftEye2DestObjectPtrStatic);
        if (strAddr is not long sa || sa == 0)
            return;

        int? size = _mem.ReadInt32(sa + GameOffsets.CStringSizeOffset);
        long? dataPtr = _mem.ReadInt64(sa + GameOffsets.CStringDataPtrOffset);
        if (size != 4 || dataPtr is not long dp)
            return;
        if (_mem.ReadShortAsciiString(dp, 4) != "NULL")
            return;

        if (dp != sa + GameOffsets.CStringInlineStorageOffset
            || LiftEyeDestObjectPlaceholder.Length + 1 > GameOffsets.CStringInlineCapacity)
        {
            AppendLog("CGetLiftEye2::_destObject is \"NULL\" but not in its inline buffer - left alone");
            return;
        }

        bool wroteBytes = _mem.WriteBytes(dp, System.Text.Encoding.ASCII.GetBytes(LiftEyeDestObjectPlaceholder + "\0"));
        bool wroteSize = _mem.WriteInt32(sa + GameOffsets.CStringSizeOffset, LiftEyeDestObjectPlaceholder.Length);
        AppendLog(wroteBytes && wroteSize
            ? $"CGetLiftEye2::_destObject was \"NULL\" - set to \"{LiftEyeDestObjectPlaceholder}\""
            : "CGetLiftEye2::_destObject was \"NULL\" - failed to overwrite it");
    }

    /// <summary>Keeps GetLiftEye2GateHook's remote "may take the Eye" gate byte in sync with whether the LiftBot
    /// Head is both AP-granted and physically sitting in the player's inventory.</summary>
    private void SyncLiftEyeGate(List<CarryItemLocation> items, string[] receivedItems)
    {
        if (!GetLiftEye2GateHook.IsInstalled || _ctx.InventoryRoom is null)
            return;

        bool headGranted = LocationChecks.TryGetApItemName("BrokenLiftbotHead", out string headApName)
            && receivedItems.Contains(headApName, StringComparer.OrdinalIgnoreCase);
        bool headInInventory = headGranted
            && FindByName(items, "BrokenLiftbotHead") is { } head
            && head.ParentAddress == _ctx.InventoryRoom.Value;

        GetLiftEye2GateHook.SetGateAllowed(_mem, headInInventory);
    }

    /// <summary>Reacts to GetLiftEye2GateHook firing: tells the player what they're missing.</summary>
    private void HandleBlockedLiftEyeDrag()
    {
        if (_ctx.InventoryRoom is null)
            return;

        GameActions.DisplayMessageSmart(_mem, _ctx.InventoryRoom.Value,
            "AP: You can't take that. Maybe if you had something to replace it with.");
    }

    // Every scripted door in the current tree (MoverDestinations.FindAll), found once per tree - null until scanned
    private List<Mover>? _movers;

    /// <summary>Keeps RoomGateHook's remote tables in sync: rooms whose AP key hasn't been received, plus any extra
    /// rooms typed into the debug box, and the scripted doors leading into them. A locked room locks every variant
    /// of its location, and the location the player is currently in is never locked - the hook's own stub only
    /// exempts the exact current room, which would block the game's scripted moves between variants.</summary>
    private void SyncRoomGate()
    {
        if (!RoomGateHook.IsInstalled)
            return;

        var locked = new HashSet<int>();
        void Lock(int roomId)
        {
            foreach (int variant in RoomNames.GetVariants(roomId))
                locked.Add(variant);
        }

        HashSet<string> requiredCards = RoomKeys.GetRequiredCards(_apConnection.SlotData);
        foreach (int roomId in RoomKeys.GetLockedRooms(requiredCards, _apConnection.GetReceivedItemNames()))
            Lock(roomId);
        foreach (string entry in Options.ExtraLockedRooms.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(entry, out int roomId) || RoomNames.TryGetRoomId(entry, out roomId))
                Lock(roomId);
        }
        if (_lastRoomNodeView is { } rnv)
            locked.ExceptWith(RoomNames.GetVariants(rnv.Room));

        RoomGateHook.SetLockedRooms(_mem, locked);

        if (_movers is null && _ctx.Project is { } project)
        {
            _movers = MoverDestinations.FindAll(_mem, project);
            AppendLog($"RoomGateHook: found {_movers.Count} scripted doors with resolvable destinations");
        }
        if (_movers is not null)
        {
            RoomGateHook.SetCursorBlockedObjects(_mem, _movers
                .Where(m => MoverDestinations.ResolveDestRoom(_mem, m, _ctx.InventoryRoom) is int dest
                    && dest != m.OwnRoom && locked.Contains(dest))
                .Select(m => m.Address));
        }
    }

    /// <summary>Reacts to RoomGateHook firing: tells the player which key the room they tried to enter needs.</summary>
    private void HandleBlockedRoomMove(int fromRoom, int toRoom)
    {
        string toRoomName = RoomNames.GetName(toRoom);
        AppendLog($"RoomGateHook: blocked move from {RoomNames.GetName(fromRoom)} into locked room {toRoomName}");

        if (_ctx.InventoryRoom is null)
            return;

        string message = RoomKeys.TryGetKeyItem(toRoom, out string keyItem)
            ? PickRandom(MissingKeyMessages, keyItem)
            : PickRandom(LockedRoomMessages, toRoomName);
        GameActions.DisplayMessageSmart(_mem, _ctx.InventoryRoom.Value, message);
    }

    /// <summary>Responses when the locked room has a known key item. {0} is the key item name.</summary>
    private static readonly string[] MissingKeyMessages =
    [
        "No {0}? Get lost.",
        "Interstellar law prohibits passage through this door without the {0}.",
        "Come back with a {0}.",
        "Trying to waltz in without the {0}? Who exactly do you think you are?",
        "You need the {0}. Go bother another door.",
        "Please do not touch that. Especially without the {0}.",
        "Without the {0}, this door remains in an advanded state of non-functionality.",
        "You do not have the {0}. You owe this door an apology.",
        "Passengers are reminded to treat the ship's doors with respect. And with the proper Access Card.",
        "Without the {0}, it is hopeless. The door has won the day.",
        "Trying to enter without the {0}? Indecorous.",
        "No {0}? The door will remember this. It would laugh if it could. It's trying.",
        "You do no have an Access Card for this area. Security has been dispatched. Security has fallen down the well.",
        "Everything else on the ship may be broken, but this door stands resolute. Bring an Access Card next time.",
    ];

    /// <summary>Responses when the locked room has no known key item. {0} is the room name.</summary>
    private static readonly string[] LockedRoomMessages =
    [
        "AP: {0} is locked.",
    ];

    private static string PickRandom(string[] templates, string arg) =>
        string.Format(templates[Random.Shared.Next(templates.Length)], arg);
}
