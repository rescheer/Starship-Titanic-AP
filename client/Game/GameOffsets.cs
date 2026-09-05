namespace StarshipTitanicAp;

public static class GameOffsets
{
    // --- Root chain: base -> step1 -> step2 -> CGameManager ---
    public const long Step1 = 0x5F30C58;
    public const long Step2 = 0xD0;
    public const long GameManager = 0x28;

    // --- Room/node/view, relative to CGameManager ---
    private const long NodeViewBase = 0xE728 + 0x8;
    public const long RoomFromGameManager = NodeViewBase + 0x10;
    public const long NodeFromGameManager = NodeViewBase + 0x14;
    public const long ViewFromGameManager = NodeViewBase + 0x18;

    // --- Passenger class, relative to CGameManager ---
    // 1=First, 2=Second, 3=Third, 4=None
    public const long PassengerClass = 0xE770;

    // --- PET active flag, relative to CGameManager ---
    public const long PetActive = 0xE780;

    // --- Inventory tree, relative to CGameManager ---
    public const long Project = 0xE718;

    // --- Function offsets (module-relative, for remote calls) ---
    public const long MoveItemFunc = 0x242AA30;    // CTreeItem detach()+attach() wrapper

    // CGameObject::loadFrame(int frameNumber)
    public const long LoadFrameFunc = 0x239EBE0;
    public const long MarkAllDirtyFunc = 0x246C840; // CGameManager::markAllDirty()
    public const long GetPetControlFunc = 0x23A1E30; // CGameObject::getPetControl()
    public const long PetControlResetFunc = 0x2429D10; // CPetControl::reset()
    public const long InventoryItemsChangedFunc = 0x242F890; // CPetInventory::itemsChanged()
    public const long PetInventoryFieldOffset = 0x6D8; // CPetControl::_inventory member offset

    // CPetControl::_conversations member offset
    public const long PetConversationsFieldOffset = 0x168;
    public const long SetAreaFunc = 0x242A150; // CPetControl::setArea()
    public const long DisplayMessageFunc = 0x242A6C0; // CPetControl::displayMessage(StringId, int)
    public const long DisplayMessageTextFunc = 0x242A730; // CPetControl::displayMessage(const CString&, int)

    // CPetControl's own current-area field - which tab is visibly active
    // (Inventory/Conversation/Controller/Chevron/Save-Load)
    public const long PetControlCurrentAreaOffset = 0x1928;

    // CPetControl area codes (tabs)
    public const int PetAreaInventory = 0;
    public const int PetAreaConversation = 1;
    public const int PetAreaController = 2;
    public const int PetAreaChevron = 3;
    public const int PetAreaSaveLoad = 4;

    // CGameObject::setPassengerClass() - the DeskBot's own vanilla class-upgrade trigger
    public const long SetPassengerClassFunc = 0x23A2740;

    // CGameObject::petReassignRoom() - called by the DeskBot right after setPassengerClass();
    // same calling convention (rcx = this/gameManager, edx = class number). This is just a wrapper: it calls
    // getPetControl() on rcx, computes CPetRooms* = result+PetRoomsOffset, then tail-jumps into the real
    // CPetRooms::reassignRoom() body (see ReassignRoomBodyFunc below) with rcx=that CPetRooms*, edx=class number
    // unchanged. RoomAssignHook hooks THIS address, since it's the one real natural callers (the DeskBot) use.
    public const long PetReassignRoomFunc = 0x23A3230;

    // CPetRooms::reassignRoom(PassengerClass) - the real body PetReassignRoomFunc's wrapper tail-jumps into.
    // Derived from that wrapper's own jmp displacement, then confirmed live: reads _elevatorBroken at
    // [this+0x284] (matching CPetRooms::reassignRoom's known source), operates on _glyphs at [this+0x10]
    // (matches PetRoomsGlyphsOffset) and writes glyph->_mode at [glyph+0x48] (matches
    // PetRoomsGlyphModeOffset) - an exact structural match, not just a similar-looking address. Callable
    // directly with rcx = petControlAddr + PetRoomsOffset (CPetRooms* this), edx = class number - this skips
    // the wrapper's own getPetControl() call entirely, which matters because that call climbs the CTreeItem
    // parent chain starting from rcx, and going in with rcx=gameManager (as the wrapper does) crashed with an
    // access violation two hops up a bogus parent chain - gameManager is reached via a separate static offset
    // chain (Step1/Step2/GameManager), not via the tree, so it isn't a valid climb start point. getPetControl()
    // itself is fine when called with a real tree-embedded item address (see PetMoveToHiddenRoomFunc, which
    // already does this successfully via RemoteCaller) - the bug is specific to using gameManager for it.
    public const long ReassignRoomBodyFunc = 0x2437440;

