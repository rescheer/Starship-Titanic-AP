namespace StarshipTitanicAp;

/// <summary>Write and remote-call operations that mutate the running game.</summary>
public static class GameActions
{
    /// <summary>Directly writes PassengerClass (1=First, 2=Second, 3=Third, 4=None).</summary>
    public static bool SetPassengerClass(MemoryReader mem, long gameManager, int newClass) =>
        mem.WriteInt32(gameManager + GameOffsets.PassengerClass, newClass);

    /// <summary>Writes PassengerClass and refreshes the PET so its display updates immediately.</summary>
    public static bool SetPassengerClassFull(MemoryReader mem, long gameManager, long petControlAddr, int newClass)
    {
        bool wrote = SetPassengerClass(mem, gameManager, newClass);
        if (!wrote)
            return false;

        bool reset = ResetPetControl(mem, petControlAddr);
        bool dirty = MarkAllDirty(mem, gameManager);
        return reset && dirty;
    }

    /// <summary>Actually performs a room assignment for the given class (1=First, 2=Second, 3=Third/SGT) by
    /// temporarily lifting RoomAssignHook's block, invoking the real petReassignRoom() so its own glyph-allocation
    /// logic runs for real (picking/dedup-ing a room the same way the vanilla DeskBot interaction would), then
    /// re-installing the hook so every other (non-AP-driven) attempt stays blocked. Used exclusively by
    /// StateroomAssignTracker's item-driven progression - unlike PassengerClass, "which room" isn't a plain field
    /// write, so it can't be spoofed the way SetPassengerClass is.</summary>
    public static bool AssignNextRoom(MemoryReader mem, long gameManager, long petControlAddr, int newClass)
    {
        if (!RoomAssignHook.IsInstalled)
            return false;
        if (!RoomAssignHook.Uninstall(mem))
            return false;

        long funcAddr = mem.ModuleBase + GameOffsets.PetReassignRoomFunc;
        bool called = RemoteCaller.Call(mem, funcAddr, rcx: gameManager, rdx: newClass);

        bool reinstalled = RoomAssignHook.Install(mem);

        bool refreshed = ResetPetControl(mem, petControlAddr) && MarkAllDirty(mem, gameManager);
        return called && reinstalled && refreshed;
    }

    /// <summary>Moves an item into the given room via the game's own detach()/attach() logic.</summary>
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

    /// <summary>The hidden room's own tree address, cached after the first successful move there this session.</summary>
    public static long? HiddenRoomAddress { get; private set; }

    public static void ClearHiddenRoomAddressCache() => HiddenRoomAddress = null;

    /// <summary>Stashes an item under the hidden room via CGameObject::petMoveToHiddenRoom().</summary>
    public static bool MoveItemToHiddenRoom(MemoryReader mem, long itemAddr)
    {
        long funcAddr = mem.ModuleBase + GameOffsets.PetMoveToHiddenRoomFunc;
        bool ok = RemoteCaller.Call(mem, funcAddr, rcx: itemAddr);

        if (ok && HiddenRoomAddress is null)
            HiddenRoomAddress = mem.ReadInt64(itemAddr + GameOffsets.Parent);

        return ok;
    }

    /// <summary>Moves an item to the hidden room and refreshes the source PET's display.</summary>
    public static bool MoveItemToHiddenRoomFull(MemoryReader mem, long itemAddr, long petControlAddr, long gameManager)
    {
        bool moved = MoveItemToHiddenRoom(mem, itemAddr);
        if (!moved)
            return false;

        return RefreshPetControl(mem, petControlAddr, gameManager);
    }

    /// <summary>Calls CPetInventory::itemsChanged() to rebuild the PET's visible glyph list.</summary>
    public static bool NotifyItemsChanged(MemoryReader mem, long petControlAddr)
    {
        long funcAddr = mem.ModuleBase + GameOffsets.InventoryItemsChangedFunc;
        long inventoryFieldAddr = petControlAddr + GameOffsets.PetInventoryFieldOffset;
        return RemoteCaller.Call(mem, funcAddr, rcx: inventoryFieldAddr);
    }

