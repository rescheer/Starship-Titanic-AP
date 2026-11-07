namespace StarshipTitanicAp;

/// <summary>Keeps the player's passenger class and stateroom assignments in line with the AP items received, and
/// temporarily spoofs class and stateroom state at the RNVs whose vanilla interactions (DeskBot upgrades, the SGT
/// stateroom puzzles) would otherwise be unreachable under item-gated progression.</summary>
internal sealed class ClassSync
{
    internal const string TitaniaRepairedLocation ="Titania's Room - Repair Titania";
    // Event-only shadow location sent alongside the real check above
    private const string TitaniaRepairedEventLocation = "Titania's Room - Repair Titania (Event)";

    private readonly SessionContext _ctx;

    /// <summary>AP items received as of the last class/stateroom sync; -1 forces the next sync to run.</summary>
    private int _lastItemsReceivedCount = -1;
    private int _lastStateroomItemsReceivedCount = -1;

    /// <summary>Whether the Repair Titania goal has been sent since the last resync.</summary>
    private bool _repairTitaniaGoalSent;

    public ClassSync(SessionContext ctx)
    {
        _ctx = ctx;
    }

    /// <summary>Forgets all spoof state and re-syncs from items next tick. Call when the game tree is rebuilt (a save
    /// load overwrites PassengerClass and the glyphs, along with any spoof on them).</summary>
    public void Reset()
    {
        _classUpgradeSpoofOriginalClass = null;
        _sgtGlyphAddr = null;
        _sgtGlyphOriginalMode = null;
        _pendingClassUpgrade = null;
        _currentRnv = null;
        ResyncFromItems();
    }

    /// <summary>Re-applies class and staterooms from the received items on the next sync, e.g. after reconnecting.</summary>
    public void ResyncFromItems()
    {
        _lastItemsReceivedCount = -1;
        _lastStateroomItemsReceivedCount = -1;
        _repairTitaniaGoalSent = false;
    }

    /// <summary>Applies the class and stateroom assignments implied by the received AP items. Runs before anything
    /// else reads or writes PassengerClass.</summary>
    public void SyncFromItems(long gameManager)
    {
        SyncPendingClassUpgrade(gameManager);
        SyncRepairedClass(gameManager);
        SyncPassengerClassFromItems(gameManager);
        SyncStateroomFromItems(gameManager);
        SyncRepairTitaniaGoal();
    }

    /// <summary>A DeskBot class upgrade ClassUpgradeHook blocked before the seed's class option was known, held until
    /// slot data arrives.</summary>
    private int? _pendingClassUpgrade;

    /// <summary>Applies a DeskBot upgrade held by <see cref="PollClassUpgradeHook"/> once slot data shows the seed has
    /// vanilla class upgrades; otherwise drops it, as the real class stays item-gated.</summary>
    private void SyncPendingClassUpgrade(long gameManager)
    {
        if (_pendingClassUpgrade is not { } newClass || _ctx.Ap.KnownSlotData is not { } slotData)
            return;

        _pendingClassUpgrade = null;
        if (ClassUpgradeTracker.IsVanilla(slotData))
            ApplyVanillaClassUpgrade(newClass, gameManager);
    }

    /// <summary>Under the Repair Titania goal, reports the goal once the loaded game says Titania is repaired -
    /// including a repair made while offline, which only gets here once slot data is back. Doesn't go by the repair's
    /// location, which another slot's collect can mark and new seeds of this goal don't have.</summary>
    private void SyncRepairTitaniaGoal()
    {
        if (_repairTitaniaGoalSent)
            return;
        if (!LocationChecks.IsRepairTitaniaGoal(_ctx.Ap.SlotData))
            return;
        if (!GameState.IsTitaniaRepaired(_ctx.Mem))
            return;

        _repairTitaniaGoalSent = true;
        // GoalLocationName is routed to SetGoalAchieved, and queued like any other check while offline.
        bool handedOff = _ctx.Ap.SendLocationCheck(LocationChecks.GoalLocationName);
        _ctx.Report.ReportAction(handedOff, handedOff
            ? "Goal complete: Titania repaired"
            : "Goal complete: Titania repaired (queued offline)");
    }

