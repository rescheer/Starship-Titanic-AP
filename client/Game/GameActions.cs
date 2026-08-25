namespace StarshipTitanicAp;

/// <summary>
/// Write and remote-call operations. Kept separate from GameState.cs
/// (which is read-only) since these mutate the running game.
/// </summary>
public static class GameActions
{
    /// <summary>
    /// Directly writes PassengerClass (1=First, 2=Second, 3=Third, 4=None).
    /// This is a raw write, not a call into game logic - confirmed to
    /// correctly gate room access immediately, but the PET's on-screen
    /// color does not update until a save/reload UNLESS followed by
    /// reset() + markAllDirty() - see SetPassengerClassFull.
    /// </summary>
    public static bool SetPassengerClass(MemoryReader mem, long gameManager, int newClass) =>
        mem.WriteInt32(gameManager + GameOffsets.PassengerClass, newClass);

    /// <summary>
    /// Full sequence: writes PassengerClass, then calls reset() and
    /// markAllDirty() so the PET color updates immediately without
    /// requiring a save/reload. Confirmed working live.
    /// </summary>
    public static bool SetPassengerClassFull(MemoryReader mem, long gameManager, long petControlAddr, int newClass)
    {
        bool wrote = SetPassengerClass(mem, gameManager, newClass);
        if (!wrote)
            return false;

        bool reset = ResetPetControl(mem, petControlAddr);
        bool dirty = MarkAllDirty(mem, gameManager);
        return reset && dirty;
    }

    /// <summary>
    /// Directly writes _petActive. Confirmed: turning ON updates the PET
    /// UI immediately; turning OFF does not update the display until a
    /// node transition or UI interaction.
    /// </summary>
    public static bool SetPetActive(MemoryReader mem, long gameManager, bool active) =>
        mem.WriteByte(gameManager + GameOffsets.PetActive, (byte)(active ? 1 : 0));

    /// <summary>
    /// Moves an item into the given room via the game's own detach()/
    /// attach() logic (not raw pointer surgery). Confirmed to correctly
    /// update actual inventory state immediately; the PET's visible glyph
    /// list does not update until a save/reload or other incidental
    /// inventory action (picking up/dropping/moving another item) UNLESS
    /// followed by ItemsChanged() + ResetPetControl() - see MoveItemToInventoryFull.
    ///
    /// rcx and r8 are confirmed (via live breakpoint on a real pickup) to
    /// both be the destination room - NOT the item.
    /// </summary>
    public static bool MoveItemToRoom(MemoryReader mem, long itemAddr, long roomAddr, int flag1 = 0, int arg5 = 0, int arg6 = 1)
    {
        long funcAddr = mem.ModuleBase + GameOffsets.MoveItemFunc;
        return RemoteCaller.Call(mem, funcAddr,
            rcx: roomAddr,
            rdx: itemAddr,
            r8: roomAddr,
            r9d: flag1,
            arg5: arg5,
            arg6: arg6);
    }

    /// <summary>
    /// Calls CPetInventory::itemsChanged() - the real function that rebuilds
    /// the PET's visible glyph list from the current tree state. Confirmed
    /// via live disassembly of CPetControl::addToInventory() during a real
    /// item pickup. Takes &amp;_inventory (petControl + 0x6D8), NOT petControl
    /// itself, as its argument.
    /// </summary>
    public static bool NotifyItemsChanged(MemoryReader mem, long petControlAddr)
    {
        long funcAddr = mem.ModuleBase + GameOffsets.InventoryItemsChangedFunc;
        long inventoryFieldAddr = petControlAddr + GameOffsets.PetInventoryFieldOffset;
        return RemoteCaller.Call(mem, funcAddr, rcx: inventoryFieldAddr);
    }

