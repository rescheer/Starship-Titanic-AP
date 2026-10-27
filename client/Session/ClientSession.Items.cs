namespace StarshipTitanicAp;

internal sealed partial class ClientSession
{
    /// <summary>Re-resolves _project and the PET control (inventory room), reporting both addresses.</summary>
    private void RefreshProjectAddresses(long gameManager)
    {
        long? project = GameState.ResolveProject(_mem, gameManager);
        _ctx.Project = project;
        ReportAddress("_project", project);

        if (project is null)
            return;

        // Only a non-null result replaces the cached address; OnTick's project-change handling clears it.
        long? inventoryRoom = GameState.FindInventoryRoom(_mem, project.Value);
        if (inventoryRoom is not null)
            _ctx.InventoryRoom = inventoryRoom;
        ReportAddress("Player Inventory (CPetControl)", _ctx.InventoryRoom);

        ReportAddress("Conversations (CPetConversations)",
            _ctx.InventoryRoom is not null ? GameState.ResolveConversationsAddr(_ctx.InventoryRoom.Value) : null);
    }

    /// <summary>Runs the full-tree item-state reconciliation pass for every tracked item.</summary>
    private void ReconcileTrackedItems(long gameManager)
    {
        if (_ctx.Project is not { } project)
            return;
        if (_ctx.InventoryRoom is null || _ctx.MailManRoom is null)
            return;

        List<CarryItemLocation> items = GameState.FindAllCarryItems(_mem, project);
        string[] receivedItems = _apConnection.GetReceivedItemNames();
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
                ItemPersistedState carryParrotPersisted = GameActions.ReadItemPersistedState(_mem, item.Address);
                if (!carryParrotPersisted.CheckFired)
                {
                    bool parrotInInventory = item.ParentAddress == _ctx.InventoryRoom.Value;
                    if (parrotInInventory)
                    {
                        if (carryParrotPersisted.PulledFrom != ItemPulledFrom.Inventory)
                        {
                            GameActions.WriteItemPersistedState(_mem, item.Address,
                                new ItemPersistedState(ItemStage.None, false, ItemPulledFrom.Inventory));
                        }
                    }
                    else if (carryParrotPersisted.PulledFrom == ItemPulledFrom.Inventory)
                    {
                        bool wentToMail = item.ParentAddress == _ctx.MailManRoom.Value;
                        if (wentToMail)
                        {
                            GameActions.WriteItemPersistedState(_mem, item.Address,
                                new ItemPersistedState(ItemStage.None, false, ItemPulledFrom.None));
                        }
                        else
                        {
                            SendItemPickupCheck("Feathers");
                            GameActions.WriteItemPersistedState(_mem, item.Address,
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
                            NotifyItemsChanged();
                        }
                    }
                    continue;
                }

                bool serverGrantedInInventory = item.ParentAddress == _ctx.InventoryRoom.Value;
                bool serverGrantedInMail = item.ParentAddress == _ctx.MailManRoom.Value;
                if (serverGrantedInInventory || serverGrantedInMail || serverGrantedInHidden)
                    continue;

                bool serverGrantedMoved = GameActions.MoveItemToHiddenRoomFull(_mem, item.Address, _ctx.InventoryRoom.Value, gameManager);
                if (serverGrantedMoved)
                {
                    // Clears any Fuse Box socket's CShipSetting state for this fuse.
                    if (GameState.FindFuseBoxView(_mem, project) is { } fuseBoxView)
                    {
                        foreach (long shipSetting in GameState.FindShipSettingsInstalledWith(_mem, project, item.Name))
                            GameActions.ResetShipSetting(_mem, shipSetting, fuseBoxView);
                    }

                    NotifyItemsChanged();
                }
                continue;
            }

            ItemPersistedState persisted = GameActions.ReadItemPersistedState(_mem, item.Address);

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
                    GameActions.SetItemCanTake(_mem, item.Address, true);

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
                    if (fireCheck && !granted && !ItemTracking.IsOneDirectionalItem(item.Name) && Options.TakeUngrantedItems)
                    {
                        bool moved = GameActions.MoveItemToHiddenRoomFull(_mem, item.Address, _ctx.InventoryRoom.Value, gameManager);
                        if (moved)
                        {
                            GameActions.WriteItemPersistedState(_mem, item.Address, new ItemPersistedState(ItemStage.Hidden, true, ItemPulledFrom.None));
                            NotifyItemsChanged();
                        }
                        ReportAction(moved, moved
                            ? $"{item.Name} picked up - not yet granted, hidden pending AP grant"
                            : $"{item.Name} picked up - not yet granted, but failed to hide it");
                        continue;
                    }

                    GameActions.WriteItemPersistedState(_mem, item.Address,
                        new ItemPersistedState(ItemStage.Inventory, fireCheck || persisted.CheckFired, ItemPulledFrom.None));
                    continue;
                }