    /// <summary>Replaces a class 4 still held after Titania's repair - from a save made before the client stopped
    /// applying it, or one made during <see cref="TryApplyBridgeAccessSpoof"/> - with the real class. At a Bridge-access
    /// RNV it only corrects the spoof's backup, so the spoof stays.</summary>
    private void SyncRepairedClass(long gameManager)
    {
        if (_classUpgradeSpoofOriginalClass is not null)
            return;
        if (!GameState.IsTitaniaRepaired(_ctx.Mem))
            return;
        if (GameState.ReadPassengerClass(_ctx.Mem, gameManager) != SpecialPassengerClassValues.BridgeAccessClassValue)
            return;
        if (RealClassAfterRepair() is not { } realClass)
            return;

        if (_currentRnv is { } rnv && IsBridgeAccessRnv(rnv))
        {
            _classUpgradeSpoofOriginalClass = realClass;
            return;
        }

        bool ok = _ctx.InventoryRoom is { } petControlAddr
            ? GameActions.SetPassengerClassFull(_ctx.Mem, gameManager, petControlAddr, realClass)
            : GameActions.SetPassengerClass(_ctx.Mem, gameManager, realClass);
        _ctx.Report.ReportAction(ok, $"Titania repaired: restored {PassengerClassNames.GetName(realClass)} in place of Bridge access");
    }

    /// <summary>The class the player holds after Titania's repair, which the saved class can't be trusted for: the
    /// items' class, or under vanilla class upgrades the DeskBot upgrade checks, and Third (checked in) otherwise. Null
    /// until slot data is known.</summary>
    private int? RealClassAfterRepair()
    {
        if (_ctx.Ap.KnownSlotData is not { } slotData)
            return null;

        if (!ClassUpgradeTracker.IsVanilla(slotData))
            return ClassUpgradeTracker.ComputeClass(_ctx.Ap.GetReceivedItemNames(), slotData) ?? (int)PassengerClass.Third;

        foreach (int upgradedClass in new[] { (int)PassengerClass.First, (int)PassengerClass.Second })
        {
            if (LocationChecks.TryGetClassUpgradeLocationName(upgradedClass, out string locationName)
                && _ctx.Ap.IsLocationCheckedOrQueued(locationName))
                return upgradedClass;
        }
        return (int)PassengerClass.Third;
    }

    /// <summary>The player's RNV as of the last spoof pass; null until one runs after a reset.</summary>
    private RoomNodeView? _currentRnv;

    /// <summary>Undoes any spoof that doesn't apply at the player's new RNV.</summary>
    public void RevertSpoofs(RoomNodeView newRnv, long gameManager)
    {
        TryRevertClassUpgradeSpoof(newRnv, gameManager);
        TryRevertSgtGlyphSpoof(newRnv);
    }

    /// <summary>Starts any spoof the player's new RNV calls for.</summary>
    public void ApplySpoofs(RoomNodeView rnv, long gameManager)
    {
        _currentRnv = rnv;
        TryApplyClassUpgradeSpoof(rnv, gameManager);
        TryApplyBridgeAccessSpoof(rnv, gameManager);
        TryApplySgtGlyphSpoof(rnv);
    }

    private const int TitaniaRoomNum = 37;
    private const int BridgeDoorNodeNum = 1; // Titania.Node 1.S (37,1,3) holds the Bridge door
    private const int BridgeRoomNum = 43;

    /// <summary>Whether the RNV is in the Bridge door's node or on the Bridge, where a repaired game reports Bridge
    /// access. Spans the whole node and the Bridge so the class is already 4 when the door's view is entered (from a
    /// turn or the Bridge's exit) and its cursor is worked out. Titania's lift (CExitTiania, at (37,2,5)) is outside it.</summary>
    private static bool IsBridgeAccessRnv(RoomNodeView rnv) =>
        (rnv.Room == TitaniaRoomNum && rnv.Node == BridgeDoorNodeNum) || rnv.Room == BridgeRoomNum;