    /// <summary>
    /// Calls CPetControl::setArea() with area=PET_INVENTORY (0), matching
    /// the exact call captured in addToInventory()'s disassembly right
    /// after itemsChanged(). Likely what actually tells the CURRENTLY
    /// VISIBLE PET tab to recompute its layout - itemsChanged() alone
    /// updates the underlying list but apparently doesn't force the
    /// active tab to redraw with it.
    /// </summary>
    public static bool SetPetAreaInventory(MemoryReader mem, long petControlAddr)
    {
        long funcAddr = mem.ModuleBase + GameOffsets.SetAreaFunc;
        return RemoteCaller.Call(mem, funcAddr, rcx: petControlAddr, rdx: 0, r8: 0);
    }

    /// <summary>
    /// Runs the refresh sequence (itemsChanged + setArea + reset +
    /// markAllDirty) against a CPetControl so its visible glyph list picks
    /// up whatever just changed underneath it - shared by the "item
    /// entered inventory" and "item left inventory" flows below, since
    /// both leave the SAME CPetControl's display stale, just via opposite
    /// moves.
    ///
    /// markAllDirty(gameManager) is the last step and turned out to be
    /// required, not optional: itemsChanged()/setArea()/reset() alone
    /// rebuild the PET's internal list/layout state correctly, but without
    /// a forced full redraw the screen doesn't actually repaint until
    /// something else does (a node/view transition) - which showed up as
    /// new items not appearing immediately, and a stale selection-highlight
    /// rectangle being left on screen after a removal. This mirrors
    /// SetPassengerClassFull, which already paired reset()+markAllDirty()
    /// and was confirmed to update on screen with no transition needed.
    ///
    /// CRITICAL: petControlAddr must be an actual CPetControl. These calls
    /// dereference CPetControl-specific fields and will crash the game if
    /// run against any other CTreeItem (e.g. CMailMan) - see MoveItemToRoom.
    /// </summary>
    public static bool RefreshPetControl(MemoryReader mem, long petControlAddr, long gameManager)
    {
        bool changed = NotifyItemsChanged(mem, petControlAddr);
        bool areaSet = SetPetAreaInventory(mem, petControlAddr);
        bool reset = ResetPetControl(mem, petControlAddr);
        bool dirty = MarkAllDirty(mem, gameManager);

        return changed && areaSet && reset && dirty;
    }

    /// <summary>
    /// Full, source-accurate sequence for granting an item: detach+attach,
    /// then refreshes the destination CPetControl so the glyph list picks
    /// it up immediately. petControlAddr is the same address this app has
    /// resolved as "inventory room" since early in the project - confirmed
    /// to BE the CPetControl object itself.
    /// </summary>
    public static bool MoveItemToInventoryFull(MemoryReader mem, long itemAddr, long petControlAddr, long gameManager)
    {
        bool moved = MoveItemToRoom(mem, itemAddr, petControlAddr);
        if (!moved)
            return false;

        return RefreshPetControl(mem, petControlAddr, gameManager);
    }

    /// <summary>
    /// Moves an item OUT of the player's inventory to some other
    /// destination (a world room, the mail room, etc.), then refreshes the
    /// SOURCE CPetControl - the inventory the item is leaving - so its
    /// glyph list drops the item immediately instead of staying stale
    /// until a save/reload or an incidental inventory action.
    ///
    /// This is the mirror image of MoveItemToInventoryFull: that one
    /// refreshes the destination because the item is arriving there; this
    /// one refreshes the source because the item is leaving there. Never
    /// pass the destination here even if you also happen to know it's a
    /// CPetControl (see MoveItemSmart if both sides might need checking).
    /// </summary>
    public static bool MoveItemOutOfInventoryFull(MemoryReader mem, long itemAddr, long destinationRoomAddr, long petControlAddr, long gameManager)
    {
        bool moved = MoveItemToRoom(mem, itemAddr, destinationRoomAddr);
        if (!moved)
            return false;

        return RefreshPetControl(mem, petControlAddr, gameManager);
    }