    // Previous PassengerClass value, written by setPassengerClass() right
    // before it overwrites PassengerClass with the new one.
    public const long PreviousPassengerClass = 0xE774;

    // CNamedItem::_name field offset, relative to the object's own base
    public const long NamedItemNameOffset = 0x30;

    // CString(const char*) constructor (BaseString<char>'s)
    public const long CStringCharPtrCtorFunc = 0x3783980;

    // --- CPetConversations line-log append ("addLine()") ---
    public const long ConversationAddLineFunc = 0x240E260;

    // Offset from CPetConversations' own base to the sub-object addLine() operates on
    public const long ConversationSubObjectOffset = 0x420;

    // r8 at the addLine call site - CPetConversations::getColor(1)'s return value
    public const long ConversationAddLineKnownGoodR8 = 0x10101;

    // --- CPetConversations::displayMessage(const CString&) ---
    public const long PetConversationsDisplayMessageFunc = 0x242C700;

    // CGameObject::_resource field offset (a CString), relative to the object's own base.
    // STRICTLY READ-ONLY.
    public const long GameObjectResourceOffset = 0xD8;

    // --- Full CGameObject field layout, in declared order ---
    //   Offset   Size   Field                  Notes
    //   0x30     0x28   _name                  CNamedItem's CString - see NamedItemNameOffset
    //   0x58     0x8    _unused1               double, unused
    //   0x60     0x8    _unused2               double, unused
    //   0x68     0x8    _unused3               double, unused (see GameObjectUnused3Offset below)
    //   0x70     0x1    _nonvisual             packed with the 3 toggle bytes below (dword 00 F0 F0 FF)
    //   0x71     0x1    _toggleR               0xF0
    //   0x72     0x1    _toggleG               0xF0
    //   0x73     0x1    _toggleB               0xFF
    //   0x78     0x18   _movieClips            funcptr@0x78, self-ptr pair@0x80/0x88 (empty intrusive list)
    //   0x90     0x4    _initialFrame          int, 0
    //   0x94     0x4    (padding)
    //   0x98     0x18   _movieRangeInfoList    same shape as _movieClips
    //   0xB0     0x4    _frameNumber           int, -1
    //   0xB4     0x4    (padding)
    //   0xB8     0x8    _text                  pointer, null
    //   0xC0     0x4    _textBorder            uint, 0
    //   0xC4     0x4    _textBorderRight       uint, 0
    //   0xC8     0x4    _savedPos              Common::Point - single dword store implies packed int16 x/y
    //   0xCC     0x4    (padding)
    //   0xD0     0x8    _surface               pointer, null - see GameObjectResourceOffset
    //   0xD8     0x28   _resource              CString - see GameObjectResourceOffset
    //   0x100    0x4    _unused4               int, 0
    //   0x104    0x8    _bounds                Rect - left/top@0x104 (0,0), right/bottom@0x108 (packed F000F = 15,15)
    //   0x10C    0x1    _isPendingMail         bool, false - see ItemIsPendingMail
    //   0x110    0x4    _destRoomFlags         uint, 0 - see ItemDestRoomFlags
    //   0x114    0x4    _roomFlags             uint, 0 - see ItemRoomFlags
    //   0x118    0x1    _handleMouseFlag       bool, false
    //   0x11C    0x4    _cursorId              CURSOR_ARROW = 1
    //   0x120    0x1    _visible               bool, true
    //
    // _unused4 is this app's own per-item state storage (see ItemPersistedState.cs).
    // _unused3 is used by SaveSeedGuard.cs, but only on the BeamBridge (Red Fuse) item.
    // _unused1/_unused2 remain available, unused.
    public const long GameObjectUnused1Offset = 0x58;
    public const long GameObjectUnused2Offset = 0x60;
    public const long GameObjectUnused3Offset = 0x68;
    public const long GameObjectNonvisualOffset = 0x70;
    public const long GameObjectToggleROffset = 0x71;
    public const long GameObjectToggleGOffset = 0x72;
    public const long GameObjectToggleBOffset = 0x73;
    public const long GameObjectMovieClipsOffset = 0x78;
    public const long GameObjectInitialFrameOffset = 0x90;
    public const long GameObjectMovieRangeInfoListOffset = 0x98;
    public const long GameObjectFrameNumberOffset = 0xB0;
    public const long GameObjectTextOffset = 0xB8;
    public const long GameObjectTextBorderOffset = 0xC0;
    public const long GameObjectTextBorderRightOffset = 0xC4;
    public const long GameObjectSavedPosOffset = 0xC8;
    public const long GameObjectSurfaceOffset = 0xD0;
    public const long GameObjectUnused4Offset = 0x100;
    public const long GameObjectBoundsOffset = 0x104;
    public const long GameObjectHandleMouseFlagOffset = 0x118;
    public const long GameObjectCursorIdOffset = 0x11C;
    public const long GameObjectVisibleOffset = 0x120;