    /// <summary>Temporarily reports class 4 (Bridge access) at <see cref="IsBridgeAccessRnv"/> RNVs once Titania is
    /// repaired - vanilla sets it for good, which also locks the Titania lift (CExitTiania refuses class 4) and every
    /// class-gated door elsewhere. Shares the class-upgrade spoof's backup, so <see cref="TryRevertClassUpgradeSpoof"/>
    /// restores the real class on leaving and item upgrades received meanwhile still land.</summary>
    private void TryApplyBridgeAccessSpoof(RoomNodeView rnv, long gameManager)
    {
        if (!IsBridgeAccessRnv(rnv) || _classUpgradeSpoofOriginalClass is not null)
            return;
        if (!GameState.IsTitaniaRepaired(_ctx.Mem))
            return;

        int? currentClass = GameState.ReadPassengerClass(_ctx.Mem, gameManager);
        // Already 4: an unmigrated class, which SyncRepairedClass turns into this spoof's backup once it can.
        if (currentClass is null || currentClass.Value == SpecialPassengerClassValues.BridgeAccessClassValue)
            return;

        if (GameActions.SetPassengerClass(_ctx.Mem, gameManager, SpecialPassengerClassValues.BridgeAccessClassValue))
        {
            _classUpgradeSpoofOriginalClass = currentClass.Value;
            _ctx.Report.ReportAction(true, $"Bridge access spoof: reporting class 4 (really {PassengerClassNames.GetName(currentClass.Value)})");
        }
    }

    /// <summary>Handles a class upgrade that ClassUpgradeHook blocked: sends the matching AP check (applying the
    /// upgrade too under vanilla class upgrades), or applies the upgrade directly for Bridge access (and for the
    /// initial None -> Third, if allowed).</summary>
    public void PollClassUpgradeHook(long gameManager)
    {
        if (!ClassUpgradeHook.IsInstalled)
            return;
        int? attemptedClass = ClassUpgradeHook.PollAttemptedClass(_ctx.Mem);
        if (attemptedClass is null)
            return;

        if (LocationChecks.TryGetClassUpgradeLocationName(attemptedClass.Value, out string locationName))
        {
            if (_ctx.Ap.KnownSlotData is null)
            {
                // Option not known yet: SyncPendingClassUpgrade applies it once slot data arrives, if vanilla.
                _pendingClassUpgrade = attemptedClass.Value;
                _ctx.Report.AppendLog($"ClassUpgradeHook: blocked setPassengerClass({PassengerClassNames.GetName(attemptedClass.Value)}) before slot data - held until the class option is known");
            }
            else if (ClassUpgradeTracker.IsVanilla(_ctx.Ap.KnownSlotData))
                ApplyVanillaClassUpgrade(attemptedClass.Value, gameManager);
            else
                _ctx.Report.AppendLog($"ClassUpgradeHook: blocked setPassengerClass({PassengerClassNames.GetName(attemptedClass.Value)}) from taking effect - real class stays item-gated");

            bool handedOff = _ctx.Ap.SendLocationCheck(locationName);
            if (LocationChecks.TryGetClassUpgradeEventLocationName(attemptedClass.Value, out string eventLocationName))
                _ctx.Ap.SendLocationCheck(eventLocationName);
            _ctx.Report.ReportAction(handedOff, handedOff
                ? $"DeskBot upgrade attempt ({PassengerClassNames.GetName(attemptedClass.Value)}) -> {locationName}"
                : $"DeskBot upgrade attempt ({PassengerClassNames.GetName(attemptedClass.Value)}) queued (offline) -> {locationName}");
        }
        else if (attemptedClass.Value == SpecialPassengerClassValues.BridgeAccessClassValue)
        {
            // Not applied: class 4 would also lock the Titania lift and every class-gated door on the ship. The real
            // class stays, and TryApplyBridgeAccessSpoof reports 4 only around the Bridge door.
            bool handedOff = _ctx.Ap.SendLocationCheck(TitaniaRepairedLocation);
            _ctx.Ap.SendLocationCheck(TitaniaRepairedEventLocation);
            _ctx.Report.ReportAction(handedOff, handedOff
                ? $"Bridge access granted -> {TitaniaRepairedLocation}"
                : $"Bridge access granted -> {TitaniaRepairedLocation} (queued offline)");
            _ctx.Report.NotifyPendingChecksChanged();
        }
        else if (attemptedClass.Value == (int)PassengerClass.Third && _ctx.Options.AllowInitialUpgrade)
        {
            if (_ctx.InventoryRoom is not null)
            {
                // During the no-room spoof this re-runs check-in, and the spoof's revert restores the real class.
                if (_classUpgradeSpoofOriginalClass is null)
                    RecordPriorClass(gameManager);
                bool ok = GameActions.SetPassengerClassFull(_ctx.Mem, gameManager, _ctx.InventoryRoom.Value, attemptedClass.Value);
                _ctx.Report.ReportAction(ok, "Initial class upgrade (None -> Third) applied directly - no AP item for this yet");
            }
            else
            {
                _ctx.Report.ReportAction(false, "Initial class upgrade blocked - inventory not resolved yet, will retry on next attempt");
            }
        }
        else
        {
            _ctx.Report.ReportAction(false, $"DeskBot upgrade attempt for unrecognized class {attemptedClass.Value}");
        }
    }

