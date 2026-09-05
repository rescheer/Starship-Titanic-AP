namespace StarshipTitanicAp;

public sealed partial class MainForm
{
    /// <summary>Computes and caches whether it's safe to act on the currently attached save under the currently connected AP seed.
    /// Re-verifies periodically once a verdict is reached, so swapping to a different save (or starting a new game) without
    /// reattaching/reconnecting is still caught instead of silently keeping the stale verdict forever.</summary>
    private void EvaluateSaveSeedGuard(long gameManager)
    {
        if (_saveSeedGuardState != SaveSeedGuardState.Unverified
            && _tickCount % SaveSeedGuardRecheckIntervalTicks != 0)
            return;
        if (_apConnection.SeedName is not { } seedName)
            return;
        if (_currentProject is not { } project)
            return;

        long? beamBridgeAddr = SaveSeedGuard.FindBeamBridgeAddress(_mem, project);
        if (beamBridgeAddr is null)
        {
            _saveSeedGuardBeamBridgeMisses++;
            if (_saveSeedGuardBeamBridgeMisses > SaveSeedGuardBeamBridgeMissLimit)
            {
                BlockSaveSeedGuard("Save/seed guard: could not locate the BeamBridge item to verify this save's AP seed tag - blocking AP actions defensively. Force with !force_seed in game or client.");
            }
            return;
        }
        _saveSeedGuardBeamBridgeMisses = 0;

        long tag = SaveSeedGuard.ComputeSeedTag(seedName);
        long? stored = SaveSeedGuard.ReadStoredSeedTag(_mem, beamBridgeAddr.Value);

        if (stored is null)
        {
            BlockSaveSeedGuard("Save/seed guard: failed to read BeamBridge's guard tag - blocking AP actions defensively");
            return;
        }

        if (stored.Value == tag)
        {
            _saveSeedGuardTagMismatches = 0;
            _saveSeedGuardState = SaveSeedGuardState.Ok;
            return;
        }

        if (stored.Value == 0)
        {
            _saveSeedGuardTagMismatches = 0;
            bool? petActive = GameState.ReadPetActive(_mem, gameManager);
            if (petActive == false)
            {
                if (SaveSeedGuard.WriteSeedTag(_mem, beamBridgeAddr.Value, tag))
                {
                    _saveSeedGuardState = SaveSeedGuardState.Ok;
                    const string taggedMessage = "Save/seed guard: tagged fresh save with current AP seed";
                    ShowActionResult(true, taggedMessage);
                    AppendServerLog($"CLIENT: {taggedMessage}", bold: true);

                    long? inventoryRoom = _currentInventoryRoom
                        ?? GameState.FindInventoryRoom(_mem, project);
                    if (inventoryRoom is not null)
                    {
                        GameActions.DisplayMessageSmart(_mem, inventoryRoom.Value,
                            "AP: This fresh save has been tagged and linked to the connected AP seed.");
                    }
                }
                else
                {
                    BlockSaveSeedGuard("Save/seed guard: failed to tag fresh save - blocking AP actions defensively.");
                }
            }
            else
            {
                BlockSaveSeedGuard("Save/seed guard: existing save has no AP seed tag (played without the client?) - blocking AP actions. Force with !force_seed in game or client.");
            }
            return;
        }

        // A mismatched (non-zero, non-matching) tag can also be transient garbage in the BeamBridge object's
        // unused field if it's read while a save is still mid-load and not yet fully constructed - especially
        // likely if the AP connection (and so this guard's polling) was established before loading into a game,
        // since evaluation then starts the instant a project resolves rather than once things have settled.
        // Require a couple seconds of consecutive mismatched reads before trusting it.
        _saveSeedGuardTagMismatches++;
        if (_saveSeedGuardTagMismatches < SaveSeedGuardTagMismatchLimit)
            return;

        BlockSaveSeedGuard("Save/seed guard: this save belongs to a different AP seed - blocking AP actions to avoid corrupting it. Force with !force_seed in game or client.");
    }

    /// <summary>Common landing spot for every way EvaluateSaveSeedGuard can end up Blocked.</summary>
    private void BlockSaveSeedGuard(string reason)
    {
        bool alreadyBlocked = _saveSeedGuardState == SaveSeedGuardState.Blocked;
        _saveSeedGuardState = SaveSeedGuardState.Blocked;
        if (alreadyBlocked)
            return;

        ShowActionResult(false, reason);
        AppendServerLog($"CLIENT: {reason}", bold: true);

        long? inventoryRoom = _currentInventoryRoom;
        if (inventoryRoom is null && _currentProject is { } project)
            inventoryRoom = GameState.FindInventoryRoom(_mem, project);

        if (inventoryRoom is not null)
        {
            GameActions.DisplayMessageSmart(_mem, inventoryRoom.Value,
                "AP: This save's seed tag doesn't match the connected seed, so item/check syncing is paused. " +
                "If you're sure this save and seed really belong together, type !force_seed in game or in the client to bypass the guard.");
        }
    }

    /// <summary>Surfaces a !force_seed failure in-game, best-effort (the inventory room may not be known yet, e.g. before attaching).</summary>
    private void ShowForceSeedFailureInGame(string text)
    {
        long? inventoryRoom = _currentInventoryRoom;
        if (inventoryRoom is null && _currentProject is { } project)
            inventoryRoom = GameState.FindInventoryRoom(_mem, project);

        if (inventoryRoom is not null)
            GameActions.DisplayMessageSmart(_mem, inventoryRoom.Value, text);
    }