    // CCarry::_canTake - gates whether the item can be picked up at all, independent of _visible/_cursorId.
    public const long CarryCanTakeOffset = 0x1E8;

    // CGetLiftEye2::MouseDragStartMsg (the broken elevator's "take the Eye" hotspot) - confirmed live via
    // disassembly. The function is `bool result = checkPoint(msg->_mousePos, false, true); if (result) {...side
    // effects...} return result;`, compiled with a single shared epilogue for both paths:
    //   0x...4000: prologue (push r15/r14/r13/r12/rdi/rsi/rbx; sub rsp,0xA0), calls checkPoint(), `jne` to Body.
    //   0x...4031: shared epilogue - `mov eax, r12d` (r12d holds checkPoint's own bool result) then pop/ret.
    //     Reached directly when checkPoint() returns false, and via a jmp back at the end of Body otherwise.
    //   0x...4050: Body - the "checkPoint succeeded" side effects (sets own _cursorId/_visible, forwards a
    //     CPassOnDragStartMsg to the real Eye2 item - notably WITHOUT ever consulting that item's own _canTake,
    //     which is why gating _canTake on the real Eye2 CCarry item has no effect on this pickup path at all).
    // GetLiftEye2GateHook hooks Body's entry to add our own gate on top of checkPoint's own result.
    public const long GetLiftEye2MouseDragBodyFunc = 0x23E4050;

    // The shared epilogue described above - jumping here directly (with r12d=0) reproduces exactly what a failed
    // checkPoint() would have done: `return false`, with no vanilla message (CGetLiftEye2 shows none on failure).
    public const long GetLiftEye2MouseDragEpilogueFunc = 0x23E4031;

    // CScraliontisTable::MaitreDDefeatedMsg(CMaitreDDefeatedMsg*) - defeating the MaitreD.
    // Confirmed live: writes _cursorId=4 (CURSOR_MOVE_FORWARD, at the standard GameObjectCursorIdOffset)
    // and _fieldEC=true, then returns true. Both writes gate whether the table can be entered - toggling
    // _fieldEC back to false alone was enough to block entry again.
    public const long MaitreDDefeatedMsgFunc = 0x2416CD0;
    public const long ScraliontisTableFieldECOffset = 0x1AC;
    public const int ScraliontisTableEnterableCursorId = 4; // CURSOR_MOVE_FORWARD

    // CGameObject::petMoveToHiddenRoom() - stashes an item under the hidden room
    public const long PetMoveToHiddenRoomFunc = 0x23A31D0;