    /// <summary>Lets a DeskBot upgrade the hook caught take effect, for seeds with vanilla class upgrades. During a
    /// class spoof it replaces the backed-up class, so <see cref="TryRevertClassUpgradeSpoof"/> keeps it.</summary>
    private void ApplyVanillaClassUpgrade(int newClass, long gameManager)
    {
        RecordPriorClass(gameManager);

        if (_classUpgradeSpoofOriginalClass is not null)
        {
            _classUpgradeSpoofOriginalClass = newClass;
            _ctx.Report.ReportAction(true, $"DeskBot upgrade to {PassengerClassNames.GetName(newClass)} (vanilla class upgrades, applied once the class spoof ends)");
            return;
        }

        bool ok = _ctx.InventoryRoom is { } petControlAddr
            ? GameActions.SetPassengerClassFull(_ctx.Mem, gameManager, petControlAddr, newClass)
            : GameActions.SetPassengerClass(_ctx.Mem, gameManager, newClass);
        _ctx.Report.ReportAction(ok, $"DeskBot upgrade to {PassengerClassNames.GetName(newClass)} applied (vanilla class upgrades)");
    }

    /// <summary>Saves the real (unspoofed) class as _priorClass ahead of a real class change, as the game's
    /// setPassengerClass() would - ClassUpgradeHook keeps that call from running.</summary>
    private void RecordPriorClass(long gameManager)
    {
        int? realClass = _classUpgradeSpoofOriginalClass ?? GameState.ReadPassengerClass(_ctx.Mem, gameManager);
        if (realClass is not null)
            GameActions.SetPriorClass(_ctx.Mem, gameManager, realClass.Value);
    }

    /// <summary>Applies the class upgrade implied by AP items received so far, if any. With no class items yet,
    /// clamps a First/Second class (only reachable via a spoof that was saved or never reverted) back to Third.
    /// While a class spoof is active, updates the spoof's backed-up original class instead of the live value, so
    /// <see cref="TryRevertClassUpgradeSpoof"/> restores the new class rather than the stale one.</summary>
    private void SyncPassengerClassFromItems(long gameManager)
    {
        IReadOnlyDictionary<string, object>? slotData = _ctx.Ap.SlotData;
        if (slotData is null || ClassUpgradeTracker.IsVanilla(slotData))
            return;
        if (_ctx.InventoryRoom is null)
            return;

        string[] receivedItems = _ctx.Ap.GetReceivedItemNames();
        if (receivedItems.Length == _lastItemsReceivedCount)
            return;
        _lastItemsReceivedCount = receivedItems.Length;

        int? currentClass = _classUpgradeSpoofOriginalClass ?? GameState.ReadPassengerClass(_ctx.Mem, gameManager);
        if (currentClass is null)
            return;

        // Bridge access shares its raw value with None - after the repair, SyncRepairedClass replaces it instead.
        if (currentClass.Value == SpecialPassengerClassValues.BridgeAccessClassValue
            && GameState.IsTitaniaRepaired(_ctx.Mem))
            return;

        int? targetClass = ClassUpgradeTracker.ComputeClass(receivedItems, slotData);
        if (targetClass is null)
        {
            if (currentClass.Value != (int)PassengerClass.First && currentClass.Value != (int)PassengerClass.Second)
                return;
            targetClass = (int)PassengerClass.Third;
        }

        if (currentClass == targetClass)
            return;

        RecordPriorClass(gameManager);

        if (_classUpgradeSpoofOriginalClass is not null)
        {
            _classUpgradeSpoofOriginalClass = targetClass.Value;
            _ctx.Report.ReportAction(true, $"Class upgrade from items: {PassengerClassNames.GetName(targetClass.Value)} (applied once the class spoof ends)");
            return;
        }

        bool ok = GameActions.SetPassengerClassFull(_ctx.Mem, gameManager, _ctx.InventoryRoom.Value, targetClass.Value);
        _ctx.Report.ReportAction(ok, $"Class upgrade from items: {PassengerClassNames.GetName(targetClass.Value)}");
    }

