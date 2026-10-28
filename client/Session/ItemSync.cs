namespace StarshipTitanicAp;

/// <summary>Outcome of <see cref="ItemSync.ReturnToInventory"/>.</summary>
internal enum ReturnItemResult
{
    NotInGame,
    UnknownItem,
    NotGranted,
    NotFound,
    AlreadyInInventory,
    InstalledInFuseBox,
    Restored,
    MoveFailed,
    Returned,
}

/// <summary>Keeps the physical carry items in the game in line with AP: sends pickup checks, hides items picked up
/// before they're granted, delivers granted items by mail, and temporarily restores granted-but-unfound items to
/// their home spot (reverting them if the player leaves without picking them up).</summary>
internal sealed class ItemSync
{
    private const int DirtyReassertDelayTicks = 4; // 4 * 50ms = 200ms
    private const int UnrestoreCheckDelayTicks = ClientSession.InventoryIntervalTicks + 4; // let ReconcileTrackedItems run at least once, plus buffer

    /// <summary>Items whose delayed unrestore decision is deferred while the player is still at the given RNV, because
    /// that RNV is reached mid-pickup rather than by walking away.</summary>
    private static readonly Dictionary<string, RoomNodeView> DeferUnrestoreAtRnv = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Eye1"] = new(7, 11, 1),       // the bellbot's Titania's Eye (Light) dialogue/hand-off cutscene
        ["CrushedTV"] = new(38, 12, 1), // CCrushedTV::MouseDragStartMsg changes view here while the TV is still being dragged
    };

    private readonly SessionContext _ctx;

    private int? _pendingDirtyReassertTick;

    /// <summary>Items still Restored at the RNV they were just left from, awaiting a delayed re-check (see
    /// TryUnrestoreItemsLeavingRnv).</summary>
    private readonly List<(string ItemName, long ItemAddress, int DueTick)> _pendingUnrestoreChecks = new();

    public ItemSync(SessionContext ctx)
    {
        _ctx = ctx;
    }

    /// <summary>Drops everything tracked against the current game tree. Call when the tree is rebuilt (save load).</summary>
    public void Reset()
    {
        _pendingUnrestoreChecks.Clear();
        _eatenMagazineAddr = null;
    }

    /// <summary>Re-marks the whole tree dirty once a scheduled reassert is due (see ScheduleDirtyReassert).</summary>
    public void ProcessPendingDirtyReassert(long gameManager)
    {
        if (_pendingDirtyReassertTick is { } dueTick && _ctx.TickCount >= dueTick)
        {
            _pendingDirtyReassertTick = null;
            GameActions.MarkAllDirty(_ctx.Mem, gameManager);
        }
    }

    /// <summary>Runs the full-tree item-state reconciliation pass for every tracked item.</summary>
    public void ReconcileTrackedItems(long gameManager)
    {
        if (_ctx.Project is not { } project)
            return;
        if (_ctx.InventoryRoom is null || _ctx.MailManRoom is null)
            return;

        List<CarryItemLocation> items = GameState.FindAllCarryItems(_ctx.Mem, project);
        string[] receivedItems = _ctx.Ap.GetReceivedItemNames();
        bool anyMailChange = false;

        SyncLiftEyeGate(items, receivedItems);
        TryHideOrDeliverFeather(items, receivedItems, gameManager);

        foreach (CarryItemLocation item in items)
        {
            bool isCarryParrot = string.Equals(item.Name, ItemTracking.CarryParrotName, StringComparison.OrdinalIgnoreCase);
            bool isServerGranted = ItemTracking.ServerGrantedItemNames.TryGetValue(item.Name, out string? serverGrantedApItemName);
            bool isTracked = isCarryParrot
                || isServerGranted
                || ItemTracking.IsOneDirectionalItem(item.Name)
                || ItemTracking.IsFullStateMachineItem(item.Name);
            if (!isTracked)
                continue;

            if (isCarryParrot)
            {
                // Sends the Feathers check when CarryParrot leaves inventory for anywhere other than the mail.
                // PulledFrom == Inventory marks the parrot as currently being watched for a departure.
                ItemPersistedState carryParrotPersisted = GameActions.ReadItemPersistedState(_ctx.Mem, item.Address);
                if (!carryParrotPersisted.CheckFired)
                {
                    bool parrotInInventory = item.ParentAddress == _ctx.InventoryRoom.Value;
                    if (parrotInInventory)
                    {
                        if (carryParrotPersisted.PulledFrom != ItemPulledFrom.Inventory)
                        {
                            GameActions.WriteItemPersistedState(_ctx.Mem, item.Address,
                                new ItemPersistedState(ItemStage.None, false, ItemPulledFrom.Inventory));
                        }
                    }
                    else if (carryParrotPersisted.PulledFrom == ItemPulledFrom.Inventory)
                    {
                        bool wentToMail = item.ParentAddress == _ctx.MailManRoom.Value;
                        if (wentToMail)
                        {
                            GameActions.WriteItemPersistedState(_ctx.Mem, item.Address,
                                new ItemPersistedState(ItemStage.None, false, ItemPulledFrom.None));
                        }
                        else
                        {
                            SendItemPickupCheck("Feathers");
                            GameActions.WriteItemPersistedState(_ctx.Mem, item.Address,
                                new ItemPersistedState(ItemStage.None, true, ItemPulledFrom.None));
                        }
                    }
                }
                continue;
            }

            if (isServerGranted)
            {
                bool itemGranted = receivedItems.Contains(serverGrantedApItemName!, StringComparer.OrdinalIgnoreCase);
                bool serverGrantedInHidden = GameActions.HiddenRoomAddress is { } serverGrantedHiddenAddr && item.ParentAddress == serverGrantedHiddenAddr;

                if (itemGranted)
                {
                    if (serverGrantedInHidden && !IsInstalledInFuseBox(project, item.Name))
                    {
                        bool delivered = DeliverToMail(item, gameManager);
                        if (delivered)
                        {
                            anyMailChange = true;
                            _ctx.Report.NotifyItemsChanged();
                        }
                    }
                    continue;
                }

                bool serverGrantedInInventory = item.ParentAddress == _ctx.InventoryRoom.Value;
                bool serverGrantedInMail = item.ParentAddress == _ctx.MailManRoom.Value;
                if (serverGrantedInInventory || serverGrantedInMail || serverGrantedInHidden)
                    continue;

                bool serverGrantedMoved = GameActions.MoveItemToHiddenRoomFull(_ctx.Mem, item.Address, _ctx.InventoryRoom.Value, gameManager);
                if (serverGrantedMoved)
                {
                    // Clears any Fuse Box socket's CShipSetting state for this fuse.
                    if (GameState.FindFuseBoxView(_ctx.Mem, project) is { } fuseBoxView)
                    {
                        foreach (long shipSetting in GameState.FindShipSettingsInstalledWith(_ctx.Mem, project, item.Name))
                            GameActions.ResetShipSetting(_ctx.Mem, shipSetting, fuseBoxView);
                    }

                    _ctx.Report.NotifyItemsChanged();
                }
                continue;
            }

            ItemPersistedState persisted = GameActions.ReadItemPersistedState(_ctx.Mem, item.Address);

            if (persisted.Stage == ItemStage.Inventory && persisted.CheckFired)
                continue;

            bool granted = LocationChecks.TryGetApItemName(item.Name, out string apItemName)
                && receivedItems.Contains(apItemName, StringComparer.OrdinalIgnoreCase);

            bool inInventory = item.ParentAddress == _ctx.InventoryRoom.Value;
            bool inHiddenRoom = GameActions.HiddenRoomAddress is { } hiddenAddr && item.ParentAddress == hiddenAddr;
            bool inMail = item.ParentAddress == _ctx.MailManRoom.Value;

            if (inInventory)
            {
                if (ItemTracking.RequiresCanTakeRestoreToggle(item.Name))
                    GameActions.SetItemCanTake(_ctx.Mem, item.Address, true);

                if (persisted.Stage == ItemStage.Mail)
                {
                    // Collected from mail. Only a Magazine that the game itself mailed (not ToolDelivered) counts as a
                    // natural pickup; every other item keeps its CheckFired as-is.
                    bool fireCheck = !persisted.CheckFired
                        && string.Equals(item.Name, "Magazine", StringComparison.OrdinalIgnoreCase)
                        && !persisted.ToolDelivered;

                    if (fireCheck)
                        SendItemPickupCheck(item.Name);

                    // Hides an ungranted natural pickup pending its AP grant.
                    if (fireCheck && !granted && !ItemTracking.IsOneDirectionalItem(item.Name) && _ctx.Options.TakeUngrantedItems)
                    {
                        bool moved = GameActions.MoveItemToHiddenRoomFull(_ctx.Mem, item.Address, _ctx.InventoryRoom.Value, gameManager);
                        if (moved)
                        {
                            GameActions.WriteItemPersistedState(_ctx.Mem, item.Address, new ItemPersistedState(ItemStage.Hidden, true, ItemPulledFrom.None));
                            _ctx.Report.NotifyItemsChanged();
                        }
                        _ctx.Report.ReportAction(moved, moved
                            ? $"{item.Name} picked up - not yet granted, hidden pending AP grant"
                            : $"{item.Name} picked up - not yet granted, but failed to hide it");
                        continue;
                    }

                    GameActions.WriteItemPersistedState(_ctx.Mem, item.Address,
                        new ItemPersistedState(ItemStage.Inventory, fireCheck || persisted.CheckFired, ItemPulledFrom.None));
                    continue;
                }

                if (persisted.Stage == ItemStage.Restored)
                {
                    SendItemPickupCheck(item.Name);
                    GameActions.WriteItemPersistedState(_ctx.Mem, item.Address, new ItemPersistedState(ItemStage.Inventory, true, ItemPulledFrom.None));
                    continue;
                }

                if (persisted.Stage == ItemStage.Inventory)
                    continue;

                SendItemPickupCheck(item.Name);

                if (granted || ItemTracking.IsOneDirectionalItem(item.Name) || !_ctx.Options.TakeUngrantedItems)
                {
                    GameActions.WriteItemPersistedState(_ctx.Mem, item.Address, new ItemPersistedState(ItemStage.Inventory, true, ItemPulledFrom.None));
                }
                else
                {
                    bool moved = GameActions.MoveItemToHiddenRoomFull(_ctx.Mem, item.Address, _ctx.InventoryRoom.Value, gameManager);
                    if (moved)
                    {
                        GameActions.WriteItemPersistedState(_ctx.Mem, item.Address, new ItemPersistedState(ItemStage.Hidden, true, ItemPulledFrom.None));
                        _ctx.Report.NotifyItemsChanged();
                    }
                    _ctx.Report.ReportAction(moved, moved
                        ? $"{item.Name} picked up - not yet granted, hidden pending AP grant"
                        : $"{item.Name} picked up - not yet granted, but failed to hide it");
                }
                continue;
            }

            if (inHiddenRoom)
            {
                if (persisted.Stage == ItemStage.Hidden && granted && !IsInstalledInFuseBox(project, item.Name))
                {
                    bool ok = DeliverToMail(item, gameManager);
                    if (ok)
                    {
                        GameActions.WriteItemPersistedState(_ctx.Mem, item.Address, new ItemPersistedState(ItemStage.Mail, true, ItemPulledFrom.None));
                        anyMailChange = true;
                        _ctx.Report.NotifyItemsChanged();
                    }
                }
                continue;
            }

            if (inMail)
            {
                // Normalizes a game-mailed Magazine's persisted state to Stage.Mail.
                if (persisted.Stage != ItemStage.Mail && string.Equals(item.Name, "Magazine", StringComparison.OrdinalIgnoreCase))
                {
                    GameActions.WriteItemPersistedState(_ctx.Mem, item.Address,
                        new ItemPersistedState(ItemStage.Mail, false, ItemPulledFrom.None));
                }
                continue;
            }

            // Granted but never found: deliver via mail. One-directional items are excluded.
            if (persisted.Stage == ItemStage.None && granted && !ItemTracking.IsOneDirectionalItem(item.Name))
            {
                bool ok = DeliverToMail(item, gameManager);
                if (ok)
                {
                    GameActions.WriteItemPersistedState(_ctx.Mem, item.Address, new ItemPersistedState(ItemStage.Mail, false, ItemPulledFrom.None, ToolDelivered: true));
                    anyMailChange = true;
                    _ctx.Report.NotifyItemsChanged();
                }
            }
        }

        if (anyMailChange)
            _ctx.Report.NotifyMailChanged();

        // After the loop above, so it never acts on the Magazine's stale pre-return parent this pass.
        TryReturnEatenMagazine(items, gameManager);
    }

    /// <summary>CMagazine's hand-off to an active DeskBot hides it and parks it at this position, whether or not the
    /// DeskBot pays out.</summary>
    private const short EatenMagazinePosition = 1000;

    /// <summary>How long an eaten Magazine must stay flagged before it's returned, so a successful hand-off's
    /// class-upgrade attempt is polled and its 2nd Class check queued first.</summary>
    private const int EatenMagazineConfirmTicks = 10;

    private long? _eatenMagazineAddr;
    private int _eatenMagazineFlaggedTick;

    /// <summary>Returns the Magazine to inventory when an active DeskBot ate it without paying out the 2nd Class
    /// upgrade (it wasn't reporting exactly Third, e.g. mid check-in), which would otherwise lose that check for good.
    /// Flags it on one pass and only acts on a later one, once it's still eaten and the check is still pending.</summary>
    private void TryReturnEatenMagazine(List<CarryItemLocation> items, long gameManager)
    {
        if (_ctx.InventoryRoom is not { } petControlAddr || _ctx.MailManRoom is not { } mailManAddr)
            return;
        if (!LocationChecks.TryGetClassUpgradeLocationName((int)PassengerClass.Second, out string secondLocationName))
            return;

        if (FindByName(items, "Magazine") is not { } magazine
            || _ctx.Ap.IsLocationCheckedOrQueued(secondLocationName)
            || !IsEatenByDeskBot(magazine, petControlAddr, mailManAddr))
        {
            _eatenMagazineAddr = null;
            return;
        }

        if (_eatenMagazineAddr != magazine.Address)
        {
            _eatenMagazineAddr = magazine.Address;
            _eatenMagazineFlaggedTick = _ctx.TickCount;
            _ctx.Report.AppendLog("Magazine looks eaten by the DeskBot with the 2nd Class check still pending - returning it if that holds");
            return;
        }
        if (_ctx.TickCount - _eatenMagazineFlaggedTick < EatenMagazineConfirmTicks)
            return;

        _eatenMagazineAddr = null;

        bool moved = GameActions.MoveItemSmart(_ctx.Mem, magazine.Address, petControlAddr, petControlAddr, gameManager);
        if (moved)
        {
            GameActions.SetItemVisible(_ctx.Mem, magazine.Address, true);
            ScheduleDirtyReassert();
            _ctx.Report.NotifyItemsChanged();
            GameActions.DisplayMessageSmart(_ctx.Mem, petControlAddr,
                "AP: The DeskBot took your Magazine without upgrading you, so it's been returned to your inventory. " +
                "Make sure you've checked in, then hand it over again.");
        }
        _ctx.Report.ReportAction(moved, moved
            ? "Magazine eaten by the DeskBot without a 2nd Class upgrade - returned to inventory"
            : "Magazine eaten by the DeskBot without a 2nd Class upgrade - failed to return it, will retry");
    }

    /// <summary>Whether the Magazine is in the state CMagazine's DeskBot hand-off leaves it: hidden, parked at
    /// <see cref="EatenMagazinePosition"/>, and outside the inventory, mail, and hidden room.</summary>
    private bool IsEatenByDeskBot(CarryItemLocation magazine, long petControlAddr, long mailManAddr)
    {
        if (magazine.ParentAddress == petControlAddr || magazine.ParentAddress == mailManAddr)
            return false;
        if (GameActions.HiddenRoomAddress is { } hiddenAddr && magazine.ParentAddress == hiddenAddr)
            return false;

        byte[]? visible = _ctx.Mem.ReadBytes(magazine.Address + GameOffsets.GameObjectVisibleOffset, 1);
        if (visible is null || visible[0] != 0)
            return false;

        // The first two int16s of _bounds; both are EatenMagazinePosition, so their order doesn't matter.
        int? topLeft = _ctx.Mem.ReadInt32(magazine.Address + GameOffsets.GameObjectBoundsOffset);
        return topLeft == ((ushort)EatenMagazinePosition | (ushort)EatenMagazinePosition << 16);
    }

    /// <summary>AP-grant handling for the real Feathers object, which is outside the generic tracked-item loop:
    /// a grant delivers it to mail, and a natural pickup while ungranted hides it pending a later grant.
    /// Never sends the Feathers check.</summary>
    private void TryHideOrDeliverFeather(List<CarryItemLocation> items, string[] receivedItems, long gameManager)
    {
        if (_ctx.InventoryRoom is null)
            return;
        if (FindByName(items, "Feathers") is not { } feather)
            return;

        bool granted = receivedItems.Contains("Feather", StringComparer.OrdinalIgnoreCase);
        ItemPersistedState persisted = GameActions.ReadItemPersistedState(_ctx.Mem, feather.Address);

        if (persisted.Stage == ItemStage.Hidden)
        {
            if (!granted)
                return;

            bool delivered = DeliverToMail(feather, gameManager);
            if (delivered)
            {
                GameActions.WriteItemPersistedState(_ctx.Mem, feather.Address, new ItemPersistedState(ItemStage.Mail, true, ItemPulledFrom.None, ToolDelivered: true));
                _ctx.Report.NotifyMailChanged();
                _ctx.Report.NotifyItemsChanged();
            }
            return;
        }

        if (persisted.Stage != ItemStage.None)
            return;

        if (granted)
        {
            bool delivered = DeliverToMail(feather, gameManager);
            if (delivered)
            {
                GameActions.WriteItemPersistedState(_ctx.Mem, feather.Address, new ItemPersistedState(ItemStage.Mail, false, ItemPulledFrom.None, ToolDelivered: true));
                _ctx.Report.NotifyMailChanged();
                _ctx.Report.NotifyItemsChanged();
            }
            return;
        }

        if (feather.ParentAddress != _ctx.InventoryRoom.Value)
            return;

        bool moved = GameActions.MoveItemToHiddenRoomFull(_ctx.Mem, feather.Address, _ctx.InventoryRoom.Value, gameManager);
        if (moved)
        {
            GameActions.WriteItemPersistedState(_ctx.Mem, feather.Address, new ItemPersistedState(ItemStage.Hidden, true, ItemPulledFrom.None));
            _ctx.Report.NotifyItemsChanged();
        }
        _ctx.Report.ReportAction(moved, moved
            ? "Feathers picked up - not yet granted, hidden pending AP grant"
            : "Feathers picked up - not yet granted, but failed to hide it");
    }

    /// <summary>AP item name to engine name for every item <see cref="ReturnToInventory"/> can return. Feathers has
    /// no AP item mapping of its own (see TryHideOrDeliverFeather), so it's added by hand.</summary>
    private static readonly Dictionary<string, string> EngineNameByApItemName = Items.All
        .Where(d => (d.ServerGrantedApItemName ?? d.ApItemName) is not null)
        .Select(d => (ApName: (d.ServerGrantedApItemName ?? d.ApItemName)!, d.Name))
        .Append((ApName: "Feather", Name: "Feathers"))
        .ToDictionary(x => x.ApName, x => x.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>The AP item names that <paramref name="query"/> refers to: an exact (case-insensitive) match, or
    /// failing that every name containing it.</summary>
    public static List<string> MatchReturnableItems(string query)
    {
        if (EngineNameByApItemName.ContainsKey(query))
            return new List<string> { EngineNameByApItemName.Keys.First(k => string.Equals(k, query, StringComparison.OrdinalIgnoreCase)) };

        return EngineNameByApItemName.Keys
            .Where(k => k.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Moves an already-granted item back into the player's inventory from wherever it ended up (used on
    /// something, left in a room, sitting in the mail or hidden room...). A tracked item's pickup check is left as it
    /// was, so one that hasn't fired yet is still restored to its home spot for a real pickup later.</summary>
    public ReturnItemResult ReturnToInventory(string apItemName)
    {
        if (_ctx.Project is not { } project || _ctx.GameManager is not { } gameManager
            || _ctx.InventoryRoom is not { } inventoryRoom)
            return ReturnItemResult.NotInGame;
        if (!EngineNameByApItemName.TryGetValue(apItemName, out string? name))
            return ReturnItemResult.UnknownItem;
        if (!_ctx.Ap.GetReceivedItemNames().Contains(apItemName, StringComparer.OrdinalIgnoreCase))
            return ReturnItemResult.NotGranted;

        if (FindByName(GameState.FindAllCarryItems(_ctx.Mem, project), name) is not { } item)
            return ReturnItemResult.NotFound;
        if (item.ParentAddress == inventoryRoom)
            return ReturnItemResult.AlreadyInInventory;
        if (IsInstalledInFuseBox(project, name))
            return ReturnItemResult.InstalledInFuseBox;

        // A Restored item is sitting at its home spot for the player to pick up, and moving it would leave that view's
        // restoration side effects (hidden sibling, drop-target frame) stale while they're still looking at it.
        if (!ItemTracking.ServerGrantedItemNames.ContainsKey(name)
            && GameActions.ReadItemPersistedState(_ctx.Mem, item.Address).Stage == ItemStage.Restored)
            return ReturnItemResult.Restored;

        bool fromMail = item.ParentAddress == _ctx.MailManRoom;
        if (!GameActions.MoveItemSmart(_ctx.Mem, item.Address, inventoryRoom, inventoryRoom, gameManager))
            return ReturnItemResult.MoveFailed;

        CancelPendingUnrestoreChecks(new[] { name });
        GameActions.SetItemVisible(_ctx.Mem, item.Address, true);
        if (ItemTracking.RequiresCanTakeRestoreToggle(name) || ItemTracking.RequiresCanTakeOverride(name))
            GameActions.SetItemCanTake(_ctx.Mem, item.Address, true);
        if (fromMail)
        {
            GameActions.UnmarkItemAsToolPlaced(_ctx.Mem, item.Address);
            _ctx.Report.NotifyMailChanged();
        }

        // Server-granted items don't use the persisted state. Everything else is marked as being in inventory.
        if (!ItemTracking.ServerGrantedItemNames.ContainsKey(name))
        {
            ItemPersistedState persisted = GameActions.ReadItemPersistedState(_ctx.Mem, item.Address);

            // Same as collecting it from the mail (see ReconcileTrackedItems): a Magazine the game itself mailed is a
            // natural pickup, so it sends its check.
            bool fireCheck = persisted.Stage == ItemStage.Mail
                && !persisted.CheckFired
                && string.Equals(name, "Magazine", StringComparison.OrdinalIgnoreCase)
                && !persisted.ToolDelivered;
            if (fireCheck)
                SendItemPickupCheck(name);

            GameActions.WriteItemPersistedState(_ctx.Mem, item.Address,
                new ItemPersistedState(ItemStage.Inventory, fireCheck || persisted.CheckFired, ItemPulledFrom.None));
        }

        ScheduleDirtyReassert();
        _ctx.Report.NotifyItemsChanged();
        return ReturnItemResult.Returned;
    }

    /// <summary>True when a fuse is currently installed in a Fuse Box socket (an installed fuse's real item is parked
    /// in the hidden room).</summary>
    private bool IsInstalledInFuseBox(long project, string itemName) =>
        ItemTracking.IsFuseBoxItem(itemName)
        && GameState.FindShipSettingsInstalledWith(_ctx.Mem, project, itemName).Count > 0;

    /// <summary>Finds a single named item within an already-fetched FindAllCarryItems result.</summary>
    private static CarryItemLocation? FindByName(List<CarryItemLocation> items, string name)
    {
        foreach (CarryItemLocation candidate in items)
        {
            if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }
        return null;
    }

    /// <summary>Arms a follow-up dirty-mark reassert a few ticks in the future.</summary>
    private void ScheduleDirtyReassert() =>
        _pendingDirtyReassertTick = _ctx.TickCount + DirtyReassertDelayTicks;

    /// <summary>Restores eligible items to their home parent, or self-heals a stray item, on arrival at the RNV that owns them.</summary>
    public void TryRestoreItemsAtHomeRnv(RoomNodeView rnv, long gameManager)
    {
        if (!ItemHomeLocations.TryGetItemsForRnv(rnv, out string[] itemNames))
            return;

        // Cancels any delayed unrestore-check still queued for these items.
        CancelPendingUnrestoreChecks(itemNames);

        if (_ctx.Project is not { } project || _ctx.InventoryRoom is null)
            return;

        List<CarryItemLocation>? items = null;

        foreach (string name in itemNames)
        {
            if (ItemTracking.IsRestorationExcluded(name))
                continue;

            items ??= GameState.FindAllCarryItems(_ctx.Mem, project);
            if (FindByName(items, name) is not { } item)
                continue;

            ItemPersistedState persisted = GameActions.ReadItemPersistedState(_ctx.Mem, item.Address);

            bool eligibleFromInventory = persisted.Stage == ItemStage.Inventory && !persisted.CheckFired;

            // Only items this app mailed (ToolDelivered) are restored out of the mail.
            bool eligibleFromMail = persisted.Stage == ItemStage.Mail && !persisted.CheckFired && persisted.ToolDelivered;

            if (eligibleFromInventory || eligibleFromMail)
            {
                long? homeParent = GameState.ResolveHomeParent(_ctx.Mem, project, name, out string failureReason);
                if (homeParent is null)
                {
                    _ctx.Report.ReportAction(false, $"{name} is eligible for restoration but its home parent couldn't be resolved ({failureReason})");
                    continue;
                }

                ItemPulledFrom pulledFrom = eligibleFromMail ? ItemPulledFrom.Mail : ItemPulledFrom.Inventory;
                bool moved = GameActions.MoveItemSmart(_ctx.Mem, item.Address, homeParent.Value, _ctx.InventoryRoom, gameManager);
                if (moved)
                {
                    GameActions.WriteItemPersistedState(_ctx.Mem, item.Address,
                        new ItemPersistedState(ItemStage.Restored, false, pulledFrom));
                    GameActions.SetItemVisible(_ctx.Mem, item.Address, false);

                    if (!ItemTracking.SkipsFirstChildReorderOnRestore(name))
                        GameActions.MoveToFirstChild(_ctx.Mem, item.Address, homeParent.Value);

                    if (ItemTracking.RequiresCanTakeRestoreToggle(name))
                        GameActions.SetItemCanTake(_ctx.Mem, item.Address, false);
                    else if (ItemTracking.RequiresCanTakeOverride(name))
                        GameActions.SetItemCanTake(_ctx.Mem, item.Address, true);

                    if (ItemHomeLocations.TryGetRestoreFieldOverride(name, out var fields))
                    {
                        if (fields is { Left: { } l, Top: { } t, Right: { } r, Bottom: { } b })
                            GameActions.SetItemBounds(_ctx.Mem, item.Address, l, t, r, b);
                        if (fields.CursorId is { } cursorId)
                            GameActions.SetItemCursorId(_ctx.Mem, item.Address, cursorId);
                        if (fields.EnterFrame is { } enterFrame)
                            GameActions.CallLoadFrame(_ctx.Mem, item.Address, enterFrame);
                        if (fields.KeepVisible is true)
                            GameActions.SetItemVisible(_ctx.Mem, item.Address, true);
                    }

                    if (ItemHomeLocations.TryGetHideSiblingOnRestore(name, out var sibling))
                    {
                        long? siblingAddr = GameState.FindDescendant(_ctx.Mem, homeParent.Value, sibling.Name, sibling.ClassName);
                        if (siblingAddr is not null)
                            GameActions.SetItemVisible(_ctx.Mem, siblingAddr.Value, false);
                    }

                    if (ItemTracking.HomeParentIsDropTargetDisplay(name)
                        && !GameActions.ShowDropTargetHolding(_ctx.Mem, homeParent.Value))
                    {
                        _ctx.Report.ReportAction(false, $"{name} restored, but its drop target's frame couldn't be updated");
                    }

                    ScheduleDirtyReassert();
                    _ctx.Report.NotifyItemsChanged();
                }
                _ctx.Report.ReportAction(moved, moved
                    ? $"{name} restored to its home parent for a real natural pickup (was granted before being found)"
                    : $"{name} failed to restore to its home parent");
                continue;
            }

            if (persisted.Stage == ItemStage.None)
            {
                long? homeParent = GameState.ResolveHomeParent(_ctx.Mem, project, name);
                if (homeParent is null || item.ParentAddress == homeParent)
                    continue;

                bool moved = GameActions.MoveItemToRoom(_ctx.Mem, item.Address, homeParent.Value);
                _ctx.Report.ReportAction(moved, $"{name} self-healed back to its home parent");
            }
        }
    }

    /// <summary>Queues a delayed re-check for any item still sitting Restored-but-unpicked at the RNV just being
    /// left. ProcessPendingUnrestoreChecks performs the actual revert.</summary>
    public void TryUnrestoreItemsLeavingRnv(RoomNodeView rnv, long gameManager)
    {
        if (!ItemHomeLocations.TryGetItemsForRnv(rnv, out string[] itemNames))
            return;
        if (_ctx.Project is not { } project)
            return;

        List<CarryItemLocation>? items = null;

        foreach (string name in itemNames)
        {
            items ??= GameState.FindAllCarryItems(_ctx.Mem, project);
            if (FindByName(items, name) is not { } item)
                continue;

            ItemPersistedState persisted = GameActions.ReadItemPersistedState(_ctx.Mem, item.Address);
            if (persisted.Stage != ItemStage.Restored)
                continue;

            if (_pendingUnrestoreChecks.Any(p => p.ItemAddress == item.Address))
                continue; // a quick leave/return/leave already re-queued this one

            _pendingUnrestoreChecks.Add((name, item.Address, _ctx.TickCount + UnrestoreCheckDelayTicks));
        }
    }

    /// <summary>Drops any queued delayed unrestore-check for the given items.</summary>
    private void CancelPendingUnrestoreChecks(string[] itemNames)
    {
        if (_pendingUnrestoreChecks.Count == 0)
            return;

        _pendingUnrestoreChecks.RemoveAll(p => itemNames.Contains(p.ItemName, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Runs the delayed revert decision queued by TryUnrestoreItemsLeavingRnv, once enough ticks have
    /// passed for ReconcileTrackedItems to have run at least once. Items still Restored are reverted.</summary>
    public void ProcessPendingUnrestoreChecks(long gameManager)
    {
        if (_pendingUnrestoreChecks.Count == 0)
            return;

        RoomNodeView? currentRnv = GameState.ReadRoomNodeView(_ctx.Mem, gameManager);

        for (int i = _pendingUnrestoreChecks.Count - 1; i >= 0; i--)
        {
            (string name, long itemAddress, int dueTick) = _pendingUnrestoreChecks[i];
            if (_ctx.TickCount < dueTick)
                continue;

            ItemPersistedState persisted = GameActions.ReadItemPersistedState(_ctx.Mem, itemAddress);
            if (persisted.Stage != ItemStage.Restored)
            {
                _pendingUnrestoreChecks.RemoveAt(i); // already picked up and reconciled - nothing to revert
                continue;
            }

            // Defers the decision while the player is still at an RNV the pickup itself leads to (see DeferUnrestoreAtRnv).
            if (DeferUnrestoreAtRnv.TryGetValue(name, out RoomNodeView deferRnv) && currentRnv == deferRnv)
            {
                _pendingUnrestoreChecks[i] = (name, itemAddress, _ctx.TickCount + UnrestoreCheckDelayTicks);
                continue;
            }

            _pendingUnrestoreChecks.RemoveAt(i);

            long? parentAddress = _ctx.Mem.ReadInt64(itemAddress + GameOffsets.Parent);
            string? parentName = parentAddress is long p ? GameState.TryReadName(_ctx.Mem, p) : null;
            var item = new CarryItemLocation(name, itemAddress, parentAddress, parentName);

            if (TryCompleteEar1IfBowlUnlocked(item, gameManager))
            {
                _ctx.Report.ReportAction(true, $"{name} left un-picked-up after the bowl unlocked - completed the pickup instead of reverting it");
                continue;
            }

            if (IsEye1MidBellbotHandoff(item))
            {
                _ctx.Report.ReportAction(true, $"{name} handed off by the bellbot - leaving it for the natural pickup to land instead of reverting");
                continue;
            }

            bool moved = RevertRestoration(item, persisted, gameManager);
            _ctx.Report.ReportAction(moved, moved
                ? $"{name} left un-picked-up - returned to where it was before restoration"
                : $"{name} failed to revert from restoration");
        }
    }

    /// <summary>If Ear1 (Pistachio Bowl) was left behind after its ParrotNutBowlActor reached the unlocked state, moves
    /// it into inventory instead of reverting it, so ReconcileTrackedItems's Restored + inInventory branch sends the
    /// pickup check.</summary>
    private bool TryCompleteEar1IfBowlUnlocked(CarryItemLocation item, long gameManager)
    {
        if (!string.Equals(item.Name, "Ear1", StringComparison.OrdinalIgnoreCase))
            return false;
        if (item.ParentAddress is not { } parentAddr || _ctx.InventoryRoom is not { } inventoryRoom)
            return false;

        long? actorAddr = GameState.FindDescendant(_ctx.Mem, parentAddr, "ParrotNutBowlActor", "CParrotNutBowlActor");
        if (actorAddr is null)
            return false;

        int? state = _ctx.Mem.ReadInt32(actorAddr.Value + GameOffsets.ParrotNutBowlActorStateOffset);
        if (state != GameOffsets.ParrotNutBowlActorStateUnlocked)
            return false;

        return GameActions.MoveItemSmart(_ctx.Mem, item.Address, inventoryRoom, inventoryRoom, gameManager);
    }

    /// <summary>True when Eye1's light fixture ("6WTL", CLight, under Eye1's home View) reports _eyePresent == false,
    /// meaning the bellbot hand-off has completed and Eye1's revert should be skipped.</summary>
    private bool IsEye1MidBellbotHandoff(CarryItemLocation item)
    {
        if (!string.Equals(item.Name, "Eye1", StringComparison.OrdinalIgnoreCase))
            return false;
        if (_ctx.Project is not { } project)
            return false;

        long? homeParent = GameState.ResolveHomeParent(_ctx.Mem, project, "Eye1");
        if (homeParent is null)
            return false;

        long? lightAddr = GameState.FindDescendant(_ctx.Mem, homeParent.Value, "6WTL", "CLight");
        if (lightAddr is null)
            return false;

        byte[]? eyePresent = _ctx.Mem.ReadBytes(lightAddr.Value + GameOffsets.LightEyePresentOffset, 1);
        return eyePresent is { Length: 1 } && eyePresent[0] == 0;
    }

    /// <summary>The actual move-back for TryUnrestoreItemsLeavingRnv, per ItemPulledFrom.</summary>
    private bool RevertRestoration(CarryItemLocation item, ItemPersistedState persisted, long gameManager)
    {
        bool moved = persisted.PulledFrom switch
        {
            ItemPulledFrom.Inventory => RevertToInventory(item, gameManager),
            ItemPulledFrom.Hidden => RevertToHidden(item, persisted, gameManager),
            ItemPulledFrom.Mail => RevertToMail(item, persisted, gameManager),
            _ => false,
        };

        if (moved)
        {
            GameActions.SetItemVisible(_ctx.Mem, item.Address, true);
            if (ItemTracking.RequiresCanTakeRestoreToggle(item.Name))
                GameActions.SetItemCanTake(_ctx.Mem, item.Address, true);
            ScheduleDirtyReassert();
        }

        return moved;
    }

    private bool RevertToInventory(CarryItemLocation item, long gameManager)
    {
        if (_ctx.InventoryRoom is null)
            return false;
        bool moved = GameActions.MoveItemSmart(_ctx.Mem, item.Address, _ctx.InventoryRoom.Value, _ctx.InventoryRoom, gameManager);
        if (moved)
            GameActions.WriteItemPersistedState(_ctx.Mem, item.Address, new ItemPersistedState(ItemStage.Inventory, false, ItemPulledFrom.None));
        return moved;
    }

    private bool RevertToHidden(CarryItemLocation item, ItemPersistedState persisted, long gameManager)
    {
        if (_ctx.InventoryRoom is null)
            return false;
        bool moved = GameActions.MoveItemToHiddenRoomFull(_ctx.Mem, item.Address, _ctx.InventoryRoom.Value, gameManager);
        if (moved)
            GameActions.WriteItemPersistedState(_ctx.Mem, item.Address, new ItemPersistedState(ItemStage.Hidden, persisted.CheckFired, ItemPulledFrom.None));
        return moved;
    }

    private bool RevertToMail(CarryItemLocation item, ItemPersistedState persisted, long gameManager)
    {
        if (_ctx.MailManRoom is null || _ctx.InventoryRoom is null)
            return false;
        bool moved = GameActions.MoveItemSmart(_ctx.Mem, item.Address, _ctx.MailManRoom.Value, _ctx.InventoryRoom, gameManager);
        if (moved)
        {
            // PulledFrom.Mail is only recorded for ToolDelivered items, so ToolDelivered is restamped.
            GameActions.WriteItemPersistedState(_ctx.Mem, item.Address,
                new ItemPersistedState(ItemStage.Mail, persisted.CheckFired, ItemPulledFrom.None, ToolDelivered: true));
        }
        return moved;
    }

    /// <summary>Delivers a tracked item to the player via the mail system.</summary>
    private bool DeliverToMail(CarryItemLocation item, long gameManager)
    {
        if (_ctx.InventoryRoom is null || _ctx.MailManRoom is null)
            return false;

        string destRoomName = _ctx.CurrentRoomName is not null && ChevronCodes.HasStation(_ctx.CurrentRoomName)
            ? _ctx.CurrentRoomName
            : "EmbLobby";
        uint? liveRoomFlags = GameState.ReadCurrentRoomFlags(_ctx.Mem, _ctx.InventoryRoom.Value);
        ChevronCodes.TryGetCode(destRoomName, liveRoomFlags, out uint code);

        bool moved = GameActions.MoveItemSmart(_ctx.Mem, item.Address, _ctx.MailManRoom.Value, _ctx.InventoryRoom, gameManager);
        bool ok = moved && GameActions.SetItemMailDestination(_ctx.Mem, item.Address, code);
        if (ok)
        {
            GameActions.MarkItemAsToolPlaced(_ctx.Mem, item.Address);
            if (ItemTracking.RequiresCanTakeOverride(item.Name))
                GameActions.SetItemCanTake(_ctx.Mem, item.Address, true);
        }

        _ctx.Report.ReportAction(ok, ok
            ? $"Delivered AP grant for {item.Name} via mail, destination {destRoomName}"
            : $"Failed to deliver AP grant for {item.Name}");

        return ok;
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

        GetLiftEye2GateHook.SetGateAllowed(_ctx.Mem, headInInventory);
    }

    /// <summary>Sends the AP location check for physically finding a tracked item, if one is mapped.</summary>
    private void SendItemPickupCheck(string itemName)
    {
        if (!LocationChecks.TryGetItemPickupLocationName(itemName, out string locationName))
            return;

        bool handedOff = _ctx.Ap.SendLocationCheck(locationName);
        _ctx.Report.ReportAction(handedOff, handedOff
            ? $"Location check: {itemName} -> {locationName}"
            : $"Location check queued (offline): {itemName} -> {locationName}");
        _ctx.Report.NotifyPendingChecksChanged();
    }
}
