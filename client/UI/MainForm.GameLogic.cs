namespace StarshipTitanicAp;

public sealed partial class MainForm
{
    /// <summary>Computes and caches whether it's safe to act on the currently attached save under the currently connected AP seed,
    /// re-verifying periodically once a verdict is reached.</summary>
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

        // Requires several consecutive mismatched reads before blocking.
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

    private const string TitaniaRepairedLocation = "Titania's Room - Repair Titania";

    /// <summary>Applies the class upgrade implied by AP items received so far, if any. With no class items yet,
    /// clamps a First/Second class (only reachable via a spoof that was saved or never reverted) back to Third.
    /// While a class spoof is active, updates the spoof's backed-up original class instead of the live value, so
    /// <see cref="TryRevertClassUpgradeSpoof"/> restores the new class rather than the stale one.</summary>
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

        int? currentClass = _classUpgradeSpoofOriginalClass ?? GameState.ReadPassengerClass(_mem, gameManager);
        if (currentClass is null)
            return;

        // Bridge access shares its raw value with None - never overwrite it once Titania has been repaired.
        if (currentClass.Value == SpecialPassengerClassValues.BridgeAccessClassValue
            && _apConnection.IsLocationChecked(TitaniaRepairedLocation))
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

        if (_classUpgradeSpoofOriginalClass is not null)
        {
            _classUpgradeSpoofOriginalClass = targetClass.Value;
            ShowActionResult(true, $"Class upgrade from items: {PassengerClassNames.GetName(targetClass.Value)} (applied once the class spoof ends)");
            return;
        }