    /// <summary>Applies the stateroom assignment(s) implied by "Progressive Stateroom" AP items received so far,
    /// if any are still outstanding, via GameActions.AssignNextRoom. Assigns every outstanding tier in order:
    /// SGT/Third, then Second, then First.</summary>
    private void SyncStateroomFromItems(long gameManager)
    {
        IReadOnlyDictionary<string, object>? slotData = _ctx.Ap.SlotData;
        if (slotData is null || StateroomAssignTracker.IsVanilla(slotData))
            return;
        if (_ctx.InventoryRoom is not { } petControlAddr)
            return;

        string[] receivedItems = _ctx.Ap.GetReceivedItemNames();
        if (receivedItems.Length == _lastStateroomItemsReceivedCount)
            return;
        _lastStateroomItemsReceivedCount = receivedItems.Length;

        int? targetCount = StateroomAssignTracker.ComputeTargetAssignedCount(receivedItems, slotData);
        if (targetCount is null)
            return;

        int achievedCount = GameActions.GetAchievedStateroomClass(_ctx.Mem, petControlAddr);
        for (int next = achievedCount + 1; next <= targetCount.Value; next++)
        {
            if (StateroomAssignTracker.ClassForAssignedCount(next) is not { } rawClass)
                break;

            bool ok = GameActions.AssignNextRoom(_ctx.Mem, gameManager, petControlAddr, rawClass);
            _ctx.Report.ReportAction(ok, $"Stateroom assignment from items: {PassengerClassNames.GetName(rawClass)}");
            if (!ok)
                break;
        }
    }

    /// <summary>The DeskBot's own RNV in the Embarkation Lobby, where both class-upgrade interactions happen.</summary>
    private static readonly RoomNodeView DeskBotRnv = new(2, 4, 1);

    /// <summary>The Embarkation Lobby RNV the game force-turns the player to right after the DeskBot's very first
    /// (No Class) conversation, for a scripted Bellbot intro. An active spoof reports Third here; this RNV never
    /// starts a spoof on its own.</summary>
    private static readonly RoomNodeView BellBotIntroRnv = new(2, 4, 2);

    /// <summary>Every RNV that can trigger or sustain a class-upgrade spoof, mapped to the PassengerClass value it
    /// should report there (raw values: 4 = no class, ..., 1 = First, 0 = First with Bridge access). Covers the
    /// DeskBot, the SGT Class Stateroom and its TV, the [22,1,2] corridor spot leading into it, and the doorway
    /// RNVs just outside each bottom-floor SGT room.</summary>
    private static readonly Dictionary<RoomNodeView, int> ClassSpoofTriggerRnvs = new()
    {
        [DeskBotRnv] = (int)PassengerClass.Third,
        [new RoomNodeView(27, 4, 1)] = (int)PassengerClass.Third,  // SGT TV for Magazine puzzle
        // SGT Staterooms and ground-floor entrances for the Magazine voice line
        [new RoomNodeView(27, 1, 2)] = (int)PassengerClass.Third,  // SGT Stateroom
        [new RoomNodeView(22, 1, 2)] = (int)PassengerClass.Third,  // SGT LittleLift
        [new RoomNodeView(11, 4, 2)] = (int)PassengerClass.Third,  // SGT Room 1
        [new RoomNodeView(11, 5, 2)] = (int)PassengerClass.Third,  // SGT Room 2
        [new RoomNodeView(11, 6, 2)] = (int)PassengerClass.Third,  // SGT Room 3
        [new RoomNodeView(11, 7, 2)] = (int)PassengerClass.Third,  // SGT Room 4
        [new RoomNodeView(11, 8, 2)] = (int)PassengerClass.Third,  // SGT Room 5
        [new RoomNodeView(11, 9, 2)] = (int)PassengerClass.Third,  // SGT Room 6
    };