    /// <summary>Handles the "!force_seed" command, a local-only override for the save/AP-seed guard.</summary>
    private void HandleForceSeedCommand()
    {
        if (!_mem.IsAttached || _currentGameManager is null)
        {
            ShowActionResult(false, "!force_seed: not attached / game not resolved yet");
            ShowForceSeedFailureInGame("AP: !force_seed failed - client isn't attached to the game yet.");
            return;
        }
        if (_apConnection.SeedName is not { } seedName)
        {
            ShowActionResult(false, "!force_seed: not connected to an AP server - no seed to tag with");
            ShowForceSeedFailureInGame("AP: !force_seed failed - not connected to an AP server.");
            return;
        }
        if (_currentProject is not { } project)
        {
            ShowActionResult(false, "!force_seed: project not resolved yet");
            ShowForceSeedFailureInGame("AP: !force_seed failed - game state not resolved yet, try again in a moment.");
            return;
        }

        long? beamBridgeAddr = SaveSeedGuard.FindBeamBridgeAddress(_mem, project);
        if (beamBridgeAddr is null)
        {
            ShowActionResult(false, "!force_seed: could not locate BeamBridge to tag");
            ShowForceSeedFailureInGame("AP: !force_seed failed - could not locate the BeamBridge item to tag.");
            return;
        }

        long tag = SaveSeedGuard.ComputeSeedTag(seedName);
        bool ok = SaveSeedGuard.WriteSeedTag(_mem, beamBridgeAddr.Value, tag);
        if (ok)
            _saveSeedGuardState = SaveSeedGuardState.Ok;

        string result = ok
            ? "!force_seed: save/AP-seed guard bypassed - this save is now tagged with the current seed"
            : "!force_seed: failed to write the guard tag";
        ShowActionResult(ok, result);
        AppendServerLog($"CLIENT: {result}", bold: true);

        if (_currentInventoryRoom is not null)
        {
            GameActions.DisplayMessageSmart(_mem, _currentInventoryRoom.Value, ok
                ? "AP: Save/seed guard bypassed - AP actions are re-enabled for this save."
                : "AP: !force_seed failed - see client log.");
        }
    }

    /// <summary>Applies the class upgrade implied by AP items received so far, if any.</summary>
    private void SyncPassengerClassFromItems(long gameManager)
    {
        IReadOnlyDictionary<string, object>? slotData = _apConnection.SlotData;
        if (slotData is null)
            return;
        if (_currentInventoryRoom is null)
            return;

        string[] receivedItems = _apConnection.GetReceivedItemNames();
        if (receivedItems.Length == _lastItemsReceivedCount)
            return;
        _lastItemsReceivedCount = receivedItems.Length;

        int? targetClass = ClassUpgradeTracker.ComputeClass(receivedItems, slotData);
        if (targetClass is null)
            return;

        int? currentClass = GameState.ReadPassengerClass(_mem, gameManager);
        if (currentClass == targetClass)
            return;

        bool ok = GameActions.SetPassengerClassFull(_mem, gameManager, _currentInventoryRoom.Value, targetClass.Value);
        ShowActionResult(ok, $"Class upgrade from items: {PassengerClassNames.GetName(targetClass.Value)}");

        // Any DeskBot upgrade location(s) this jump passes over or lands on are left for TryApplyClassUpgradeSpoof
        // to send once the player actually visits the DeskBot - it lets the vanilla interaction run for real
        // (spoofing PassengerClass down just long enough for the game's own eligibility check to pass), so it also
        // gets the real petReassignRoom() room-assignment side effect that just sending the check here would miss.
    }

    /// <summary>Applies the stateroom assignment(s) implied by "Progressive Stateroom" AP items received so far,
    /// if any are still outstanding. Unlike SyncPassengerClassFromItems (a plain field write), each assignment
    /// has to run through GameActions.AssignNextRoom - see RoomAssignHook's own doc comment for why. Catches up
    /// more than one tier in a single pass (e.g. two items granted while offline), assigning SGT/Third first,
    /// then Second, then First, in order.</summary>
    private void SyncStateroomFromItems(long gameManager)
    {
        IReadOnlyDictionary<string, object>? slotData = _apConnection.SlotData;
        if (slotData is null)
            return;
        if (_currentInventoryRoom is not { } petControlAddr)
            return;

        string[] receivedItems = _apConnection.GetReceivedItemNames();
        if (receivedItems.Length == _lastStateroomItemsReceivedCount)
            return;
        _lastStateroomItemsReceivedCount = receivedItems.Length;

        int? targetCount = StateroomAssignTracker.ComputeTargetAssignedCount(receivedItems, slotData);
        if (targetCount is null)
            return;

        int achievedCount = GameActions.GetAchievedStateroomClass(_mem, petControlAddr);
        for (int next = achievedCount + 1; next <= targetCount.Value; next++)
        {
            if (StateroomAssignTracker.ClassForAssignedCount(next) is not { } rawClass)
                break;

            bool ok = GameActions.AssignNextRoom(_mem, gameManager, petControlAddr, rawClass);
            ShowActionResult(ok, $"Stateroom assignment from items: {PassengerClassNames.GetName(rawClass)}");
            if (!ok)
                break;
        }
    }

    /// <summary>The DeskBot's own RNV in the Embarkation Lobby, where both class-upgrade interactions happen.</summary>
    private static readonly RoomNodeView DeskBotRnv = new(2, 4, 1);

    /// <summary>The Embarkation Lobby RNV the game force-turns the player to right after the DeskBot's very first
    /// (No Class) conversation, for a scripted Bellbot intro. That conversation is itself gated on PassengerClass
    /// reading Third specifically - if the DeskBot visit was spoofed down to None (see
    /// <see cref="TryApplyClassUpgradeSpoof"/>'s no-assigned-room case), the spoof needs to be bumped to Third for
    /// this second scripted step or the conversation misfires and the game softlocks. Deliberately not part of
    /// <see cref="ClassSpoofTriggerRnvs"/>, since unlike that dictionary this RNV should never *start* a spoof by
    /// itself - only continue one already in progress from the DeskBot.</summary>
    private static readonly RoomNodeView BellBotIntroRnv = new(2, 4, 2);

    /// <summary>Every RNV that can trigger or sustain a class-upgrade spoof, mapped to the PassengerClass value it
    /// should report there (raw values, not just the <see cref="PassengerClass"/> enum's: 4 = no class, ...,
    /// 1 = First, 0 = First with Bridge access). Covers the DeskBot itself; the SGT Class Stateroom TV [27,4,1], where
    /// the vanilla bed/TV puzzle grants the Magazine but only pays out while PassengerClass is still Third; the
    /// [22,1,2] corridor spot bracketing the transition into that room, which plays a voice line reminding the
    /// player to check the TV - included, as a test, so the spoof stays active across that transition (see
    /// <see cref="TryRevertClassUpgradeSpoof"/>) on the theory the line is itself gated on PassengerClass; and the
    /// doorway RNVs just outside each bottom-floor SGT room.</summary>
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