    // CShipSetting (titanic/game/ship_setting.h) - a Fuse Box socket. Confirmed live via ScanForCStrings on
    // the 4 sockets under room 37/node 12/view 1: _itemName holds the installed CBridgePiece's name (e.g.
    // "BeamBridge"), or "NULL" when empty. EnterRoomMsg picks the socket's displayed frame purely off this
    // field, independent of where the real CBridgePiece object actually lives - so a raw petMoveToHiddenRoom()
    // on the item (as our reconciliation does) never updates it, leaving the socket showing a "ghost" fuse.
    public const long ShipSettingItemNameOffset = 0x1B8; // CString: size (int32) @ +0, data ptr (int64) @ +8

    // CShipSetting._frameTarget - name of the movie/graphic object (e.g. "BeamAnim") whose displayed frame
    // shows the installed fuse's icon. AddHeadPieceMsg/MouseDragStartMsg execute a CSetFrameMsg against this
    // named object directly (not against the CShipSetting itself), which is why fixing only _itemName leaves
    // the sprite showing until the room/view is re-entered.
    public const long ShipSettingFrameTargetOffset = 0x1E0; // CString: size (int32) @ +0, data ptr (int64) @ +8
    public const int CursorArrow = 1;

    // CPetControl::moveToHiddenRoom() itself
    public const long PetControlMoveToHiddenRoomFunc = 0x242AB40;

    // --- PET talk input hook ---
    public const long TextLineEnteredFunc = 0x242D6A0; // CPetConversations::textLineEntered()
    public const long ClearTextControlFunc = 0x240CED0; // CTextControl::setup() - clears the input box
    public const long TextInputFieldOffset = 0x4B0; // CPetConversations::_textInput, relative to the object itself

    // --- Mail-related fields, relative to a CGameObject (item) itself, NOT gameManager ---
    public const long ItemIsPendingMail = 0x10C;
    public const long ItemDestRoomFlags = 0x110;
    public const long ItemRoomFlags = 0x114;

    // Sentinel written into _destRoomFlags for items this app has delivered to the mail system.
    public const uint ToolPlacedSentinel = 0xFFFFFFFF;

    // CTreeItem layout: +0x08 _parent, +0x10 _nextSibling, +0x18 _priorSibling, +0x20 _firstChild
    // (HeaderOffset accounts for CMessageTarget's vtable pointer at +0x0.)
    private const long HeaderOffset = 0x8;
    public const long Parent = HeaderOffset + 0x00;

    // CPetControl's own current room-flags value (see RoomFlags.cs), relative
    // to the PET control's own base address.
    public const long PetControlCurrentRoomFlags = 0x1110;

    // --- CPetRooms / CPetRoomsGlyph (titanic/pet_control/pet_rooms{,_glyphs}.cpp) ---
    // Confirmed live via breakpoint + disassembly of the real CPetRooms::reassignRoom body (the previously-known
    // PetReassignRoomFunc above is just a name-lookup wrapper that eventually tail-jumps into it - its own offset
    // is apparently stale/build-specific and wasn't chased further). reassignRoom's body:
    //   glyph = _glyphs.findAssignedRoom(); if (glyph) glyph->_mode = RGM_PREV_ASSIGNED_ROOM;   // mov [rax+48],2
    //   roomFlags.setRandomLocation(passClass, _elevatorBroken);                                 // reads [this+284]
    //   glyph = addRoom(roomFlags, true); if (glyph) { glyph->_mode = RGM_ASSIGNED_ROOM; ... }    // mov [rax+48],1
    // addRoom's dedup check (moduleBase+FindGlyphByFlagsFunc) walks _glyphs' linked list (list node layout:
    // node+0x08 = next, node+0x10 = pointer to the CPetRoomsGlyph payload) comparing each glyph's _roomFlags
    // (glyph+0x40) against the target, returning the matching CPetRoomsGlyph* (or falsy) in rax - a pure,
    // side-effect-free query safe to call directly via RemoteCaller.CallAndGetResult(rcx=&_glyphs, rdx=roomFlags).
    // This is what MainForm.GameLogic.cs's SGT-glyph spoof calls to find and re-flag the player's original SGT
    // Class Stateroom's glyph as RGM_ASSIGNED_ROOM while they're back in it, working around reassignRoom() having
    // flagged it RGM_PREV_ASSIGNED_ROOM once AP items pushed a real 2nd/1st class room assignment out of order
    // (see TryApplyClassUpgradeSpoof, the separate/older PassengerClass-value spoof this complements).
    public const int RgmUnassigned = 0;
    public const int RgmAssignedRoom = 1;
    public const int RgmPrevAssignedRoom = 2;