    /// <summary>Backs up the true PassengerClass value while <see cref="TryApplyClassUpgradeSpoof"/> has temporarily
    /// overwritten it, so <see cref="TryRevertClassUpgradeSpoof"/> can put it back. Null when not currently spoofed.</summary>
    private int? _classUpgradeSpoofOriginalClass;

    /// <summary>Temporarily overwrites PassengerClass while the player is at one of <see cref="ClassSpoofTriggerRnvs"/>
    /// (or continues an active spoof at <see cref="BellBotIntroRnv"/>), so the vanilla class-gated DeskBot/SGT TV
    /// interactions run. Reports Third at the SGT TV and staterooms for a pending Magazine pickup, Second at the
    /// DeskBot for a pending 1st Class upgrade, and None there when no room has been assigned yet. The true value is
    /// saved in
    /// <see cref="_classUpgradeSpoofOriginalClass"/>.</summary>
    private void TryApplyClassUpgradeSpoof(RoomNodeView rnv, long gameManager)
    {
        if (rnv == BellBotIntroRnv)
        {
            // Only continues an already-active spoof.
            if (_classUpgradeSpoofOriginalClass is null)
                return;

            int? reportedClass = GameState.ReadPassengerClass(_ctx.Mem, gameManager);
            if (reportedClass is not null && reportedClass.Value != (int)PassengerClass.Third
                && GameActions.SetPassengerClass(_ctx.Mem, gameManager, (int)PassengerClass.Third))
            {
                _ctx.Report.ReportAction(true, $"Class upgrade spoof: reporting {PassengerClassNames.GetName((int)PassengerClass.Third)} for Bellbot intro (really {PassengerClassNames.GetName(_classUpgradeSpoofOriginalClass.Value)})");
            }
            return;
        }

        if (!ClassSpoofTriggerRnvs.TryGetValue(rnv, out int configuredSpoofClass))
            return;
        if (_ctx.InventoryRoom is null)
            return;
        if (_classUpgradeSpoofOriginalClass is not null)
            return;
        if (!LocationChecks.TryGetClassUpgradeLocationName((int)PassengerClass.Second, out string secondLocationName))
            return;
        if (!LocationChecks.TryGetClassUpgradeLocationName((int)PassengerClass.First, out string firstLocationName))
            return;

        int? currentClass = GameState.ReadPassengerClass(_ctx.Mem, gameManager);
        if (currentClass is null)
            return;

        int? spoofToClass = null;
        // Under vanilla class upgrades the real class is already what the DeskBot's upgrades need.
        bool vanillaClass = ClassUpgradeTracker.IsVanilla(_ctx.Ap.KnownSlotData);
        // Queued counts as checked, so a check made while offline still advances the spoof.
        bool secondClassPending = !_ctx.Ap.IsLocationCheckedOrQueued(secondLocationName);
        // Tracked separately from the 2nd Class check: a Magazine granted early can be handed to the DeskBot before
        // the SGT TV puzzle has been done.
        bool magazineCheckPending = !_ctx.Ap.IsLocationCheckedOrQueued(MagazineItem.Definition.PickupLocationName!);
        // None before Titania's repair means the player hasn't checked in yet (after it, None is Bridge access).
        bool notCheckedIn = currentClass.Value == (int)PassengerClass.None
            && !GameState.IsTitaniaRepaired(_ctx.Mem);

        // Not checked in: leave the DeskBot unspoofed so the check-in conversation runs first - otherwise the player
        // can never leave the Embarkation Lobby. Once it pays out, DeskBotMagazinePatch goes on, so a Magazine
        // hand-off works even in the same visit, and later visits spoof as usual.
        if (rnv == DeskBotRnv && notCheckedIn)
            return;

        // The Magazine hand-off needs no spoof: DeskBotMagazinePatch lifts its class gates.
        if (rnv != DeskBotRnv && currentClass.Value < (int)PassengerClass.Third && magazineCheckPending)
        {
            // The SGT TV's Magazine payout and the stateroom's "you may have won an upgrade" line both require
            // exactly Third.
            spoofToClass = configuredSpoofClass;
        }
        else if (rnv == DeskBotRnv && !vanillaClass && !secondClassPending
            && !_ctx.Ap.IsLocationCheckedOrQueued(firstLocationName))
        {
            // DeskbotScript's 1st Class upgrade pays out only at exactly Second.
            if (currentClass.Value != (int)PassengerClass.Second)
                spoofToClass = (int)PassengerClass.Second;
        }
        else if (rnv == DeskBotRnv
            && currentClass.Value != (int)PassengerClass.None
            && !(vanillaClass && currentClass.Value <= (int)PassengerClass.Second)
            && !GameActions.HasAnyAssignedRoom(_ctx.Mem, _ctx.InventoryRoom.Value))
        {
            // No room assigned yet: report None so the initial DeskBot conversation and room assignment run. Not under
            // vanilla class upgrades once upgraded: the player has checked in, and None would hide the 1st Class upgrade
            // until a Progressive Stateroom arrives.
            spoofToClass = (int)PassengerClass.None;
        }

        if (spoofToClass is null)
            return;

        if (GameActions.SetPassengerClass(_ctx.Mem, gameManager, spoofToClass.Value))
        {
            _classUpgradeSpoofOriginalClass = currentClass.Value;
            _ctx.Report.ReportAction(true, $"Class upgrade spoof: reporting {PassengerClassNames.GetName(spoofToClass.Value)} (really {PassengerClassNames.GetName(currentClass.Value)})");
        }
    }