    /// <summary>Several vanilla scripts (the DeskBot upgrade interaction, the SGT TV magazine puzzle, and
    /// apparently a voice line on the way into that room) only run their class-upgrade payoff while the player is
    /// still below that tier - if AP items already pushed PassengerClass past it (see SyncPassengerClassFromItems),
    /// the natural interaction silently no-ops instead: it takes the item but never calls
    /// setPassengerClass()/petReassignRoom(), stranding both the location check and the room assignment
    /// petReassignRoom() performs (needed for progression).
    /// Worked around by temporarily lying about PassengerClass while at one of <see cref="ClassSpoofTriggerRnvs"/>,
    /// so the natural interaction still runs - ClassUpgradeHook still blocks the resulting setPassengerClass() from
    /// taking effect (the real class stays item-gated), but the uninstrumented petReassignRoom() call alongside it
    /// goes through for real.
    /// The DeskBot case has a second, unrelated failure mode this also covers: if a progressive class upgrade
    /// lands before the player's very first DeskBot visit, PassengerClass is already past None even though no
    /// room has ever been assigned - the vanilla None-gated "you're now Third Class" conversation and its room
    /// assignment never run, leaving the player roomless. Detected via <see cref="GameActions.HasAnyAssignedRoom"/>
    /// and spoofed down to None instead, ahead of the Third-class spoof below.</summary>
    private void TryApplyClassUpgradeSpoof(RoomNodeView rnv, long gameManager)
    {
        if (rnv == BellBotIntroRnv)
        {
            // Only continues an already-active spoof (see BellBotIntroRnv's own doc) - never starts one.
            if (_classUpgradeSpoofOriginalClass is null)
                return;

            int? reportedClass = GameState.ReadPassengerClass(_mem, gameManager);
            if (reportedClass is not null && reportedClass.Value != (int)PassengerClass.Third
                && GameActions.SetPassengerClass(_mem, gameManager, (int)PassengerClass.Third))
            {
                ShowActionResult(true, $"Class upgrade spoof: reporting {PassengerClassNames.GetName((int)PassengerClass.Third)} for Bellbot intro (really {PassengerClassNames.GetName(_classUpgradeSpoofOriginalClass.Value)})");
            }
            return;
        }

        if (!ClassSpoofTriggerRnvs.TryGetValue(rnv, out int configuredSpoofClass))
            return;
        if (_currentProject is not { } project || _currentInventoryRoom is null)
            return;
        if (_classUpgradeSpoofOriginalClass is not null)
            return;
        if (!LocationChecks.TryGetClassUpgradeLocationName((int)PassengerClass.Second, out string secondLocationName))
            return;
        if (!LocationChecks.TryGetClassUpgradeLocationName((int)PassengerClass.First, out string firstLocationName))
            return;

        int? currentClass = GameState.ReadPassengerClass(_mem, gameManager);
        if (currentClass is null)
            return;

        int? spoofToClass = null;

        if (rnv == DeskBotRnv
            && currentClass.Value != (int)PassengerClass.None
            && !GameActions.HasAnyAssignedRoom(_mem, _currentInventoryRoom.Value))
        {
            // A progressive class upgrade landed before the player's very first DeskBot visit, so PassengerClass
            // is already past None even though no room has ever been assigned - the vanilla "you're now Third
            // Class, here's your room" interaction is itself gated on None, so without this it silently no-ops
            // and the player is left with no room at all. Spoof down to None so that initial conversation and
            // room assignment plays out for real before the item-driven class takes over.
            spoofToClass = (int)PassengerClass.None;
        }
        else if (currentClass.Value < (int)PassengerClass.Third && !_apConnection.IsLocationChecked(secondLocationName))
        {
            // Only the DeskBot hand-off needs the Magazine already in hand
            bool magazineRequired = rnv == DeskBotRnv;
            bool magazineInInventory = !magazineRequired || GameState.FindAllCarryItems(_mem, project).Any(item =>
                string.Equals(item.Name, "Magazine", StringComparison.OrdinalIgnoreCase)
                && item.ParentAddress == _currentInventoryRoom.Value);
            if (magazineInInventory)
                spoofToClass = configuredSpoofClass;
        }
        else if (rnv == DeskBotRnv
            && currentClass.Value == (int)PassengerClass.First
            && _apConnection.IsLocationChecked(secondLocationName)
            && !_apConnection.IsLocationChecked(firstLocationName))
        {
            spoofToClass = (int)PassengerClass.Second;
        }

        if (spoofToClass is null)
            return;

        if (GameActions.SetPassengerClass(_mem, gameManager, spoofToClass.Value))
        {
            _classUpgradeSpoofOriginalClass = currentClass.Value;
            ShowActionResult(true, $"Class upgrade spoof: reporting {PassengerClassNames.GetName(spoofToClass.Value)} (really {PassengerClassNames.GetName(currentClass.Value)})");
        }
    }

    /// <summary>Restores the true PassengerClass value once the player moves to an RNV that isn't one of
    /// <see cref="ClassSpoofTriggerRnvs"/> (nor <see cref="BellBotIntroRnv"/>), undoing
    /// <see cref="TryApplyClassUpgradeSpoof"/>. Checking the destination (rather than the RNV being left) keeps the
    /// spoof alive across a move between two trigger RNVs - e.g. [22,1,2] into [27,1,2] - so a check gated on
    /// PassengerClass during the transition itself still sees the spoofed value.
    /// Unlike the initial spoof (a silent write - redrawing the PET then would flash the target class's colors and
    /// give the trick away), this redraws the PET so its display catches up to whatever the visit actually left
    /// the real class as.</summary>
    private void TryRevertClassUpgradeSpoof(RoomNodeView newRnv, long gameManager)
    {
        if (_classUpgradeSpoofOriginalClass is not { } originalClass)
            return;
        if (ClassSpoofTriggerRnvs.ContainsKey(newRnv) || newRnv == BellBotIntroRnv)
            return;

        if (_currentInventoryRoom is { } petControlAddr)
            GameActions.SetPassengerClassFull(_mem, gameManager, petControlAddr, originalClass);
        else
            GameActions.SetPassengerClass(_mem, gameManager, originalClass);

        _classUpgradeSpoofOriginalClass = null;
    }

    /// <summary>The two RNVs inside the player's own randomly-assigned SGT Class Stateroom where the bed/TV
    /// puzzle actually plays out - distinct from the wider <see cref="ClassSpoofTriggerRnvs"/> set (which also
    /// covers unrelated doorway/voice-line RNVs outside other passengers' rooms).</summary>
    private static readonly RoomNodeView[] SgtGlyphSpoofTriggerRnvs =
    {
        new(27, 1, 2), // SGT Stateroom
        new(27, 4, 1), // SGT TV
    };

    private long? _sgtGlyphAddr;
    private int? _sgtGlyphOriginalMode;