    // CPetRooms's own embedded offset within CPetControl (i.e. CPetRooms's `this` == petControlAddr + this).
    // Confirmed live via two independent reassignRoom breakpoints, cross-checked against this app's own
    // _currentInventoryRoom printout (Debug tab's "List PET Room Glyphs") rather than a hand-supplied address -
    // an earlier pass got 0xFB0 from a petControlAddr that wasn't actually sourced from this same printout.
    public const long PetRoomsOffset = 0xFF0;

    // CPetRooms::_glyphs (a CPetGlyphs), relative to CPetRooms's own base.
    public const long PetRoomsGlyphsOffset = 0x10;

    // CPetRoomsGlyph field offsets, relative to the glyph object's own base (as returned by FindGlyphByFlagsFunc).
    public const long PetRoomsGlyphRoomFlagsOffset = 0x40; // uint
    public const long PetRoomsGlyphModeOffset = 0x48;      // RoomGlyphMode (int)

    // CPetRoomsGlyphs' internal linked-list search (used here as findGlyphByFlags): call with
    // rcx = petControlAddr + PetRoomsOffset + PetRoomsGlyphsOffset, rdx = target roomFlags (zero-extended uint).
    // Returns the matching CPetRoomsGlyph* in rax (module-relative; add mem.ModuleBase before calling).
    public const long FindGlyphByFlagsFunc = 0x2438460;
    public const long NextSibling = HeaderOffset + 0x08;
    public const long PriorSibling = HeaderOffset + 0x10;
    public const long FirstChild = HeaderOffset + 0x18;

    // CParrotNutBowlActor::_state (titanic/game/parrot_nut_bowl_actor.h) - the Pistachio Bowl puzzle's progress:
    // 0 before the nut-rustle, 1 once the parrot-eats-the-nuts animation has played, 2 once the bowl has unlocked
    // and renders pickable. Confirmed live (byte values 0x00/0x01/0x02 observed through the sequence). This is
    // the class's second declared field - a bool _puzzleDone (1 byte) is declared first in source and, per the
    // same layout rule confirmed for CCarry's own first derived field (ItemFieldsForm.cs's "_unused5 (CCarry
    // candidate)" landing 4-byte-aligned at 0x124 after CGameObject's 1-byte _visible at 0x120), would sit
    // unaligned right at 0x121 with no padding (it only needs 1-byte alignment) - pushing _state to the next
    // 4-byte boundary at 0x124, which matches what was observed here. _puzzleDone itself is unconfirmed/unused.
    public const long ParrotNutBowlActorStateOffset = 0x124;
    public const int ParrotNutBowlActorStateUnlocked = 2; // bowl unlocked, ear rendered pickable

    // CLight::_eyePresent (titanic/game/light.h) - true while the light fixture still holds its bulb, flipped
    // false by CLight::ActMsg("Eye Removed") once the bulb/eye is taken. Confirmed live via before/after diff on
    // the four CLight fixtures at Room 7 ("1stClassState") / Node 6 / View 4 ("6WTL"/"6WTR"/"6WBL"/"6WBR") - only
    // "6WTL" (the fixture holding Titania's Eye (Light)/Eye1) changed, 0x01 -> 0x00, at this offset, the instant
    // the bellbot hand-off cutscene finished. The other three fixtures and every byte outside this offset (across
    // a 0x800-byte window) were unchanged. Not yet wired to anything - the Eye1 pickup check currently relies on
    // the item's normal full-state-machine tracking (ReconcileTrackedItems) once the CEye object itself reparents
    // into the inventory room, which was observed to fire correctly on a second attempt after an earlier miss;
    // this offset is saved here in case that natural-pickup detection turns out to need a direct assist.
    public const long LightEyePresentOffset = 0x194;

    // Window scanned for a name-string pointer on each tree node.
    public const long NameScanStart = HeaderOffset + 0x20;
    public const long NameScanEnd = HeaderOffset + 0x60;
    public const long NameScanStep = 0x8;
}