                if (persisted.Stage == ItemStage.Restored)
                {
                    SendItemPickupCheck(item.Name);
                    GameActions.WriteItemPersistedState(_mem, item.Address, new ItemPersistedState(ItemStage.Inventory, true, ItemPulledFrom.None));
                    continue;
                }

                if (persisted.Stage == ItemStage.Inventory)
                    continue;

                SendItemPickupCheck(item.Name);

                if (granted || ItemTracking.IsOneDirectionalItem(item.Name) || !Options.TakeUngrantedItems)
                {
                    GameActions.WriteItemPersistedState(_mem, item.Address, new ItemPersistedState(ItemStage.Inventory, true, ItemPulledFrom.None));
                }
                else
                {
                    bool moved = GameActions.MoveItemToHiddenRoomFull(_mem, item.Address, _ctx.InventoryRoom.Value, gameManager);
                    if (moved)
                    {
                        GameActions.WriteItemPersistedState(_mem, item.Address, new ItemPersistedState(ItemStage.Hidden, true, ItemPulledFrom.None));
                        NotifyItemsChanged();
                    }
                    ReportAction(moved, moved
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
                        GameActions.WriteItemPersistedState(_mem, item.Address, new ItemPersistedState(ItemStage.Mail, true, ItemPulledFrom.None));
                        anyMailChange = true;
                        NotifyItemsChanged();
                    }
                }
                continue;
            }

            if (inMail)
            {
                // Normalizes a game-mailed Magazine's persisted state to Stage.Mail.
                if (persisted.Stage != ItemStage.Mail && string.Equals(item.Name, "Magazine", StringComparison.OrdinalIgnoreCase))
                {
                    GameActions.WriteItemPersistedState(_mem, item.Address,
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
                    GameActions.WriteItemPersistedState(_mem, item.Address, new ItemPersistedState(ItemStage.Mail, false, ItemPulledFrom.None, ToolDelivered: true));
                    anyMailChange = true;
                    NotifyItemsChanged();
                }
            }
        }

        if (anyMailChange)
            _lastMailItems = null;

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
            || _apConnection.IsLocationCheckedOrQueued(secondLocationName)
            || !IsEatenByDeskBot(magazine, petControlAddr, mailManAddr))
        {
            _eatenMagazineAddr = null;
            return;
        }

        if (_eatenMagazineAddr != magazine.Address)
        {
            _eatenMagazineAddr = magazine.Address;
            _eatenMagazineFlaggedTick = _ctx.TickCount;
            AppendLog("Magazine looks eaten by the DeskBot with the 2nd Class check still pending - returning it if that holds");
            return;
        }
        if (_ctx.TickCount - _eatenMagazineFlaggedTick < EatenMagazineConfirmTicks)
            return;

        _eatenMagazineAddr = null;

        bool moved = GameActions.MoveItemSmart(_mem, magazine.Address, petControlAddr, petControlAddr, gameManager);
        if (moved)
        {
            GameActions.SetItemVisible(_mem, magazine.Address, true);
            ScheduleDirtyReassert();
            NotifyItemsChanged();
            GameActions.DisplayMessageSmart(_mem, petControlAddr,
                "AP: The DeskBot took your Magazine without upgrading you, so it's been returned to your inventory. " +
                "Make sure you've checked in, then hand it over again.");
        }
        ReportAction(moved, moved
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

        byte[]? visible = _mem.ReadBytes(magazine.Address + GameOffsets.GameObjectVisibleOffset, 1);
        if (visible is null || visible[0] != 0)
            return false;

        // The first two int16s of _bounds; both are EatenMagazinePosition, so their order doesn't matter.
        int? topLeft = _mem.ReadInt32(magazine.Address + GameOffsets.GameObjectBoundsOffset);
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
        ItemPersistedState persisted = GameActions.ReadItemPersistedState(_mem, feather.Address);

        if (persisted.Stage == ItemStage.Hidden)
        {
            if (!granted)
                return;

            bool delivered = DeliverToMail(feather, gameManager);
            if (delivered)
            {
                GameActions.WriteItemPersistedState(_mem, feather.Address, new ItemPersistedState(ItemStage.Mail, true, ItemPulledFrom.None, ToolDelivered: true));
                _lastMailItems = null;
                NotifyItemsChanged();
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
                GameActions.WriteItemPersistedState(_mem, feather.Address, new ItemPersistedState(ItemStage.Mail, false, ItemPulledFrom.None, ToolDelivered: true));
                _lastMailItems = null;
                NotifyItemsChanged();
            }
            return;
        }

        if (feather.ParentAddress != _ctx.InventoryRoom.Value)
            return;

        bool moved = GameActions.MoveItemToHiddenRoomFull(_mem, feather.Address, _ctx.InventoryRoom.Value, gameManager);
        if (moved)
        {
            GameActions.WriteItemPersistedState(_mem, feather.Address, new ItemPersistedState(ItemStage.Hidden, true, ItemPulledFrom.None));
            NotifyItemsChanged();
        }
        ReportAction(moved, moved
            ? "Feathers picked up - not yet granted, hidden pending AP grant"
            : "Feathers picked up - not yet granted, but failed to hide it");
    }

    /// <summary>True when a fuse is currently installed in a Fuse Box socket (an installed fuse's real item is parked
    /// in the hidden room).</summary>
    private bool IsInstalledInFuseBox(long project, string itemName) =>
        ItemTracking.IsFuseBoxItem(itemName)
        && GameState.FindShipSettingsInstalledWith(_mem, project, itemName).Count > 0;

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
    private void TryRestoreItemsAtHomeRnv(RoomNodeView rnv, long gameManager)
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

            items ??= GameState.FindAllCarryItems(_mem, project);
            if (FindByName(items, name) is not { } item)
                continue;

            ItemPersistedState persisted = GameActions.ReadItemPersistedState(_mem, item.Address);

            bool eligibleFromInventory = persisted.Stage == ItemStage.Inventory && !persisted.CheckFired;

            // Only items this app mailed (ToolDelivered) are restored out of the mail.
            bool eligibleFromMail = persisted.Stage == ItemStage.Mail && !persisted.CheckFired && persisted.ToolDelivered;

            if (eligibleFromInventory || eligibleFromMail)
            {
                long? homeParent = GameState.ResolveHomeParent(_mem, project, name, out string failureReason);
                if (homeParent is null)
                {
                    ReportAction(false, $"{name} is eligible for restoration but its home parent couldn't be resolved ({failureReason})");
                    continue;
                }

                ItemPulledFrom pulledFrom = eligibleFromMail ? ItemPulledFrom.Mail : ItemPulledFrom.Inventory;
                bool moved = GameActions.MoveItemSmart(_mem, item.Address, homeParent.Value, _ctx.InventoryRoom, gameManager);
                if (moved)
                {
                    GameActions.WriteItemPersistedState(_mem, item.Address,
                        new ItemPersistedState(ItemStage.Restored, false, pulledFrom));
                    GameActions.SetItemVisible(_mem, item.Address, false);

                    if (!ItemTracking.SkipsFirstChildReorderOnRestore(name))
                        GameActions.MoveToFirstChild(_mem, item.Address, homeParent.Value);

                    if (ItemTracking.RequiresCanTakeRestoreToggle(name))
                        GameActions.SetItemCanTake(_mem, item.Address, false);
                    else if (ItemTracking.RequiresCanTakeOverride(name))
                        GameActions.SetItemCanTake(_mem, item.Address, true);

                    if (ItemHomeLocations.TryGetRestoreFieldOverride(name, out var fields))
                    {
                        if (fields is { Left: { } l, Top: { } t, Right: { } r, Bottom: { } b })
                            GameActions.SetItemBounds(_mem, item.Address, l, t, r, b);
                        if (fields.CursorId is { } cursorId)
                            GameActions.SetItemCursorId(_mem, item.Address, cursorId);
                        if (fields.EnterFrame is { } enterFrame)
                            GameActions.CallLoadFrame(_mem, item.Address, enterFrame);
                        if (fields.KeepVisible is true)
                            GameActions.SetItemVisible(_mem, item.Address, true);
                    }

                    if (ItemHomeLocations.TryGetHideSiblingOnRestore(name, out var sibling))
                    {
                        long? siblingAddr = GameState.FindDescendant(_mem, homeParent.Value, sibling.Name, sibling.ClassName);
                        if (siblingAddr is not null)
                            GameActions.SetItemVisible(_mem, siblingAddr.Value, false);
                    }

                    ScheduleDirtyReassert();
                    NotifyItemsChanged();
                }
                ReportAction(moved, moved
                    ? $"{name} restored to its home parent for a real natural pickup (was granted before being found)"
                    : $"{name} failed to restore to its home parent");
                continue;
            }

            if (persisted.Stage == ItemStage.None)
            {
                long? homeParent = GameState.ResolveHomeParent(_mem, project, name);
                if (homeParent is null || item.ParentAddress == homeParent)
                    continue;

                bool moved = GameActions.MoveItemToRoom(_mem, item.Address, homeParent.Value);
                ReportAction(moved, $"{name} self-healed back to its home parent");
            }
        }
    }

    /// <summary>Queues a delayed re-check for any item still sitting Restored-but-unpicked at the RNV just being
    /// left. ProcessPendingUnrestoreChecks performs the actual revert.</summary>
    private void TryUnrestoreItemsLeavingRnv(RoomNodeView rnv, long gameManager)
    {
        if (!ItemHomeLocations.TryGetItemsForRnv(rnv, out string[] itemNames))
            return;
        if (_ctx.Project is not { } project)
            return;

        List<CarryItemLocation>? items = null;

        foreach (string name in itemNames)
        {
            items ??= GameState.FindAllCarryItems(_mem, project);
            if (FindByName(items, name) is not { } item)
                continue;

            ItemPersistedState persisted = GameActions.ReadItemPersistedState(_mem, item.Address);
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
    private void ProcessPendingUnrestoreChecks(long gameManager)
    {
        if (_pendingUnrestoreChecks.Count == 0)
            return;

        RoomNodeView? currentRnv = GameState.ReadRoomNodeView(_mem, gameManager);

        for (int i = _pendingUnrestoreChecks.Count - 1; i >= 0; i--)
        {
            (string name, long itemAddress, int dueTick) = _pendingUnrestoreChecks[i];
            if (_ctx.TickCount < dueTick)
                continue;

            ItemPersistedState persisted = GameActions.ReadItemPersistedState(_mem, itemAddress);
            if (persisted.Stage != ItemStage.Restored)
            {
                _pendingUnrestoreChecks.RemoveAt(i); // already picked up and reconciled - nothing to revert
                continue;
            }

            // Defers Eye1's decision while the player is still at the bellbot hand-off cutscene RNV.
            if (string.Equals(name, "Eye1", StringComparison.OrdinalIgnoreCase) && currentRnv == Eye1HandoffCutsceneRnv)
            {
                _pendingUnrestoreChecks[i] = (name, itemAddress, _ctx.TickCount + UnrestoreCheckDelayTicks);
                continue;
            }

            _pendingUnrestoreChecks.RemoveAt(i);

            long? parentAddress = _mem.ReadInt64(itemAddress + GameOffsets.Parent);
            string? parentName = parentAddress is long p ? GameState.TryReadName(_mem, p) : null;
            var item = new CarryItemLocation(name, itemAddress, parentAddress, parentName);

            if (TryCompleteEar1IfBowlUnlocked(item, gameManager))
            {
                ReportAction(true, $"{name} left un-picked-up after the bowl unlocked - completed the pickup instead of reverting it");
                continue;
            }

            if (IsEye1MidBellbotHandoff(item))
            {
                ReportAction(true, $"{name} handed off by the bellbot - leaving it for the natural pickup to land instead of reverting");
                continue;
            }

            bool moved = RevertRestoration(item, persisted, gameManager);
            ReportAction(moved, moved
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

        long? actorAddr = GameState.FindDescendant(_mem, parentAddr, "ParrotNutBowlActor", "CParrotNutBowlActor");
        if (actorAddr is null)
            return false;

        int? state = _mem.ReadInt32(actorAddr.Value + GameOffsets.ParrotNutBowlActorStateOffset);
        if (state != GameOffsets.ParrotNutBowlActorStateUnlocked)
            return false;

        return GameActions.MoveItemSmart(_mem, item.Address, inventoryRoom, inventoryRoom, gameManager);
    }

    /// <summary>True when Eye1's light fixture ("6WTL", CLight, under Eye1's home View) reports _eyePresent == false,
    /// meaning the bellbot hand-off has completed and Eye1's revert should be skipped.</summary>
    private bool IsEye1MidBellbotHandoff(CarryItemLocation item)
    {
        if (!string.Equals(item.Name, "Eye1", StringComparison.OrdinalIgnoreCase))
            return false;
        if (_ctx.Project is not { } project)
            return false;

        long? homeParent = GameState.ResolveHomeParent(_mem, project, "Eye1");
        if (homeParent is null)
            return false;

        long? lightAddr = GameState.FindDescendant(_mem, homeParent.Value, "6WTL", "CLight");
        if (lightAddr is null)
            return false;

        byte[]? eyePresent = _mem.ReadBytes(lightAddr.Value + GameOffsets.LightEyePresentOffset, 1);
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
            GameActions.SetItemVisible(_mem, item.Address, true);
            if (ItemTracking.RequiresCanTakeRestoreToggle(item.Name))
                GameActions.SetItemCanTake(_mem, item.Address, true);
            ScheduleDirtyReassert();
        }

        return moved;
    }

    private bool RevertToInventory(CarryItemLocation item, long gameManager)
    {
        if (_ctx.InventoryRoom is null)
            return false;
        bool moved = GameActions.MoveItemSmart(_mem, item.Address, _ctx.InventoryRoom.Value, _ctx.InventoryRoom, gameManager);
        if (moved)
            GameActions.WriteItemPersistedState(_mem, item.Address, new ItemPersistedState(ItemStage.Inventory, false, ItemPulledFrom.None));
        return moved;
    }

    private bool RevertToHidden(CarryItemLocation item, ItemPersistedState persisted, long gameManager)
    {
        if (_ctx.InventoryRoom is null)
            return false;
        bool moved = GameActions.MoveItemToHiddenRoomFull(_mem, item.Address, _ctx.InventoryRoom.Value, gameManager);
        if (moved)
            GameActions.WriteItemPersistedState(_mem, item.Address, new ItemPersistedState(ItemStage.Hidden, persisted.CheckFired, ItemPulledFrom.None));
        return moved;
    }

    private bool RevertToMail(CarryItemLocation item, ItemPersistedState persisted, long gameManager)
    {
        if (_ctx.MailManRoom is null || _ctx.InventoryRoom is null)
            return false;
        bool moved = GameActions.MoveItemSmart(_mem, item.Address, _ctx.MailManRoom.Value, _ctx.InventoryRoom, gameManager);
        if (moved)
        {
            // PulledFrom.Mail is only recorded for ToolDelivered items, so ToolDelivered is restamped.
            GameActions.WriteItemPersistedState(_mem, item.Address,
                new ItemPersistedState(ItemStage.Mail, persisted.CheckFired, ItemPulledFrom.None, ToolDelivered: true));
        }
        return moved;
    }

    /// <summary>Delivers a tracked item to the player via the mail system.</summary>
    private bool DeliverToMail(CarryItemLocation item, long gameManager)
    {
        if (_ctx.InventoryRoom is null || _ctx.MailManRoom is null)
            return false;

        string destRoomName = _currentRoomName is not null && ChevronCodes.HasStation(_currentRoomName)
            ? _currentRoomName
            : "EmbLobby";
        uint? liveRoomFlags = GameState.ReadCurrentRoomFlags(_mem, _ctx.InventoryRoom.Value);
        ChevronCodes.TryGetCode(destRoomName, liveRoomFlags, out uint code);

        bool moved = GameActions.MoveItemSmart(_mem, item.Address, _ctx.MailManRoom.Value, _ctx.InventoryRoom, gameManager);
        bool ok = moved && GameActions.SetItemMailDestination(_mem, item.Address, code);
        if (ok)
        {
            GameActions.MarkItemAsToolPlaced(_mem, item.Address);
            if (ItemTracking.RequiresCanTakeOverride(item.Name))
                GameActions.SetItemCanTake(_mem, item.Address, true);
        }

        ReportAction(ok, ok
            ? $"Delivered AP grant for {item.Name} via mail, destination {destRoomName}"
            : $"Failed to deliver AP grant for {item.Name}");

        return ok;
    }
}