    /// <summary>Works around CPetRooms::reassignRoom() flagging the player's original SGT Class Stateroom glyph
    /// RGM_PREV_ASSIGNED_ROOM once a real 2nd/1st class room gets assigned out of order via AP items (see
    /// GameOffsets.cs's CPetRooms/CPetRoomsGlyph notes) - which otherwise makes the SGT bed/TV puzzle impossible
    /// to complete, since the vanilla interaction depends on that room still reading as currently assigned.
    /// Temporarily re-flags it RGM_ASSIGNED_ROOM while the player is back in one of <see cref="SgtGlyphSpoofTriggerRnvs"/>,
    /// restoring the original mode on <see cref="TryRevertSgtGlyphSpoof"/> - narrowly scoped to avoid two glyphs
    /// simultaneously reading as "assigned" anywhere else in the game (e.g. elevator routing, mail delivery).
    /// Identifies the SGT room's glyph by its own encoded class + RGM_PREV_ASSIGNED_ROOM mode (see
    /// GameActions.FindPrevAssignedGlyphForClass) rather than a pre-captured roomFlags value - an earlier version
    /// captured that opportunistically on room entry, which proved unreliable (could catch a stale/transient
    /// roomFlags reading mid room-transition, before it and the RNV settled together).</summary>
    private void TryApplySgtGlyphSpoof(RoomNodeView rnv)
    {
        if (_sgtGlyphAddr is not null)
            return; // already spoofed
        if (Array.IndexOf(SgtGlyphSpoofTriggerRnvs, rnv) < 0)
            return;
        if (_currentInventoryRoom is not { } petControlAddr)
            return;

        long? glyphAddr = GameActions.FindPrevAssignedGlyphForClass(_mem, petControlAddr, (int)PassengerClass.Third);
        if (glyphAddr is null)
            return; // no prev-assigned Third-class glyph - either never upgraded, or nothing to spoof

        int? currentMode = GameActions.ReadGlyphMode(_mem, glyphAddr.Value);
        if (currentMode is null || currentMode == GameOffsets.RgmAssignedRoom)
            return; // unreadable, or already the real assignment - nothing to spoof

        if (GameActions.WriteGlyphMode(_mem, glyphAddr.Value, GameOffsets.RgmAssignedRoom))
        {
            _sgtGlyphAddr = glyphAddr.Value;
            _sgtGlyphOriginalMode = currentMode.Value;
            ShowActionResult(true, "SGT Stateroom glyph spoof: reporting assigned (really prev-assigned)");
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

        GameActions.WriteGlyphMode(_mem, glyphAddr, originalMode);

        _sgtGlyphAddr = null;
        _sgtGlyphOriginalMode = null;
    }

    private const string TableAccessItemName = "Restaurant Table Reservation";

    /// <summary>Grants access to the Maitre'D's table (the effect MaitreDHook blocks) once the "Table Access"
    /// AP item is received, regardless of whether the MaitreD was ever defeated naturally.</summary>
    private void SyncTableAccessFromItems()
    {
        if (_currentProject is not { } project)
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
        ShowActionResult(ok, "Table Access granted -> Maitre'D's table unlocked");
    }

    /// <summary>Sends the AP location check for a room's "Arrive for the First Time" location.</summary>
    private void TrySendRoomVisitCheck(string roomName)
    {
        if (!LocationChecks.TryGetLocationName(roomName, out string locationName))
            return;

        if (!_sentRoomVisitChecks.Add(roomName))
            return;

        bool handedOff = _apConnection.SendLocationCheck(locationName);
        ShowActionResult(handedOff, handedOff
            ? $"Location check: {roomName} -> {locationName}"
            : $"Location check queued (offline): {roomName} -> {locationName}");
        UpdatePendingChecksLabel();
    }

    /// <summary>Sends the AP location check for visiting an exact (Room, Node, View) point of interest.</summary>
    private void TrySendPointOfInterestCheck(RoomNodeView rnv)
    {
        if (!LocationChecks.TryGetPointOfInterestLocationName(rnv, out string locationName))
            return;

        if (!_sentPointOfInterestChecks.Add(rnv))
            return;

        bool handedOff = _apConnection.SendLocationCheck(locationName);
        ShowActionResult(handedOff, handedOff
            ? $"Location check: Room {rnv.Room} Node {rnv.Node} View {rnv.View} -> {locationName}"
            : $"Location check queued (offline): Room {rnv.Room} Node {rnv.Node} View {rnv.View} -> {locationName}");
        UpdatePendingChecksLabel();
    }

    /// <summary>Displays a reminder message if the player arrives at a point of interest without the AP item it requires.</summary>
    private void TryShowItemNeededReminder(RoomNodeView rnv)
    {
        if (!RnvItemReminders.TryGetReminder(rnv, out string apItemName, out string message))
            return;
        if (_currentInventoryRoom is null)
            return;

        string[] receivedItems = _apConnection.GetReceivedItemNames();
        if (receivedItems.Contains(apItemName, StringComparer.OrdinalIgnoreCase))
            return;

        GameActions.DisplayMessageSmart(_mem, _currentInventoryRoom.Value, message);
    }

    private void UpdateInventory(long gameManager)
    {
        long? project = GameState.ResolveProject(_mem, gameManager);
        _currentProject = project;
        SetAddressRow("_project", project);

        if (project is null)
            return;

        // FindInventoryRoom is a shallow, budget-capped tree walk (see its own doc comment) that has been
        // observed to transiently fail to find the PET control while standing in certain rooms (e.g. Bottom of
        // the Well), even though the address itself is still perfectly valid. Only ever replace the cached
        // address with a fresh non-null result - never let a transient failure null it back out - so a momentary
        // miss doesn't strand every address-dependent system (ReconcileTrackedItems bails outright when this is
        // null) until the player happens to leave the room. OnTick's own project-change handling is still what's
        // responsible for actually clearing this on a genuine save/tree reload.
        long? inventoryRoom = GameState.FindInventoryRoom(_mem, project.Value);
        if (inventoryRoom is not null)
            _currentInventoryRoom = inventoryRoom;
        SetAddressRow("Player Inventory (CPetControl)", _currentInventoryRoom);

        SetAddressRow("Conversations (CPetConversations)",
            _currentInventoryRoom is not null ? GameState.ResolveConversationsAddr(_currentInventoryRoom.Value) : null);
    }

    /// <summary>Runs the full-tree item-state reconciliation pass for every tracked item.</summary>
    private void ReconcileTrackedItems(long gameManager)
    {
        if (_currentProject is not { } project)
            return;
        if (_currentInventoryRoom is null || _currentMailManRoom is null)
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
                // CarryParrot itself has no location check of its own (see LocationChecks.cs) - what this tracks
                // instead is the Feather's own check, fired off CarryParrot's escape from inventory rather than
                // off the real Feathers object's movement (see FeathersItem's doc comment for why). Per the
                // game's own mechanics, while the player is carrying the parrot exactly one of three things
                // happens: they mail it away (no feather left behind), they leave the room, or a timer runs out
                // (the latter two both leave a feather behind) - so once the parrot is seen leaving inventory,
                // landing anywhere other than the mail is proof a feather was left behind.
                //
                // PulledFrom (otherwise unused for CarryParrot) doubles as the "currently being watched for a
                // departure" flag: set the moment it's first seen in inventory, cleared without firing if it goes
                // to mail (so a later re-pickup-then-real-escape after mailing it back to themselves is still
                // caught), and left permanently resolved (CheckFired) once a genuine escape fires the check.
                ItemPersistedState carryParrotPersisted = GameActions.ReadItemPersistedState(_mem, item.Address);
                if (!carryParrotPersisted.CheckFired)
                {
                    bool parrotInInventory = item.ParentAddress == _currentInventoryRoom.Value;
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
                        bool wentToMail = item.ParentAddress == _currentMailManRoom.Value;
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
                            DoRefreshAllItems();
                        }
                    }
                    continue;
                }