    /// <summary>Whether the player has really checked in with the DeskBot: their true class (behind any spoof) isn't
    /// None, or Titania is repaired (after which None is Bridge access). Null if the class can't be read.</summary>
    public bool? IsCheckedIn(long gameManager)
    {
        int? realClass = _classUpgradeSpoofOriginalClass ?? GameState.ReadPassengerClass(_ctx.Mem, gameManager);
        if (realClass is null)
            return null;
        return realClass.Value != (int)PassengerClass.None || GameState.IsTitaniaRepaired(_ctx.Mem);
    }

    /// <summary>The state <see cref="SyncDeskBotMagazinePatch"/> last failed to reach, so a failing install or
    /// uninstall is reported once rather than every tick.</summary>
    private bool? _deskBotMagazinePatchFailedState;

    /// <summary>Applies <see cref="DeskBotMagazinePatch"/> while the 2nd Class check is pending and the player has
    /// checked in, so the Magazine hand-off pays out at any class (and through the no-room None spoof). Removed
    /// otherwise: before check-in the vanilla gates make the DeskBot eat the Magazine, and ItemSync returns it; once the
    /// check is in, the vanilla gates stop a First class player being handed a Second class upgrade.</summary>
    public void SyncDeskBotMagazinePatch(long gameManager)
    {
        if (!LocationChecks.TryGetClassUpgradeLocationName((int)PassengerClass.Second, out string secondLocationName))
            return;
        if (IsCheckedIn(gameManager) is not { } checkedIn)
            return;

        bool wanted = checkedIn && !_ctx.Ap.IsLocationCheckedOrQueued(secondLocationName);
        if (wanted == DeskBotMagazinePatch.IsInstalled)
        {
            _deskBotMagazinePatchFailedState = null;
            return;
        }
        if (_deskBotMagazinePatchFailedState == wanted)
            return;

        bool ok = wanted ? DeskBotMagazinePatch.Install(_ctx.Mem) : DeskBotMagazinePatch.Uninstall(_ctx.Mem);
        _deskBotMagazinePatchFailedState = ok ? null : wanted;
        _ctx.Report.ReportAction(ok, wanted
            ? "Install DeskBot Magazine hand-off patch (2nd Class check pending)"
            : "Uninstall DeskBot Magazine hand-off patch");
    }