    /// <summary>
    /// General-purpose move that does the right thing regardless of
    /// whether the item is entering the inventory, leaving it, or neither
    /// (e.g. mail room -> world room). Reads the item's CURRENT parent
    /// BEFORE moving it, then afterward refreshes whichever side - old
    /// parent or new destination - actually matches the known CPetControl
    /// address, since that's the only side whose display could have gone
    /// stale and the only side it's safe to run these calls against.
    ///
    /// If petControlAddr is null (not resolved yet) or neither side
    /// matches it, this behaves exactly like a plain MoveItemToRoom.
    /// </summary>
    public static bool MoveItemSmart(MemoryReader mem, long itemAddr, long destinationRoomAddr, long? petControlAddr, long gameManager)
    {
        long? previousParent = petControlAddr is null
            ? null
            : mem.ReadInt64(itemAddr + GameOffsets.Parent);

        bool moved = MoveItemToRoom(mem, itemAddr, destinationRoomAddr);
        if (!moved)
            return false;

        if (petControlAddr is null)
            return true;

        // Nothing actually moved relative to the inventory - avoid a
        // pointless (though harmless) extra refresh call.
        if (previousParent == destinationRoomAddr)
            return true;

        bool leavingInventory = previousParent == petControlAddr.Value;
        bool enteringInventory = destinationRoomAddr == petControlAddr.Value;

        if (!leavingInventory && !enteringInventory)
            return true;

        return RefreshPetControl(mem, petControlAddr.Value, gameManager);
    }

    /// <summary>
    /// Calls CPetControl::reset() - the real, source-confirmed fix for the
    /// stale PET display (found in CGameObject::setPassengerClass(), which
    /// calls this after changing the class). Takes only the CPetControl
    /// object itself as an argument.
    ///
    /// The "inventory room" address this app has resolved since early in
    /// the project (via the 3-NoName-siblings tree search) IS the
    /// CPetControl object itself - confirmed live by comparing it against
    /// CGameObject::getPetControl()'s real return value.
    /// </summary>
    public static bool ResetPetControl(MemoryReader mem, long petControlAddr)
    {
        long funcAddr = mem.ModuleBase + GameOffsets.PetControlResetFunc;
        return RemoteCaller.Call(mem, funcAddr, rcx: petControlAddr);
    }

    /// <summary>
    /// Calls CPetControl::displayMessage(StringId, int) - confirmed live by
    /// breakpointing the class-restriction message ("Passengers of your
    /// class are not permitted to enter this area." = StringId 0x21/33,
    /// param 0). This is the StringId (integer index into a text resource
    /// table) overload - NOT the CString&amp; (arbitrary text) overload from
    /// source, which hasn't been traced yet. Valid StringId values and
    /// their meanings are currently unknown beyond 0x21.
    /// </summary>
    public static bool DisplayPetMessage(MemoryReader mem, long petControlAddr, int stringId, int param = 0)
    {
        long funcAddr = mem.ModuleBase + GameOffsets.DisplayMessageFunc;
        return RemoteCaller.Call(mem, funcAddr, rcx: petControlAddr, rdx: stringId, r8: param);
    }