        bool ok = GameActions.SetPassengerClassFull(_mem, gameManager, _currentInventoryRoom.Value, targetClass.Value);
        ShowActionResult(ok, $"Class upgrade from items: {PassengerClassNames.GetName(targetClass.Value)}");
    }

    /// <summary>Applies the stateroom assignment(s) implied by "Progressive Stateroom" AP items received so far,
    /// if any are still outstanding, via GameActions.AssignNextRoom. Assigns every outstanding tier in order:
    /// SGT/Third, then Second, then First.</summary>
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
    /// interactions run. Reports Third for a pending Magazine hand-off, Second for a pending 1st Class upgrade, and
    /// None at the DeskBot when no room has been assigned yet. The true value is saved in
    /// <see cref="_classUpgradeSpoofOriginalClass"/>.</summary>
    private void TryApplyClassUpgradeSpoof(RoomNodeView rnv, long gameManager)
    {
        if (rnv == BellBotIntroRnv)
        {
            // Only continues an already-active spoof.
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
        // Queued counts as checked, so a check made while offline still advances the spoof.
        bool secondClassPending = !_apConnection.IsLocationCheckedOrQueued(secondLocationName);
        // None before Titania's repair means the player hasn't checked in yet (after it, None is Bridge access).
        bool notCheckedIn = currentClass.Value == (int)PassengerClass.None
            && !_apConnection.IsLocationCheckedOrQueued(TitaniaRepairedLocation);
        bool magazineHandOffPending = rnv == DeskBotRnv && secondClassPending
            && GameState.FindAllCarryItems(_mem, project).Any(item =>
                string.Equals(item.Name, "Magazine", StringComparison.OrdinalIgnoreCase)
                && item.ParentAddress == _currentInventoryRoom.Value);

        if (magazineHandOffPending)
        {
            // DeskbotScript's 2ndClassUpgrade dialogue (140) pays out only at exactly Third.
            if (currentClass.Value != (int)PassengerClass.Third)
                spoofToClass = (int)PassengerClass.Third;
        }
        else if (rnv != DeskBotRnv && currentClass.Value < (int)PassengerClass.Third && secondClassPending)
        {
            spoofToClass = configuredSpoofClass;
        }
        else if (rnv == DeskBotRnv && !secondClassPending && !notCheckedIn && !_apConnection.IsLocationCheckedOrQueued(firstLocationName))
        {
            // DeskbotScript's 1st Class upgrade pays out only at exactly Second. Skipped until the player has checked
            // in, so the unspoofed check-in conversation can run - otherwise they can never leave the Embarkation Lobby.
            if (currentClass.Value != (int)PassengerClass.Second)
                spoofToClass = (int)PassengerClass.Second;
        }
        else if (rnv == DeskBotRnv
            && currentClass.Value != (int)PassengerClass.None
            && !GameActions.HasAnyAssignedRoom(_mem, _currentInventoryRoom.Value))
        {
            // No room assigned yet: report None so the initial DeskBot conversation and room assignment run.
            spoofToClass = (int)PassengerClass.None;
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
    /// <see cref="TryApplyClassUpgradeSpoof"/> and redrawing the PET.</summary>
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

        // Only a non-null result replaces the cached address; OnTick's project-change handling clears it.
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
                // Sends the Feathers check when CarryParrot leaves inventory for anywhere other than the mail.
                // PulledFrom == Inventory marks the parrot as currently being watched for a departure.
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
                    // Clears any Fuse Box socket's CShipSetting state for this fuse.
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
                    // Collected from mail. Only a Magazine that the game itself mailed (not ToolDelivered) counts as a
                    // natural pickup; every other item keeps its CheckFired as-is.
                    bool fireCheck = !persisted.CheckFired
                        && string.Equals(item.Name, "Magazine", StringComparison.OrdinalIgnoreCase)
                        && !persisted.ToolDelivered;

                    if (fireCheck)
                        SendItemPickupCheck(item.Name);

                    // Hides an ungranted natural pickup pending its AP grant.
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
                    DoRefreshAllItems();
                }
            }
        }

        if (anyMailChange)
            _lastMailItems = null;
    }

    /// <summary>Keeps GetLiftEye2GateHook's remote "may take the Eye" gate byte in sync with whether the LiftBot
    /// Head is both AP-granted and physically sitting in the player's inventory.</summary>
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

    /// <summary>AP-grant handling for the real Feathers object, which is outside the generic tracked-item loop:
    /// a grant delivers it to mail, and a natural pickup while ungranted hides it pending a later grant.
    /// Never sends the Feathers check.</summary>
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

    /// <summary>Reacts to GetLiftEye2GateHook firing: tells the player what they're missing.</summary>
    private void HandleBlockedLiftEyeDrag()
    {
        if (_currentInventoryRoom is null)
            return;

        GameActions.DisplayMessageSmart(_mem, _currentInventoryRoom.Value,
            "AP: You can't take that. Maybe if you had something to replace it with.");
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
        _pendingDirtyReassertTick = _tickCount + DirtyReassertDelayTicks;

    /// <summary>Restores eligible items to their home parent, or self-heals a stray item, on arrival at the RNV that owns them.</summary>
    private void TryRestoreItemsAtHomeRnv(RoomNodeView rnv, long gameManager)
    {
        if (!ItemHomeLocations.TryGetItemsForRnv(rnv, out string[] itemNames))
            return;

        // Cancels any delayed unrestore-check still queued for these items.
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

            // Only items this app mailed (ToolDelivered) are restored out of the mail.
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
    /// left. ProcessPendingUnrestoreChecks performs the actual revert.</summary>
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
            if (_tickCount < dueTick)
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

    /// <summary>If Ear1 (Pistachio Bowl) was left behind after its ParrotNutBowlActor reached the unlocked state, moves
    /// it into inventory instead of reverting it, so ReconcileTrackedItems's Restored + inInventory branch sends the
    /// pickup check.</summary>
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

    /// <summary>True when Eye1's light fixture ("6WTL", CLight, under Eye1's home View) reports _eyePresent == false,
    /// meaning the bellbot hand-off has completed and Eye1's revert should be skipped.</summary>
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
            // PulledFrom.Mail is only recorded for ToolDelivered items, so ToolDelivered is restamped.
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
