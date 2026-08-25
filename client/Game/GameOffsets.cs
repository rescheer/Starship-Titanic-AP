namespace StarshipTitanicAp;

/// <summary>
/// All offsets here were found and verified via manual reverse engineering
/// (Cheat Engine + x64dbg + ScummVM Titanic engine source), and confirmed
/// stable across multiple relaunches. See the original Python prototypes
/// (track_final.py, list_inventory.py) for the full derivation history.
/// </summary>
public static class GameOffsets
{
    // --- Root chain: base -> step1 -> step2 -> CGameManager ---
    public const long Step1 = 0x5F30C58;
    public const long Step2 = 0xD0;
    public const long GameManager = 0x28;

    // --- Room/node/view, relative to CGameManager ---
    // The persistent room/node/view holder is a nested object at
    // gameManager+0xE728, not gameManager itself.
    private const long NodeViewBase = 0xE728 + 0x8;
    public const long RoomFromGameManager = NodeViewBase + 0x10;
    public const long NodeFromGameManager = NodeViewBase + 0x14;
    public const long ViewFromGameManager = NodeViewBase + 0x18;

    // --- Passenger class, relative to CGameManager ---
    // Confirmed via 4-way save-file diff: 1=First, 2=Second, 3=Third, 4=None
    public const long PassengerClass = 0xE770;

    // --- PET active flag, relative to CGameManager ---
    // Confirmed via before/after byte diff toggling ScummVM's "pet on"/"pet off"
    // debug console commands: clean 0/1 flip, no other candidates.
    public const long PetActive = 0xE780;

    // --- Inventory tree, relative to CGameManager ---
    public const long Project = 0xE718;

    // --- Function offsets (module-relative, for remote calls) ---
    public const long MoveItemFunc = 0x242AA30;    // CTreeItem detach()+attach() wrapper
    public const long MarkAllDirtyFunc = 0x246C840; // CGameManager::markAllDirty()
    public const long GetPetControlFunc = 0x23A1E30; // CGameObject::getPetControl()
    public const long PetControlResetFunc = 0x2429D10; // CPetControl::reset() - confirmed via setPassengerClass()
    public const long InventoryItemsChangedFunc = 0x242F890; // CPetInventory::itemsChanged() - confirmed via addToInventory()
    public const long PetInventoryFieldOffset = 0x6D8; // CPetControl::_inventory member offset
    public const long SetAreaFunc = 0x242A150; // CPetControl::setArea() - confirmed via addToInventory(), called right after itemsChanged()
    public const long DisplayMessageFunc = 0x242A6C0; // CPetControl::displayMessage(StringId, int) - confirmed via class-restriction message trace
    public const long DisplayMessageTextFunc = 0x242A730; // CPetControl::displayMessage(const CString&, int) - the free-text overload

    // CGameObject::setPassengerClass() - the DeskBot's own vanilla
    // class-upgrade trigger (see Memory/ClassUpgradeHook.cs). Confirmed
    // live via disassembly: calls CGameObject::getPetControl() (0x23A1E30)
    // then tail-jumps into CPetControl::reset() (0x2429D10) - both known
    // addresses resolve to the same module base from this function's own
    // address, cross-confirming it.
    public const long SetPassengerClassFunc = 0x23A2740;

    // Previous PassengerClass value, written by setPassengerClass() right
    // before it overwrites PassengerClass with the new one. Not currently
    // used for anything, but confirmed live in the same disassembly pass
    // as SetPassengerClassFunc, so recorded here in case it's useful later
    // (e.g. detecting what class a blocked upgrade attempt would have set).
    public const long PreviousPassengerClass = 0xE774;

    // --- PET talk input hook, confirmed via live disassembly trace ---
    public const long TextLineEnteredFunc = 0x242D6A0; // CPetConversations::textLineEntered()
    public const long ClearTextControlFunc = 0x240CED0; // CTextControl::setup() - clears the input box, called by textLineEntered()
    public const long TextInputFieldOffset = 0x4B0; // CPetConversations::_textInput, relative to the object itself

    // --- Mail-related fields, relative to a CGameObject (item) itself, NOT gameManager ---
    // Confirmed live via chevron code round-trip (Napkin sent to "Bar", 0xB3D97 found at +0x114).
    public const long ItemIsPendingMail = 0x10C;
    public const long ItemDestRoomFlags = 0x110;
    public const long ItemRoomFlags = 0x114;

    // Sentinel written into _destRoomFlags for items THIS APP has
    // delivered to the mail system (see GameActions.MarkItemAsToolPlaced).
    // Once an item is actually delivered (_roomFlags != 0), the real
    // game's own findMailByFlags() never consults _destRoomFlags again -
    // it's dead weight we can safely reuse. All legitimate chevron/
    // room-flags values are packed from a handful of small bitfields
    // (ELEVATOR/PASSENGER_CLASS/FLOOR/ROOM - see the engine's own
    // room_flags.cpp) and the 13 known SuccUBus codes in ChevronCodes,
    // none of which come anywhere near the top of the 32-bit range, so a
    // full-width sentinel like this can never collide with a real value.
    // Being part of the object's own serialized fields, this survives
    // detach/reattach, game restarts, and save/reload exactly like the
    // item's real location does - no external bookkeeping needed.
    public const uint ToolPlacedSentinel = 0xFFFFFFFF;

    // CTreeItem layout (confirmed via tree_item.h + live probing):
    //   +0x08 _parent, +0x10 _nextSibling, +0x18 _priorSibling, +0x20 _firstChild
    // (HeaderOffset accounts for CMessageTarget's vtable pointer at +0x0.)
    private const long HeaderOffset = 0x8;
    public const long Parent = HeaderOffset + 0x00;
    public const long NextSibling = HeaderOffset + 0x08;
    public const long FirstChild = HeaderOffset + 0x18;

    // Window scanned for a name-string pointer on each tree node.
    public const long NameScanStart = HeaderOffset + 0x20;
    public const long NameScanEnd = HeaderOffset + 0x60;
    public const long NameScanStep = 0x8;
}