    /// <summary>Calls CPetControl::setArea() with area=PET_INVENTORY.</summary>
    public static bool SetPetAreaInventory(MemoryReader mem, long petControlAddr)
    {
        long funcAddr = mem.ModuleBase + GameOffsets.SetAreaFunc;
        return RemoteCaller.Call(mem, funcAddr, rcx: petControlAddr, rdx: 0, r8: 0);
    }

    /// <summary>Runs the full refresh sequence so a CPetControl's visible glyph list picks up whatever changed
    /// underneath it. Only forces the visible area to Inventory if that's already what's showing - background
    /// housekeeping (hiding/restoring/mailing tracked items) shouldn't yank the player away from whatever PET
    /// tab (map, chevron, conversation) they're currently looking at.</summary>
    public static bool RefreshPetControl(MemoryReader mem, long petControlAddr, long gameManager)
    {
        bool changed = NotifyItemsChanged(mem, petControlAddr);

        int? area = GameState.GetCurrentPetArea(mem, petControlAddr);
        bool areaSet = area != GameOffsets.PetAreaInventory || SetPetAreaInventory(mem, petControlAddr);

        bool reset = ResetPetControl(mem, petControlAddr);
        bool dirty = MarkAllDirty(mem, gameManager);

        return changed && areaSet && reset && dirty;
    }

    /// <summary>Moves an item to a room, refreshing the PET's display only if the move affects the inventory.</summary>
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

        if (previousParent == destinationRoomAddr)
            return true;

        bool leavingInventory = previousParent == petControlAddr.Value;
        bool enteringInventory = destinationRoomAddr == petControlAddr.Value;

        if (!leavingInventory && !enteringInventory)
            return true;