    /// <summary>
    /// Calls CPetControl::displayMessage(const CString&amp;, int) - the free-text
    /// overload. Confirmed via disassembly: CString's layout is a 4-byte
    /// size + 4-byte padding, then an 8-byte char* at offset +8. We don't
    /// build a fully "real" CString - we fake a 16-byte header where +8
    /// points at the actual text bytes (written into the same remote
    /// allocation, right after the header), which is all the function
    /// actually reads.
    /// </summary>
    public static bool DisplayPetMessageText(MemoryReader mem, long petControlAddr, string text, int param = 0)
    {
        byte[] textBytes = System.Text.Encoding.ASCII.GetBytes(text + "\0");
        int headerSize = 16;

        byte[] buffer = new byte[headerSize + textBytes.Length];
        BitConverter.GetBytes(text.Length).CopyTo(buffer, 0);  // size field
        // bytes 4-7: padding, left zero
        // pointer field at +8 needs to point at the text bytes, which we
        // don't know the final remote address of until after allocation -
        // so we allocate first with a placeholder, then patch it in.
        Array.Copy(textBytes, 0, buffer, headerSize, textBytes.Length);

        long remoteAddr = RemoteCaller.AllocateAndWrite(mem, buffer);
        if (remoteAddr == 0)
            return false;

        long textAddr = remoteAddr + headerSize;
        bool patched = mem.WriteInt64(remoteAddr + 8, textAddr);
        if (!patched)
        {
            RemoteCaller.FreeRemoteMemory(mem, remoteAddr);
            return false;
        }

        long funcAddr = mem.ModuleBase + GameOffsets.DisplayMessageTextFunc;
        bool ok = RemoteCaller.Call(mem, funcAddr, rcx: petControlAddr, rdx: remoteAddr, r8: param);

        RemoteCaller.FreeRemoteMemory(mem, remoteAddr);
        return ok;
    }

    /// <summary>
    /// Calls CGameManager::markAllDirty() - the function ScummVM's own
    /// "pet on" debug command calls to force a redraw. Confirmed sufficient
    /// on its own for PET visibility toggling; not sufficient alone for
    /// inventory or class color (those need the fuller sequences above).
    /// </summary>
    public static bool MarkAllDirty(MemoryReader mem, long gameManager)
    {
        long funcAddr = mem.ModuleBase + GameOffsets.MarkAllDirtyFunc;
        return RemoteCaller.Call(mem, funcAddr, rcx: gameManager);
    }

    /// <summary>
    /// Retargets a mail item's destination to the given chevron/room-flags
    /// code, mimicking the state left by a real, completed delivery
    /// (CMailMan::setMailDest / SuccUBus receive flow): _roomFlags set to
    /// the destination, _isPendingMail cleared. _destRoomFlags is left
    /// untouched since it isn't checked by findMailByFlags.
    /// </summary>
    public static bool SetItemMailDestination(MemoryReader mem, long itemAddr, uint roomFlagsCode)
    {
        bool wroteRoomFlags = mem.WriteInt32(itemAddr + GameOffsets.ItemRoomFlags, unchecked((int)roomFlagsCode));
        bool wrotePending = mem.WriteByte(itemAddr + GameOffsets.ItemIsPendingMail, 0);
        return wroteRoomFlags && wrotePending;
    }

    /// <summary>
    /// Marks an item as placed into the mail system by THIS APP, not
    /// normal gameplay, by writing ToolPlacedSentinel into _destRoomFlags.
    /// Only meaningful for an already-delivered item (_roomFlags != 0) -
    /// that's the state where findMailByFlags stops consulting
    /// _destRoomFlags at all, so the sentinel can't affect real
    /// mail-retrieval logic. Always call this AFTER SetItemMailDestination
    /// has already set a real _roomFlags value, never before.
    /// </summary>
    public static bool MarkItemAsToolPlaced(MemoryReader mem, long itemAddr) =>
        mem.WriteInt32(itemAddr + GameOffsets.ItemDestRoomFlags, unchecked((int)GameOffsets.ToolPlacedSentinel));

    /// <summary>
    /// Clears the tool-placed sentinel (see MarkItemAsToolPlaced) by
    /// zeroing _destRoomFlags - used when an item leaves the mail system
    /// via this app, so a stale marker can't resurface if the same item
    /// is later routed there again by normal gameplay. Callers should
    /// only invoke this after confirming the item's current
    /// _destRoomFlags actually IS the sentinel, so an organically-mailed
    /// item's real pending-destination value is never touched.
    /// </summary>
    public static bool UnmarkItemAsToolPlaced(MemoryReader mem, long itemAddr) =>
        mem.WriteInt32(itemAddr + GameOffsets.ItemDestRoomFlags, 0);
}