                bool serverGrantedInInventory = item.ParentAddress == _currentInventoryRoom.Value;
                bool serverGrantedInMail = item.ParentAddress == _currentMailManRoom.Value;
                if (serverGrantedInInventory || serverGrantedInMail || serverGrantedInHidden)
                    continue;

                bool serverGrantedMoved = GameActions.MoveItemToHiddenRoomFull(_mem, item.Address, _currentInventoryRoom.Value, gameManager);
                if (serverGrantedMoved)
                {
                    // petMoveToHiddenRoom() only relocates the real item - it never clears the Fuse Box socket's
                    // own CShipSetting state, so without this the socket keeps showing the fuse as "installed"
                    // (a ghost that's still clickable/draggable, with a pickup-style cursor). See
                    // ship_setting.cpp's MouseDragStartMsg for the game's own equivalent reset.
                    if (GameState.FindFuseBoxView(_mem, project) is { } fuseBoxView)
                    {
                        foreach (long shipSetting in GameState.FindShipSettingsInstalledWith(_mem, project, item.Name))
                            GameActions.ResetShipSetting(_mem, shipSetting, fuseBoxView);
                    }

                    DoRefreshAllItems();
                }
                continue;
            }

            ItemPersistedState persisted = GameActions.ReadItemPersistedState(_mem, item.Address);

            if (persisted.Stage == ItemStage.Inventory && persisted.CheckFired)
                continue;

            bool granted = LocationChecks.TryGetApItemName(item.Name, out string apItemName)
                && receivedItems.Contains(apItemName, StringComparer.OrdinalIgnoreCase);

            bool inInventory = item.ParentAddress == _currentInventoryRoom.Value;
            bool inHiddenRoom = GameActions.HiddenRoomAddress is { } hiddenAddr && item.ParentAddress == hiddenAddr;
            bool inMail = item.ParentAddress == _currentMailManRoom.Value;

            if (inInventory)
            {
                if (ItemTracking.RequiresCanTakeRestoreToggle(item.Name))
                    GameActions.SetItemCanTake(_mem, item.Address, true);

                if (persisted.Stage == ItemStage.Mail)
                {
                    // Every full-state-machine item except the Magazine only ever reaches Stage.Mail via our own
                    // DeliverToMail (proactive AP-grant delivery, or a natural-pickup-then-granted handoff), so its
                    // pickup check either doesn't apply here or already fired - preserve that CheckFired verbatim.
                    // A player can also legitimately re-mail an already-found item to themselves via the game's own
                    // SuccUBus stations, which must NOT re-trigger its check - so this can't be widened to "any
                    // item currently in the mail with a real (non-tool-placed) destination".
                    //
                    // The Magazine is the sole exception: it has no in-world home (see ItemHomeLocations.cs) and can
                    // reach Stage.Mail either via this app's own proactive delivery (an AP grant landing before the
                    // player found it naturally - no check here, same as every other item's proactive-delivery
                    // case) or via the SGT TV puzzle's own script placing it there directly (a genuine natural
                    // delivery - this is that pickup). ItemPersistedState.ToolDelivered records which one it was
                    // at the moment it entered Mail, since the tool-placed sentinel (see GameActions.
                    // MarkItemAsToolPlaced) that used to live in _destRoomFlags didn't survive to this point - live
                    // testing showed the game's own mail-retrieval processing overwrites _destRoomFlags with a real
                    // value before this code gets to read it back. The sentinel now lives in _unused1 instead,
                    // which the engine never touches, but ToolDelivered is kept as the source of truth here rather
                    // than re-deriving from that untested assumption.
                    bool fireCheck = !persisted.CheckFired
                        && string.Equals(item.Name, "Magazine", StringComparison.OrdinalIgnoreCase)
                        && !persisted.ToolDelivered;

                    if (fireCheck)
                        SendItemPickupCheck(item.Name);

                    // A genuine natural pickup (fireCheck) is subject to the same ungranted-item hide rule as
                    // every other item's natural pickup below - without this, picking up the Magazine before its
                    // AP item is granted would leave it sitting in inventory instead of being pulled pending grant.
                    if (fireCheck && !granted && !ItemTracking.IsOneDirectionalItem(item.Name) && _chkTakeUngrantedItems.Checked)
                    {
                        bool moved = GameActions.MoveItemToHiddenRoomFull(_mem, item.Address, _currentInventoryRoom.Value, gameManager);
                        if (moved)
                        {
                            GameActions.WriteItemPersistedState(_mem, item.Address, new ItemPersistedState(ItemStage.Hidden, true, ItemPulledFrom.None));
                            DoRefreshAllItems();
                        }
                        ShowActionResult(moved, moved
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

                if (granted || ItemTracking.IsOneDirectionalItem(item.Name) || !_chkTakeUngrantedItems.Checked)
                {
                    GameActions.WriteItemPersistedState(_mem, item.Address, new ItemPersistedState(ItemStage.Inventory, true, ItemPulledFrom.None));
                }
                else
                {
                    bool moved = GameActions.MoveItemToHiddenRoomFull(_mem, item.Address, _currentInventoryRoom.Value, gameManager);
                    if (moved)
                    {
                        GameActions.WriteItemPersistedState(_mem, item.Address, new ItemPersistedState(ItemStage.Hidden, true, ItemPulledFrom.None));
                        DoRefreshAllItems();
                    }
                    ShowActionResult(moved, moved
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
                        DoRefreshAllItems();
                    }
                }
                continue;
            }

            if (inMail)
            {
                // Only the Magazine has no in-world home and gets placed into mail natively by the game itself
                // (the SGT TV puzzle) rather than via our own DeliverToMail, so it's the only item whose
                // persisted-state field can show up here holding leftover memory from a freshly game-constructed
                // object rather than a real prior write - normalize it so it stages cleanly for the pickup check
                // above. Every other item that legitimately reaches Stage.Mail got there through our own bookkeeping
                // already, and one sitting in mail with some OTHER stage just means the player re-mailed an
                // already-tracked item to themselves via the game's own SuccUBus stations - leave that alone.
                if (persisted.Stage != ItemStage.Mail && string.Equals(item.Name, "Magazine", StringComparison.OrdinalIgnoreCase))
                {
                    GameActions.WriteItemPersistedState(_mem, item.Address,
                        new ItemPersistedState(ItemStage.Mail, false, ItemPulledFrom.None));
                }
                continue;
            }

            // One-directional items have no home parent to proactively deliver from - they only enter play via
            // their own in-game trigger, and forcibly reparenting them into mail before that trigger fires would
            // break the mechanism that produces them. Leave them be; their check still fires normally once they
            // naturally land in inventory (see the inInventory branch).
            if (persisted.Stage == ItemStage.None && granted && !ItemTracking.IsOneDirectionalItem(item.Name))
            {
                bool ok = DeliverToMail(item, gameManager);
                if (ok)
                {
                    GameActions.WriteItemPersistedState(_mem, item.Address, new ItemPersistedState(ItemStage.Mail, false, ItemPulledFrom.None, ToolDelivered: true));
                    anyMailChange = true;
                    DoRefreshAllItems();
                }
            }
        }

        if (anyMailChange)
            _lastMailItems = null;
    }

    /// <summary>Keeps GetLiftEye2GateHook's remote "may take the Eye" gate byte in sync with whether the LiftBot
    /// Head is both AP-granted and physically sitting in the player's inventory - see the hook's own doc comment
    /// for why the block has to happen inside CGetLiftEye2::MouseDragStartMsg itself rather than via this app's
    /// usual post-hoc item reconciliation (by the time the pickup completes, the elevator's socket is already
    /// empty regardless of what this app does with the resulting item afterward).</summary>
    private void SyncLiftEyeGate(List<CarryItemLocation> items, string[] receivedItems)
    {
        if (!GetLiftEye2GateHook.IsInstalled || _currentInventoryRoom is null)
            return;

        bool headGranted = LocationChecks.TryGetApItemName("BrokenLiftbotHead", out string headApName)
            && receivedItems.Contains(headApName, StringComparer.OrdinalIgnoreCase);
        bool headInInventory = headGranted
            && FindByName(items, "BrokenLiftbotHead") is { } head
            && head.ParentAddress == _currentInventoryRoom.Value;

        GetLiftEye2GateHook.SetGateAllowed(_mem, headInInventory);
    }

    /// <summary>Feathers stays out of the generic tracked-item pipeline entirely (see FeathersItem's doc comment) -
    /// its pickup check fires off CarryParrot's own escape from inventory, independent of AP-grant state, since
    /// finding it is what matters for the check, not owning it. But every other aspect of a normal full-state-
    /// machine item's AP-grant handling still applies to the real Feathers object: a grant proactively delivers it
    /// to mail (same as every other item's Stage.None-and-granted case), and a natural pickup while ungranted hides
    /// it pending a later grant (same as every other item's ungranted-natural-pickup case). Reimplemented by hand,
    /// rather than by just letting Feathers into the generic loop, only so its own check doesn't get evaluated
    /// there too and double-fire alongside the CarryParrot-driven one.</summary>
    private void TryHideOrDeliverFeather(List<CarryItemLocation> items, string[] receivedItems, long gameManager)
    {
        if (_currentInventoryRoom is null)
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
                DoRefreshAllItems();
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
                DoRefreshAllItems();
            }
            return;
        }

        if (feather.ParentAddress != _currentInventoryRoom.Value)
            return;

        bool moved = GameActions.MoveItemToHiddenRoomFull(_mem, feather.Address, _currentInventoryRoom.Value, gameManager);
        if (moved)
        {
            GameActions.WriteItemPersistedState(_mem, feather.Address, new ItemPersistedState(ItemStage.Hidden, true, ItemPulledFrom.None));
            DoRefreshAllItems();
        }
        ShowActionResult(moved, moved
            ? "Feathers picked up - not yet granted, hidden pending AP grant"
            : "Feathers picked up - not yet granted, but failed to hide it");
    }

    /// <summary>Reacts to GetLiftEye2GateHook firing: tells the player what they're missing. A plain RNV arrival
    /// reminder can't do this job here since the broken elevator's (Room, Node, View) is shared by every lift in
    /// the game, not just this one - this only fires from the exact code path that just blocked a real attempt.</summary>
    private void HandleBlockedLiftEyeDrag()
    {
        if (_currentInventoryRoom is null)
            return;

        GameActions.DisplayMessageSmart(_mem, _currentInventoryRoom.Value,
            "AP: You can't take that. Maybe if you had something to replace it with.");
    }

    /// <summary>True when a fuse is currently installed in a Fuse Box socket. The hidden room is the same one
    /// CGameObject::petMoveToHiddenRoom() parks a fuse's real item under when the player installs it - not just
    /// where AP stashes items awaiting mail delivery - so both the server-granted and natural-pickup tracking
    /// branches need this check before treating "item is in the hidden room" as "still pending delivery" and
    /// redelivering (and thereby yanking it back out of the socket).</summary>
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
        _pendingDirtyReassertTick = _tickCount + DirtyReassertDelayTicks;

    /// <summary>Restores eligible items to their home parent, or self-heals a stray item, on arrival at the RNV that owns them.</summary>
    private void TryRestoreItemsAtHomeRnv(RoomNodeView rnv, long gameManager)
    {
        if (!ItemHomeLocations.TryGetItemsForRnv(rnv, out string[] itemNames))
            return;

        // The player is back looking at these items' home RNV - cancel any delayed unrestore-check still queued
        // for them from a prior quick leave (see TryUnrestoreItemsLeavingRnv/ProcessPendingUnrestoreChecks), so a
        // quick leave-and-return doesn't unrestore an item still on screen out from under the player.
        CancelPendingUnrestoreChecks(itemNames);

        if (_currentProject is not { } project || _currentInventoryRoom is null)
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

            // For the Magazine specifically, Stage.Mail can also hold a genuine natural delivery already in
            // progress (the SGT TV puzzle's own script placed it there for the player to retrieve for real - see
            // ItemPersistedState.ToolDelivered) - pulling that back out to "restore" it here would yank it out of
            // its own natural mail-delivery flow instead of leaving the app done with it. Every other item only
            // ever reaches Stage.Mail via this app's own proactive delivery, so ToolDelivered is always true for
            // them and this check is a no-op.
            bool eligibleFromMail = persisted.Stage == ItemStage.Mail && !persisted.CheckFired && persisted.ToolDelivered;

            if (eligibleFromInventory || eligibleFromMail)
            {
                long? homeParent = GameState.ResolveHomeParent(_mem, project, name, out string failureReason);
                if (homeParent is null)
                {
                    ShowActionResult(false, $"{name} is eligible for restoration but its home parent couldn't be resolved ({failureReason})");
                    continue;
                }

                ItemPulledFrom pulledFrom = eligibleFromMail ? ItemPulledFrom.Mail : ItemPulledFrom.Inventory;
                bool moved = GameActions.MoveItemSmart(_mem, item.Address, homeParent.Value, _currentInventoryRoom, gameManager);
                if (moved)
                {
                    GameActions.WriteItemPersistedState(_mem, item.Address,
                        new ItemPersistedState(ItemStage.Restored, false, pulledFrom));
                    GameActions.SetItemVisible(_mem, item.Address, false);

                    if (!ItemTracking.SkipsFirstChildReorderOnRestore(name))
                        GameActions.MoveToFirstChild(_mem, item.Address, homeParent.Value);

                    if (ItemTracking.RequiresCanTakeRestoreToggle(name))
                        GameActions.SetItemCanTake(_mem, item.Address, false);

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
                    DoRefreshAllItems();
                }
                ShowActionResult(moved, moved
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
                ShowActionResult(moved, $"{name} self-healed back to its home parent");
            }
        }
    }

    /// <summary>Queues a delayed re-check for any item still sitting Restored-but-unpicked at the RNV just being
    /// left, instead of reverting it immediately - the RNV can change (e.g. the moment an item attaches to the
    /// cursor, well before it's actually reparented into inventory by a drag-and-drop) before the item's own
    /// ParentAddress reflects a real pickup, so deciding synchronously here can't reliably tell a genuine pickup
    /// apart from actually leaving the item behind. See ProcessPendingUnrestoreChecks for the actual revert.</summary>
    private void TryUnrestoreItemsLeavingRnv(RoomNodeView rnv, long gameManager)
    {
        if (!ItemHomeLocations.TryGetItemsForRnv(rnv, out string[] itemNames))
            return;
        if (_currentProject is not { } project)
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

            _pendingUnrestoreChecks.Add((name, item.Address, _tickCount + UnrestoreCheckDelayTicks));
        }
    }

    /// <summary>Drops any queued delayed unrestore-check for the given items - called when the player is back at
    /// (one of) their home RNVs, so a quick leave-and-return doesn't unrestore an item still on screen.</summary>
    private void CancelPendingUnrestoreChecks(string[] itemNames)
    {
        if (_pendingUnrestoreChecks.Count == 0)
            return;

        _pendingUnrestoreChecks.RemoveAll(p => itemNames.Contains(p.ItemName, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Runs the delayed revert decision queued by TryUnrestoreItemsLeavingRnv, once enough ticks have
    /// passed for ReconcileTrackedItems to have run at least once. By then, a genuine pickup has already been
    /// caught and flipped out of Restored - so anything still Restored here really was left behind.</summary>
    private void ProcessPendingUnrestoreChecks(long gameManager)
    {
        if (_pendingUnrestoreChecks.Count == 0)
            return;

        RoomNodeView? currentRnv = GameState.ReadRoomNodeView(_mem, gameManager);

        for (int i = _pendingUnrestoreChecks.Count - 1; i >= 0; i--)
        {
            (string name, long itemAddress, int dueTick) = _pendingUnrestoreChecks[i];
            if (_tickCount < dueTick)
                continue;

            ItemPersistedState persisted = GameActions.ReadItemPersistedState(_mem, itemAddress);
            if (persisted.Stage != ItemStage.Restored)
            {
                _pendingUnrestoreChecks.RemoveAt(i); // already picked up and reconciled - nothing to revert
                continue;
            }

            // Titania's Eye (Light)'s bellbot hand-off keeps the player sitting at a fixed cutscene RNV for the
            // whole dialogue+hand-off, well before the real CEye object reparents into inventory - reverting here
            // would pull a genuine in-progress hand-off out from under the still-playing cutscene (confirmed
            // live). Keep deferring the decision for as long as the player is still there; the entry only reaches
            // a real revert-or-not decision once they've actually left it.
            if (string.Equals(name, "Eye1", StringComparison.OrdinalIgnoreCase) && currentRnv == Eye1HandoffCutsceneRnv)
            {
                _pendingUnrestoreChecks[i] = (name, itemAddress, _tickCount + UnrestoreCheckDelayTicks);
                continue;
            }

            _pendingUnrestoreChecks.RemoveAt(i);

            long? parentAddress = _mem.ReadInt64(itemAddress + GameOffsets.Parent);
            string? parentName = parentAddress is long p ? GameState.TryReadName(_mem, p) : null;
            var item = new CarryItemLocation(name, itemAddress, parentAddress, parentName);

            if (TryCompleteEar1IfBowlUnlocked(item, gameManager))
            {
                ShowActionResult(true, $"{name} left un-picked-up after the bowl unlocked - completed the pickup instead of reverting it");
                continue;
            }

            if (IsEye1MidBellbotHandoff(item))
            {
                ShowActionResult(true, $"{name} handed off by the bellbot - leaving it for the natural pickup to land instead of reverting");
                continue;
            }

            bool moved = RevertRestoration(item, persisted, gameManager);
            ShowActionResult(moved, moved
                ? $"{name} left un-picked-up - returned to where it was before restoration"
                : $"{name} failed to revert from restoration");
        }
    }

    /// <summary>Ear1 (Pistachio Bowl)'s pickup puzzle can't be safely left mid-sequence and re-captured: once the
    /// nut-rustle/parrot-eat sequence resolves, the bowl unlocks and renders with its drag graphic, but if the
    /// player leaves the view without dragging it into inventory, re-entering resets the whole sequence back to
    /// its start (see ParrotNutBowlActorStateOffset's doc comment) - so there's no stable "just unlocked, not yet
    /// grabbed" state to restore back to on a later visit. Rather than reverting Ear1 like a normal left-behind
    /// restoration in that case, force it straight into inventory as if the drag had completed;
    /// ReconcileTrackedItems's own Stage==Restored + inInventory branch then grants the pickup check exactly like
    /// a genuine natural pickup would.</summary>
    private bool TryCompleteEar1IfBowlUnlocked(CarryItemLocation item, long gameManager)
    {
        if (!string.Equals(item.Name, "Ear1", StringComparison.OrdinalIgnoreCase))
            return false;
        if (item.ParentAddress is not { } parentAddr || _currentInventoryRoom is not { } inventoryRoom)
            return false;

        long? actorAddr = GameState.FindDescendant(_mem, parentAddr, "ParrotNutBowlActor", "CParrotNutBowlActor");
        if (actorAddr is null)
            return false;

        int? state = _mem.ReadInt32(actorAddr.Value + GameOffsets.ParrotNutBowlActorStateOffset);
        if (state != GameOffsets.ParrotNutBowlActorStateUnlocked)
            return false;

        return GameActions.MoveItemSmart(_mem, item.Address, inventoryRoom, inventoryRoom, gameManager);
    }

    /// <summary>Belt-and-suspenders backstop for once the player has actually left Eye1HandoffCutsceneRnv:
    /// confirmed live that CLight's _eyePresent field on the fixture ("6WTL", under Eye1's home View) only flips
    /// false once the bellbot's dialogue+hand-off cutscene actually finishes - not when it starts - so it can't be
    /// used to detect "hand-off in progress" while still sitting at that RNV (an earlier version of this fix
    /// tried that and still lost the race; ProcessPendingUnrestoreChecks's own RNV-based deferral handles that
    /// part instead). What this catches is the case where the object hasn't reparented into inventory yet right
    /// as they step away from that RNV: if _eyePresent has already flipped, the hand-off is real regardless, so
    /// skip the revert and let ReconcileTrackedItems's own Stage==Restored + inInventory branch fire the pickup
    /// check once the object's parent actually updates - forcing a move of our own is what caused the original
    /// race in the first place.</summary>
    private bool IsEye1MidBellbotHandoff(CarryItemLocation item)
    {
        if (!string.Equals(item.Name, "Eye1", StringComparison.OrdinalIgnoreCase))
            return false;
        if (_currentProject is not { } project)
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
        if (_currentInventoryRoom is null)
            return false;
        bool moved = GameActions.MoveItemSmart(_mem, item.Address, _currentInventoryRoom.Value, _currentInventoryRoom, gameManager);
        if (moved)
            GameActions.WriteItemPersistedState(_mem, item.Address, new ItemPersistedState(ItemStage.Inventory, false, ItemPulledFrom.None));
        return moved;
    }

    private bool RevertToHidden(CarryItemLocation item, ItemPersistedState persisted, long gameManager)
    {
        if (_currentInventoryRoom is null)
            return false;
        bool moved = GameActions.MoveItemToHiddenRoomFull(_mem, item.Address, _currentInventoryRoom.Value, gameManager);
        if (moved)
            GameActions.WriteItemPersistedState(_mem, item.Address, new ItemPersistedState(ItemStage.Hidden, persisted.CheckFired, ItemPulledFrom.None));
        return moved;
    }

    private bool RevertToMail(CarryItemLocation item, ItemPersistedState persisted, long gameManager)
    {
        if (_currentMailManRoom is null || _currentInventoryRoom is null)
            return false;
        bool moved = GameActions.MoveItemSmart(_mem, item.Address, _currentMailManRoom.Value, _currentInventoryRoom, gameManager);
        if (moved)
        {
            // Only reachable via PulledFrom.Mail, which TryRestoreItemsAtHomeRnv only ever records when the item
            // was ToolDelivered to begin with (see eligibleFromMail there) - restamp it so a later retrieval still
            // correctly waits instead of firing early.
            GameActions.WriteItemPersistedState(_mem, item.Address,
                new ItemPersistedState(ItemStage.Mail, persisted.CheckFired, ItemPulledFrom.None, ToolDelivered: true));
        }
        return moved;
    }

    /// <summary>Sends the AP location check for physically finding a tracked item, if one is mapped.</summary>
    private void SendItemPickupCheck(string itemName)
    {
        if (!LocationChecks.TryGetItemPickupLocationName(itemName, out string locationName))
            return;

        bool handedOff = _apConnection.SendLocationCheck(locationName);
        ShowActionResult(handedOff, handedOff
            ? $"Location check: {itemName} -> {locationName}"
            : $"Location check queued (offline): {itemName} -> {locationName}");
        UpdatePendingChecksLabel();
    }

    /// <summary>Delivers a tracked item to the player via the mail system.</summary>
    private bool DeliverToMail(CarryItemLocation item, long gameManager)
    {
        if (_currentInventoryRoom is null || _currentMailManRoom is null)
            return false;

        string destRoomName = _currentRoomName is not null && ChevronCodes.HasStation(_currentRoomName)
            ? _currentRoomName
            : "EmbLobby";
        uint? liveRoomFlags = GameState.ReadCurrentRoomFlags(_mem, _currentInventoryRoom.Value);
        ChevronCodes.TryGetCode(destRoomName, liveRoomFlags, out uint code);

        bool moved = GameActions.MoveItemSmart(_mem, item.Address, _currentMailManRoom.Value, _currentInventoryRoom, gameManager);
        bool ok = moved && GameActions.SetItemMailDestination(_mem, item.Address, code);
        if (ok)
        {
            GameActions.MarkItemAsToolPlaced(_mem, item.Address);
            if (ItemTracking.RequiresCanTakeOverride(item.Name))
                GameActions.SetItemCanTake(_mem, item.Address, true);
        }

        ShowActionResult(ok, ok
            ? $"Delivered AP grant for {item.Name} via mail, destination {destRoomName}"
            : $"Failed to deliver AP grant for {item.Name}");

        return ok;
    }
}