        return RefreshPetControl(mem, petControlAddr.Value, gameManager);
    }

    /// <summary>Calls CPetControl::reset() to fix a stale PET display.</summary>
    public static bool ResetPetControl(MemoryReader mem, long petControlAddr)
    {
        long funcAddr = mem.ModuleBase + GameOffsets.PetControlResetFunc;
        return RemoteCaller.Call(mem, funcAddr, rcx: petControlAddr);
    }

    /// <summary>Calls CPetControl::displayMessage(const CString&amp;, int), the free-text overload.</summary>
    public static bool DisplayPetMessageText(MemoryReader mem, long petControlAddr, string text, int param = 0)
    {
        byte[] textBytes = System.Text.Encoding.ASCII.GetBytes(text + "\0");
        int headerSize = 16;

        byte[] buffer = new byte[headerSize + textBytes.Length];
        BitConverter.GetBytes(text.Length).CopyTo(buffer, 0);
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

    /// <summary>Placement-constructs a CString(const char*) directly over fieldAddr.</summary>
    public static bool ConstructCString(MemoryReader mem, long fieldAddr, string text)
    {
        byte[] textBytes = System.Text.Encoding.ASCII.GetBytes(text + "\0");
        long textBufAddr = RemoteCaller.AllocateAndWrite(mem, textBytes);
        if (textBufAddr == 0)
            return false;

        long ctorFuncAddr = mem.ModuleBase + GameOffsets.CStringCharPtrCtorFunc;
        bool ok = RemoteCaller.Call(mem, ctorFuncAddr, rcx: fieldAddr, rdx: textBufAddr);

        RemoteCaller.FreeRemoteMemory(mem, textBufAddr);
        return ok;
    }

    /// <summary>Calls CPetConversations::displayMessage(const CString&amp;) to log a conversation line.</summary>
    public static bool DisplayMessage(MemoryReader mem, long conversationsAddr, string text)
    {
        long scratch = RemoteCaller.AllocateAndWrite(mem, new byte[64]);
        if (scratch == 0)
            return false;

        bool constructed = ConstructCString(mem, scratch, text);
        if (!constructed)
        {
            RemoteCaller.FreeRemoteMemory(mem, scratch);
            return false;
        }

        long funcAddr = mem.ModuleBase + GameOffsets.PetConversationsDisplayMessageFunc;
        bool ok = RemoteCaller.Call(mem, funcAddr, rcx: conversationsAddr, rdx: scratch);

        RemoteCaller.FreeRemoteMemory(mem, scratch);
        return ok;
    }

    /// <summary>Shows a message using whichever method suits the currently visible PET tab.</summary>
    public static bool DisplayMessageSmart(MemoryReader mem, long petControlAddr, string text)
    {
        long conversationsAddr = GameState.ResolveConversationsAddr(petControlAddr);
        bool logged = DisplayMessage(mem, conversationsAddr, text);

        int? area = GameState.GetCurrentPetArea(mem, petControlAddr);
        if (area == GameOffsets.PetAreaConversation)
            return logged;

        bool shownImmediately = DisplayPetMessageText(mem, petControlAddr, text, 0);
        return logged && shownImmediately;
    }

    /// <summary>Calls CGameManager::markAllDirty() to force a full redraw.</summary>
    public static bool MarkAllDirty(MemoryReader mem, long gameManager)
    {
        long funcAddr = mem.ModuleBase + GameOffsets.MarkAllDirtyFunc;
        return RemoteCaller.Call(mem, funcAddr, rcx: gameManager);
    }

    /// <summary>Retargets a mail item's destination to the given chevron/room-flags code.</summary>
    public static bool SetItemMailDestination(MemoryReader mem, long itemAddr, uint roomFlagsCode)
    {
        bool wroteRoomFlags = mem.WriteInt32(itemAddr + GameOffsets.ItemRoomFlags, unchecked((int)roomFlagsCode));
        bool wrotePending = mem.WriteByte(itemAddr + GameOffsets.ItemIsPendingMail, 0);
        return wroteRoomFlags && wrotePending;
    }

    /// <summary>Marks an item as placed into the mail system by this app.</summary>
    public static bool MarkItemAsToolPlaced(MemoryReader mem, long itemAddr) =>
        mem.WriteInt32(itemAddr + GameOffsets.ItemDestRoomFlags, unchecked((int)GameOffsets.ToolPlacedSentinel));

    /// <summary>Clears the tool-placed sentinel on an item.</summary>
    public static bool UnmarkItemAsToolPlaced(MemoryReader mem, long itemAddr) =>
        mem.WriteInt32(itemAddr + GameOffsets.ItemDestRoomFlags, 0);

    /// <summary>Clears a CShipSetting's _itemName back to "NULL" in place, mirroring what
    /// CShipSetting::MouseDragStartMsg does when a fuse is properly pulled out of its socket. Overwrites the
    /// existing string buffer in place (safe here since "NULL" is always shorter than any real fuse name) rather
    /// than reallocating, and updates the CString's own size field to match.</summary>
    public static bool ClearShipSettingItemName(MemoryReader mem, long shipSettingAddr)
    {
        long fieldAddr = shipSettingAddr + GameOffsets.ShipSettingItemNameOffset;
        long? dataPtr = mem.ReadInt64(fieldAddr + 8);
        if (dataPtr is not long dp || dp == 0)
            return false;

        bool wroteBytes = mem.WriteBytes(dp, System.Text.Encoding.ASCII.GetBytes("NULL\0"));
        bool wroteSize = mem.WriteInt32(fieldAddr, 4);
        return wroteBytes && wroteSize;
    }

    /// <summary>Full reset of a CShipSetting back to "empty," mirroring CShipSetting::MouseDragStartMsg's own
    /// cleanup when a fuse is properly pulled out: clears _itemName, resets the socket's own cursor to
    /// CURSOR_ARROW, and resets the displayed frame via _frameTarget (frame 0) so the ghost sprite and
    /// pickup-style cursor clear immediately rather than waiting for the next EnterRoomMsg (room re-entry).</summary>
    public static bool ResetShipSetting(MemoryReader mem, long shipSettingAddr, long fuseBoxView)
    {
        bool clearedName = ClearShipSettingItemName(mem, shipSettingAddr);
        bool cursorReset = SetItemCursorId(mem, shipSettingAddr, GameOffsets.CursorArrow);

        bool frameReset = true;
        string? frameTargetName = GameState.ReadShipSettingFrameTarget(mem, shipSettingAddr);
        if (frameTargetName is not null
            && GameState.FindDescendantByName(mem, fuseBoxView, frameTargetName) is { } frameTargetAddr)
        {
            frameReset = CallLoadFrame(mem, frameTargetAddr, 0);
        }

        return clearedName && cursorReset && frameReset;
    }

    /// <summary>Writes an item's _visible flag.</summary>
    public static bool SetItemVisible(MemoryReader mem, long itemAddr, bool visible) =>
        mem.WriteByte(itemAddr + GameOffsets.GameObjectVisibleOffset, (byte)(visible ? 1 : 0));

    /// <summary>Writes a CCarry item's _canTake flag, which gates pickup independent of _visible/_cursorId.</summary>
    public static bool SetItemCanTake(MemoryReader mem, long itemAddr, bool canTake) =>
        mem.WriteByte(itemAddr + GameOffsets.CarryCanTakeOffset, (byte)(canTake ? 1 : 0));

    /// <summary>Writes an item's _bounds rectangle, which gates mouse hit-testing for it.</summary>
    public static bool SetItemBounds(MemoryReader mem, long itemAddr, short left, short top, short right, short bottom)
    {
        int lt = (ushort)left | ((ushort)top << 16);
        int rb = (ushort)right | ((ushort)bottom << 16);
        bool wroteLt = mem.WriteInt32(itemAddr + GameOffsets.GameObjectBoundsOffset, lt);
        bool wroteRb = mem.WriteInt32(itemAddr + GameOffsets.GameObjectBoundsOffset + 4, rb);
        return wroteLt && wroteRb;
    }

    /// <summary>Writes an item's _cursorId.</summary>
    public static bool SetItemCursorId(MemoryReader mem, long itemAddr, int cursorId) =>
        mem.WriteInt32(itemAddr + GameOffsets.GameObjectCursorIdOffset, cursorId);

    /// <summary>Writes the two fields CScraliontisTable::MaitreDDefeatedMsg() would have written, granting
    /// table access without requiring the natural shell-game win (see MaitreDHook).</summary>
    public static bool GrantScraliontisTableAccess(MemoryReader mem, long tableAddr)
    {
        bool cursor = SetItemCursorId(mem, tableAddr, GameOffsets.ScraliontisTableEnterableCursorId);
        bool fieldEc = mem.WriteByte(tableAddr + GameOffsets.ScraliontisTableFieldECOffset, 1);
        return cursor && fieldEc;
    }

    /// <summary>Calls CGameObject::loadFrame(int frameNumber) to seek an item's displayed movie frame.</summary>
    public static bool CallLoadFrame(MemoryReader mem, long itemAddr, int frameNumber)
    {
        long funcAddr = mem.ModuleBase + GameOffsets.LoadFrameFunc;
        return RemoteCaller.Call(mem, funcAddr, rcx: itemAddr, rdx: frameNumber);
    }

    /// <summary>Relinks an item to be the first child of its parent's sibling list.</summary>
    public static bool MoveToFirstChild(MemoryReader mem, long itemAddr, long parentAddr)
    {
        long? firstChild = mem.ReadInt64(parentAddr + GameOffsets.FirstChild);
        if (firstChild is null)
            return false;
        if (firstChild.Value == itemAddr)
            return true; // already first - nothing to do

        long? priorSibling = mem.ReadInt64(itemAddr + GameOffsets.PriorSibling);
        long? nextSibling = mem.ReadInt64(itemAddr + GameOffsets.NextSibling);
        if (priorSibling is null || nextSibling is null)
            return false;

        // Unlink from current position.
        bool unlinkedPrior = priorSibling.Value == 0
            || mem.WriteInt64(priorSibling.Value + GameOffsets.NextSibling, nextSibling.Value);
        bool unlinkedNext = nextSibling.Value == 0
            || mem.WriteInt64(nextSibling.Value + GameOffsets.PriorSibling, priorSibling.Value);

        // Splice in as the new head.
        bool wroteItemNext = mem.WriteInt64(itemAddr + GameOffsets.NextSibling, firstChild.Value);
        bool wroteItemPrior = mem.WriteInt64(itemAddr + GameOffsets.PriorSibling, 0);
        bool wroteOldFirstPrior = mem.WriteInt64(firstChild.Value + GameOffsets.PriorSibling, itemAddr);
        bool wroteParentFirst = mem.WriteInt64(parentAddr + GameOffsets.FirstChild, itemAddr);

        return unlinkedPrior && unlinkedNext && wroteItemNext && wroteItemPrior && wroteOldFirstPrior && wroteParentFirst;
    }

    /// <summary>Reads this app's own persisted per-item state.</summary>
    public static ItemPersistedState ReadItemPersistedState(MemoryReader mem, long itemAddr)
    {
        int? raw = mem.ReadInt32(itemAddr + GameOffsets.GameObjectUnused4Offset);
        return raw is null ? ItemPersistedState.None : ItemPersistedState.Decode(raw.Value);
    }

    /// <summary>Writes this app's own persisted per-item state.</summary>
    public static bool WriteItemPersistedState(MemoryReader mem, long itemAddr, ItemPersistedState state) =>
        mem.WriteInt32(itemAddr + GameOffsets.GameObjectUnused4Offset, state.Encode());

/// <summary>One CPetRoomsGlyph found while walking _glyphs' linked list.</summary>
    public readonly record struct RoomGlyph(long GlyphAddr, uint RoomFlags, int Mode);

    /// <summary>Walks CPetRooms::_glyphs' linked list directly (see GameOffsets.FindGlyphByFlagsFunc for the
    /// disassembly this mirrors) - deliberately a pure, read-only walk from OUR OWN process rather than a
    /// RemoteCaller call into the game: injecting a CreateRemoteThread call at room-entry time (when callers of
    /// this need it) races the game's own main thread over the same list and can corrupt it - this avoids
    /// executing anything inside the target process at all. Stops early (silently truncating) if a node/glyph
    /// pointer is unreadable, and caps at 64 nodes (well past addRoom's own 32-glyph trim limit) so a corrupt
    /// pointer chain can't loop forever.</summary>
    public static IEnumerable<RoomGlyph> EnumerateGlyphs(MemoryReader mem, long petControlAddr)
    {
        long glyphsAddr = petControlAddr + GameOffsets.PetRoomsOffset + GameOffsets.PetRoomsGlyphsOffset;
        long sentinel = glyphsAddr + 8;

        long? current = mem.ReadInt64(glyphsAddr + 0x10); // _glyphs' own head-of-list pointer
        for (int i = 0; i < 64 && current is { } node && node != sentinel && node != 0; i++)
        {
            long? glyphAddr = mem.ReadInt64(node + 0x10); // node's payload pointer -> CPetRoomsGlyph*
            if (glyphAddr is { } addr && addr != 0)
            {
                int? roomFlags = mem.ReadInt32(addr + GameOffsets.PetRoomsGlyphRoomFlagsOffset);
                int? mode = mem.ReadInt32(addr + GameOffsets.PetRoomsGlyphModeOffset);
                if (roomFlags is not null && mode is not null)
                    yield return new RoomGlyph(addr, unchecked((uint)roomFlags.Value), mode.Value);
            }

            current = mem.ReadInt64(node + 0x08); // node's own next pointer
        }
    }

    /// <summary>Finds the CPetRoomsGlyph matching a given roomFlags value. Null if not found.</summary>
    public static long? FindGlyphByRoomFlags(MemoryReader mem, long petControlAddr, uint roomFlags)
    {
        foreach (RoomGlyph glyph in EnumerateGlyphs(mem, petControlAddr))
        {
            if (glyph.RoomFlags == roomFlags)
                return glyph.GlyphAddr;
        }
        return null;
    }

    /// <summary>Finds the glyph flagged RGM_PREV_ASSIGNED_ROOM for a specific passenger class (raw value: 1=First,
    /// 2=Second, 3=Third, 4=None - see RoomFlags.Decode's classNum) - i.e. "the room that used to be assigned
    /// before a later class upgrade bumped it out", identified by its own roomFlags-encoded class rather than a
    /// pre-captured value, since capturing roomFlags opportunistically on room entry proved unreliable (can catch
    /// a stale/transient reading mid room-transition, before the RNV and roomFlags fields settle together).
    /// Null if not found (e.g. that class was never assigned, or its glyph aged out of the 32-entry list).</summary>
    public static long? FindPrevAssignedGlyphForClass(MemoryReader mem, long petControlAddr, int passengerClass)
    {
        foreach (RoomGlyph glyph in EnumerateGlyphs(mem, petControlAddr))
        {
            if (glyph.Mode != GameOffsets.RgmPrevAssignedRoom)
                continue;
            if (RoomFlags.IsNamedRoom(glyph.RoomFlags))
                continue;

            (_, int classNum, _, _) = RoomFlags.Decode(glyph.RoomFlags);
            if (classNum == passengerClass)
                return glyph.GlyphAddr;
        }
        return null;
    }

    /// <summary>Whether any glyph in CPetRooms::_glyphs currently reads RGM_ASSIGNED_ROOM - i.e. whether the player
    /// has ever been assigned a room at all. False before the DeskBot's very first (No Class) interaction has run.</summary>
    public static bool HasAnyAssignedRoom(MemoryReader mem, long petControlAddr)
    {
        foreach (RoomGlyph glyph in EnumerateGlyphs(mem, petControlAddr))
        {
            if (glyph.Mode == GameOffsets.RgmAssignedRoom)
                return true;
        }
        return false;
    }

    /// <summary>Counts CPetRooms glyphs that have ever been assigned as the player's own stateroom - i.e. Mode is
    /// RgmAssignedRoom or RgmPrevAssignedRoom, excluding RgmUnassigned (_mode == 0, a glyph allocated but never
    /// actually assigned) and named-room glyphs (see RoomFlags.IsNamedRoom), which aren't randomly-assigned
    /// stateroom classes. reassignRoom() only ever demotes the previously-assigned glyph to RgmPrevAssignedRoom
    /// rather than removing it, and each stateroom class (SGT/3rd, 2nd, 1st) is assigned exactly once, so this
    /// count equals the number of stateroom classes the player has ever reached.</summary>
    public static int CountEverAssignedRooms(MemoryReader mem, long petControlAddr)
    {
        int count = 0;
        foreach (RoomGlyph glyph in EnumerateGlyphs(mem, petControlAddr))
        {
            if (RoomFlags.IsNamedRoom(glyph.RoomFlags))
                continue;
            if (glyph.Mode == GameOffsets.RgmAssignedRoom || glyph.Mode == GameOffsets.RgmPrevAssignedRoom)
                count++;
        }
        return count;
    }

    /// <summary>Derives the stateroom class the player has achieved from <see cref="CountEverAssignedRooms"/>:
    /// 0 = none, 1 = SGT/3rd Class, 2 = 2nd Class, 3 = 1st Class. Clamped to 3 in case of an unexpected extra
    /// glyph.</summary>
    public static int GetAchievedStateroomClass(MemoryReader mem, long petControlAddr) =>
        Math.Min(CountEverAssignedRooms(mem, petControlAddr), 3);

    /// <summary>Reads a CPetRoomsGlyph's _mode (see GameOffsets.RgmUnassigned/RgmAssignedRoom/RgmPrevAssignedRoom).</summary>
    public static int? ReadGlyphMode(MemoryReader mem, long glyphAddr) =>
        mem.ReadInt32(glyphAddr + GameOffsets.PetRoomsGlyphModeOffset);

    /// <summary>Writes a CPetRoomsGlyph's _mode directly.</summary>
    public static bool WriteGlyphMode(MemoryReader mem, long glyphAddr, int mode) =>
        mem.WriteInt32(glyphAddr + GameOffsets.PetRoomsGlyphModeOffset, mode);
}