    /// <summary>Restores the true PassengerClass value once the player moves to an RNV that isn't one of
    /// <see cref="ClassSpoofTriggerRnvs"/> (nor <see cref="BellBotIntroRnv"/>), undoing
    /// <see cref="TryApplyClassUpgradeSpoof"/> and redrawing the PET.</summary>
    private void TryRevertClassUpgradeSpoof(RoomNodeView newRnv, long gameManager)
    {
        if (_classUpgradeSpoofOriginalClass is not { } originalClass)
            return;
        if (ClassSpoofTriggerRnvs.ContainsKey(newRnv) || newRnv == BellBotIntroRnv || IsBridgeAccessRnv(newRnv))
            return;

        if (_ctx.InventoryRoom is { } petControlAddr)
            GameActions.SetPassengerClassFull(_ctx.Mem, gameManager, petControlAddr, originalClass);
        else
            GameActions.SetPassengerClass(_ctx.Mem, gameManager, originalClass);

        _classUpgradeSpoofOriginalClass = null;
    }

    /// <summary>The two RNVs inside the player's own randomly-assigned SGT Class Stateroom where the bed/TV
    /// puzzle plays out.</summary>
    private static readonly RoomNodeView[] SgtGlyphSpoofTriggerRnvs =
    {
        new(27, 1, 2), // SGT Stateroom
        new(27, 4, 1), // SGT TV
    };

    private long? _sgtGlyphAddr;
    private int? _sgtGlyphOriginalMode;

    /// <summary>Temporarily re-flags the player's RGM_PREV_ASSIGNED_ROOM SGT Class Stateroom glyph as RGM_ASSIGNED_ROOM
    /// while the player is in one of <see cref="SgtGlyphSpoofTriggerRnvs"/>, so the SGT bed/TV puzzle can be completed.
    /// The glyph is found by its encoded class + mode via GameActions.FindPrevAssignedGlyphForClass.
    /// <see cref="TryRevertSgtGlyphSpoof"/> restores the original mode.</summary>
    private void TryApplySgtGlyphSpoof(RoomNodeView rnv)
    {
        if (_sgtGlyphAddr is not null)
            return; // already spoofed
        if (Array.IndexOf(SgtGlyphSpoofTriggerRnvs, rnv) < 0)
            return;
        if (_ctx.InventoryRoom is not { } petControlAddr)
            return;

        long? glyphAddr = GameActions.FindPrevAssignedGlyphForClass(_ctx.Mem, petControlAddr, (int)PassengerClass.Third);
        if (glyphAddr is null)
            return; // no prev-assigned Third-class glyph - either never upgraded, or nothing to spoof

        int? currentMode = GameActions.ReadGlyphMode(_ctx.Mem, glyphAddr.Value);
        if (currentMode is null || currentMode == GameOffsets.RgmAssignedRoom)
            return; // unreadable, or already the real assignment - nothing to spoof

        if (GameActions.WriteGlyphMode(_ctx.Mem, glyphAddr.Value, GameOffsets.RgmAssignedRoom))
        {
            _sgtGlyphAddr = glyphAddr.Value;
            _sgtGlyphOriginalMode = currentMode.Value;
            _ctx.Report.ReportAction(true, "SGT Stateroom glyph spoof: reporting assigned (really prev-assigned)");
        }
    }

    /// <summary>Restores the SGT Class Stateroom glyph's true _mode once the player leaves <see cref="SgtGlyphSpoofTriggerRnvs"/>,
    /// undoing <see cref="TryApplySgtGlyphSpoof"/>.</summary>
    private void TryRevertSgtGlyphSpoof(RoomNodeView newRnv)
    {
        if (_sgtGlyphAddr is not { } glyphAddr || _sgtGlyphOriginalMode is not { } originalMode)
            return;
        if (Array.IndexOf(SgtGlyphSpoofTriggerRnvs, newRnv) >= 0)
            return;

        GameActions.WriteGlyphMode(_ctx.Mem, glyphAddr, originalMode);

        _sgtGlyphAddr = null;
        _sgtGlyphOriginalMode = null;
    }
}
