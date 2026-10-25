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
    // rcx = this/gameManager, edx = class number. A wrapper: calls getPetControl() on rcx, computes
    // CPetRooms* = result+PetRoomsOffset, then tail-jumps into ReassignRoomBodyFunc. Hooked by RoomAssignHook.
    public const long PetReassignRoomFunc = 0x23A3230;

    // CPetRooms::reassignRoom(PassengerClass) - the real body PetReassignRoomFunc's wrapper tail-jumps into.
    // Call with rcx = petControlAddr + PetRoomsOffset (CPetRooms* this), edx = class number.
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
    // _unused1 is this app's own tool-placed/game-placed mail marker (see ToolPlacedSentinel below).
    // _unused2 remains available, unused.
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

    // CTelevision::_channel4Glyph (titanic/game/television.h) - a STATIC bool, so a module-relative global
    // (add mem.ModuleBase), not a field on the CTelevision object. Gates the channel-4 picture in
    // CTelevision::PETActivateMsg (`_channelNum == 4 && _channel4Glyph`); CEye clears it once Eye1 is taken.
    // Saved/loaded with each CTelevision, and reset to true by CTelevision::init().
    public const long TelevisionChannel4GlyphStatic = 0x5F32601;

    // CGetLiftEye2::_destObject (titanic/game/get_lift_eye2.h) - a STATIC `CString *`, so this module-relative
    // global holds a pointer to a heap-allocated CString (allocated once by CGetLiftEye2::init()), not the string
    // itself. Holds the name of the eye placed into the broken lift ("Eye1"/"Eye2") - set by CGetLiftEye2::ActMsg,
    // reset to "NULL" by MouseDragStartMsg when that eye is taken back out. CTelevision::PETActivateMsg only shows
    // channel 5 (`loadFrame(393 + _eyeFloorNum)`) while it's != "NULL".
    public const long GetLiftEye2DestObjectPtrStatic = 0x5F322B0;

    // CString (ScummVM Common::String) layout: _size (uint32) @ +0, _str (char*) @ +8, inline _storage @ +0x10 - _str points at the inline buffer for short
    // strings, or at a separately-allocated (possibly refcount-shared) buffer for long ones.
    public const long CStringSizeOffset = 0x0;
    public const long CStringDataPtrOffset = 0x8;
    public const long CStringInlineStorageOffset = 0x10;
    public const int CStringInlineCapacity = 0x28 - 0x10;

    // CGetLiftEye2::MouseDragStartMsg (the broken elevator's "take the Eye" hotspot):
    // `bool result = checkPoint(msg->_mousePos, false, true); if (result) {...} return result;`, with one shared
    // epilogue for both paths.
    //   0x...4000: prologue, calls checkPoint(), `jne` to Body.
    //   0x...4031: shared epilogue - `mov eax, r12d` (r12d holds checkPoint's bool result) then pop/ret.
    //   0x...4050: Body - the "checkPoint succeeded" side effects (sets own _cursorId/_visible, forwards a
    //     CPassOnDragStartMsg to the real Eye2 item without consulting its _canTake).
    // GetLiftEye2GateHook hooks Body's entry.
    public const long GetLiftEye2MouseDragBodyFunc = 0x23E4050;

    // The shared epilogue above. Jumping here with r12d=0 returns false, with no vanilla message.
    public const long GetLiftEye2MouseDragEpilogueFunc = 0x23E4031;

    // CScraliontisTable::MaitreDDefeatedMsg(CMaitreDDefeatedMsg*) - defeating the MaitreD. Writes _cursorId=4
    // (CURSOR_MOVE_FORWARD) and _fieldEC=true, then returns true. Both fields gate whether the table can be entered.
    public const long MaitreDDefeatedMsgFunc = 0x2416CD0;
    public const long ScraliontisTableFieldECOffset = 0x1AC;
    public const int ScraliontisTableEnterableCursorId = 4; // CURSOR_MOVE_FORWARD

    // CGameObject::petMoveToHiddenRoom() - stashes an item under the hidden room
    public const long PetMoveToHiddenRoomFunc = 0x23A31D0;

    // CShipSetting (titanic/game/ship_setting.h) - a Fuse Box socket (room 37/node 12/view 1). _itemName holds the
    // installed CBridgePiece's name (e.g. "BeamBridge"), or "NULL" when empty. EnterRoomMsg picks the socket's
    // displayed frame purely off this field.
    public const long ShipSettingItemNameOffset = 0x1B8; // CString: size (int32) @ +0, data ptr (int64) @ +8

    // CShipSetting._frameTarget - name of the movie/graphic object (e.g. "BeamAnim") whose displayed frame
    // shows the installed fuse's icon. AddHeadPieceMsg/MouseDragStartMsg execute a CSetFrameMsg against this
    // named object directly.
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

    // Sentinel written into _unused1 (GameObjectUnused1Offset) for items this app has delivered to the mail
    // system, distinguishing a tool-placed item from one the game itself placed.
    public const uint ToolPlacedSentinel = 0xFFFFFFFF;

    // CTreeItem layout: +0x08 _parent, +0x10 _nextSibling, +0x18 _priorSibling, +0x20 _firstChild
    // (HeaderOffset accounts for CMessageTarget's vtable pointer at +0x0.)
    private const long HeaderOffset = 0x8;
    public const long Parent = HeaderOffset + 0x00;

    // CPetControl's own current room-flags value (see RoomFlags.cs), relative
    // to the PET control's own base address.
    public const long PetControlCurrentRoomFlags = 0x1110;

    // --- CPetRooms / CPetRoomsGlyph (titanic/pet_control/pet_rooms{,_glyphs}.cpp) ---
    // CPetRooms::reassignRoom's body:
    //   glyph = _glyphs.findAssignedRoom(); if (glyph) glyph->_mode = RGM_PREV_ASSIGNED_ROOM;   // mov [rax+48],2
    //   roomFlags.setRandomLocation(passClass, _elevatorBroken);                                 // reads [this+284]
    //   glyph = addRoom(roomFlags, true); if (glyph) { glyph->_mode = RGM_ASSIGNED_ROOM; ... }    // mov [rax+48],1
    // addRoom's dedup check (moduleBase+FindGlyphByFlagsFunc) walks _glyphs' linked list (list node layout:
    // node+0x08 = next, node+0x10 = pointer to the CPetRoomsGlyph payload) comparing each glyph's _roomFlags
    // (glyph+0x40) against the target, returning the matching CPetRoomsGlyph* (or falsy) in rax - a side-effect-free
    // query, callable via RemoteCaller.CallAndGetResult(rcx=&_glyphs, rdx=roomFlags).
    public const int RgmUnassigned = 0;
    public const int RgmAssignedRoom = 1;
    public const int RgmPrevAssignedRoom = 2;

    // CPetRooms's own embedded offset within CPetControl (i.e. CPetRooms's `this` == petControlAddr + this).
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
    // and renders pickable. Re-entering the view before the bowl is taken resets it to 0.
    public const long ParrotNutBowlActorStateOffset = 0x124;
    public const int ParrotNutBowlActorStateUnlocked = 2; // bowl unlocked, ear rendered pickable

    // CLight::_eyePresent (titanic/game/light.h) - true while the light fixture still holds its bulb, flipped
    // false by CLight::ActMsg("Eye Removed") once the bulb/eye is taken. The "6WTL" fixture (Room 7 / Node 6 /
    // View 4) holds Titania's Eye (Light)/Eye1.
    public const long LightEyePresentOffset = 0x194;

    // CProjectItem::loadGame(int slotId) (titanic/core/project_item.cpp) - the single path every save load (PET
    // Save/Load tab, ScummVM's own menu/launcher) and new game goes through. It does NOT replace the
    // CProjectItem or CGameManager - both live for the whole process - it destroys every child of the project
    // and re-parents freshly loaded ones under the same `this`, so every cached tree address goes stale while
    // _project itself never changes. Its prologue (8 pushes + sub rsp,0xC8 = 19 bytes) has no RIP-relative
    // operands; SaveLoadHook replays it verbatim.
    public const long ProjectLoadGameFunc = 0x23A63A0;

    // CProjectItem::postLoad() - called exactly once, from the tail of loadGame, after the loaded children have
    // been re-parented under the project (so the new tree is complete by the time it runs).
    public const long ProjectPostLoadFunc = 0x23A5410;

    // Target of the `lea rdx,[rip-0xCFD]` at ProjectPostLoadFunc+0x6 (the devirtualization check against
    // CProjectItem::getGameManager's address). SaveLoadHook's stub rebuilds it as an absolute mov.
    public const long ProjectPostLoadLeaTarget = 0x23A4720;

    // Window scanned for a name-string pointer on each tree node.
    public const long NameScanStart = HeaderOffset + 0x20;
    public const long NameScanEnd = HeaderOffset + 0x60;
    public const long NameScanStep = 0x8;
}
