namespace StarshipTitanicAp;

public sealed partial class MainForm
{
    private void OnTick()
    {
        if (!_mem.IsAttached)
            return;

        _tickCount++;

        long? gameManager = GameState.ResolveGameManager(_mem);
        _currentGameManager = gameManager;

        if (gameManager is null)
        {
            _lblRoomNodeView.Text = "Room: -   Node: -   View: -   (menu / loading)";
            SetAddressRow("gameManager", null);
            return;
        }

        SetAddressRow("gameManager", gameManager.Value);
        SetAddressRow("PassengerClass field", gameManager.Value + GameOffsets.PassengerClass);

        if (_pendingDirtyReassertTick is { } dueTick && _tickCount >= dueTick)
        {
            _pendingDirtyReassertTick = null;
            GameActions.MarkAllDirty(_mem, gameManager.Value);
        }

        if (TextCommandHook.IsInstalled)
        {
            string? command = TextCommandHook.PollCommand(_mem);

            if (!_conversationsAddrShown && TextCommandHook.ConversationsAddr != 0)
            {
                _conversationsAddrShown = true;

                if (_currentInventoryRoom is not null)
                {
                    long computed = GameState.ResolveConversationsAddr(_currentInventoryRoom.Value);
                    if (computed != TextCommandHook.ConversationsAddr)
                    {
                        ShowActionResult(false,
                            $"WARNING: static _conversations offset (0x{computed:X}) disagrees with live hook capture (0x{TextCommandHook.ConversationsAddr:X}) - PetConversationsFieldOffset may need re-checking");
                    }
                }
            }

            if (command is not null)
            {
                if (string.Equals(command.Trim(), "force_seed", StringComparison.OrdinalIgnoreCase))
                {
                    ShowCapturedCommand($"{command} (handled locally - not sent to server)");
                    HandleForceSeedCommand();
                }
                else if (_apConnection.IsConnected)
                {
                    _apConnection.SendCommand(command);
                    ShowCapturedCommand($"{command} (sent to server)");
                }
                else
                {
                    ShowCapturedCommand($"{command} (not connected - not sent)");
                }
            }
        }

        long? liveProject = GameState.ResolveProject(_mem, gameManager.Value);
        if (liveProject != _currentProject)
        {
            // The game re-created its object tree (e.g. a save was loaded while attached) - every address
            // cached against the old tree is now potentially dangling, so drop them all rather than let
            // later tree-walking code (FindAllCarryItems, etc.) chase stale/freed pointers and crash.
            _currentProject = liveProject;
            _currentInventoryRoom = null;
            _currentMailManRoom = null;
            _lastRoomNodeView = null;
            _lastDisplayedRnv = null;
            _classUpgradeSpoofOriginalClass = null; // the old gameManager address (and any spoof on it) is gone
            _saveSeedGuardState = SaveSeedGuardState.Unverified;
            _saveSeedGuardBeamBridgeMisses = 0;
            _saveSeedGuardTagMismatches = 0;
            GameActions.ClearHiddenRoomAddressCache();
            SetAddressRow("_project", _currentProject);
        }
        if (_currentProject is not null)
        {
            if (_currentInventoryRoom is null)
            {
                _currentInventoryRoom = GameState.FindInventoryRoom(_mem, _currentProject.Value);
                SetAddressRow("Player Inventory (CPetControl)", _currentInventoryRoom);
            }
            if (_currentMailManRoom is null)
                _currentMailManRoom = GameState.FindMailManRoom(_mem, _currentProject.Value);
        }

        // --- Read-only display refresh: always runs, independent of the save/seed guard below, so the Live/Mail/
        // Items tabs stay populated even before an AP connection has verified this save. Uses _lastDisplayedRnv
        // (rather than _lastRoomNodeView, which the AP-facing write logic further down still gates on the guard)
        // so that once the guard does pass, room-change-triggered writes still correctly fire for every room
        // change that happened while unverified.
        RoomNodeView? rnv = GameState.ReadRoomNodeView(_mem, gameManager.Value);
        if (rnv is not null && rnv != _lastDisplayedRnv)
        {
            _lastDisplayedRnv = rnv;
            string roomName = RoomNames.GetName(rnv.Value.Room);
            _currentRoomName = roomName;
            _lblRoomNodeView.Text = $"Room: {rnv.Value.Room}   Node: {rnv.Value.Node}   View: {rnv.Value.View}";
            UpdateMailCurrentRoomLabel();
            UpdateInfoRoom(roomName);
        }

        if (_currentInventoryRoom is not null)
        {
            uint? roomFlags = GameState.ReadCurrentRoomFlags(_mem, _currentInventoryRoom.Value);
            if (roomFlags is not null && roomFlags != _lastRoomFlags)
            {
                _lastRoomFlags = roomFlags;
                _lblCurrentLocation.Text = FormatCurrentLocation(roomFlags.Value);
            }
        }

        int? passengerClass = GameState.ReadPassengerClass(_mem, gameManager.Value);
        if (passengerClass is not null && passengerClass != _lastPassengerClass)
        {
            _lastPassengerClass = passengerClass;
            _lblClass.Text = $"Class: {PassengerClassNames.GetName(passengerClass.Value)}";
        }

        UpdateInfoChecks();

        if (_tickCount % InventoryIntervalTicks == 0)
        {
            UpdateInventory(gameManager.Value);
        }
        if (_tickCount % MailIntervalTicks == 0)
        {
            UpdateMail(gameManager.Value);
        }

        // --- Save/seed guard gate: everything below reads AND writes to the save on the AP server's behalf
        // (item syncing, location checks, mail delivery, class upgrades) - unlike the read-only refresh above,
        // this must not run until the attached save is verified against the connected AP seed.
        EvaluateSaveSeedGuard(gameManager.Value);
        UpdateSaveSeedGuardStatusLabel();
        UpdateInfoAreaSaveSeedGuard();
        if (_chkEnforceSaveSeedGuard.Checked && _saveSeedGuardState != SaveSeedGuardState.Ok)
            return;

        _apConnection.NotifyGameVerifiedForSeed();

        // Must run before anything below touches PassengerClass (e.g. TryApplyClassUpgradeSpoof off the RNV-change
        // block just below): if the game was last saved while class-spoofed, the on-disk PassengerClass can be a
        // lie, and _classUpgradeSpoofOriginalClass (the in-memory backup of the true value) doesn't survive an app
        // restart. Reconciling PassengerClass against AP items here first ensures nothing downstream reads that
        // stale/spoofed value before it's corrected.
        SyncPassengerClassFromItems(gameManager.Value);
        SyncStateroomFromItems(gameManager.Value);

        ProcessPendingUnrestoreChecks(gameManager.Value);

        if (rnv is not null && rnv != _lastRoomNodeView)
        {
            RoomNodeView? previousRnv = _lastRoomNodeView;
            int? previousRoom = previousRnv?.Room;
            _lastRoomNodeView = rnv;
            string roomName = _currentRoomName ?? RoomNames.GetName(rnv.Value.Room);

            if (rnv.Value.Room != previousRoom)
            {
                DeliverQueuedMailAtStation(roomName);
                TrySendRoomVisitCheck(roomName);
            }

            TrySendPointOfInterestCheck(rnv.Value);
            TryShowItemNeededReminder(rnv.Value);

            if (previousRnv is not null)
            {
                TryUnrestoreItemsLeavingRnv(previousRnv.Value, gameManager.Value);
            }
            TryRevertClassUpgradeSpoof(rnv.Value, gameManager.Value);
            TryRevertSgtGlyphSpoof(rnv.Value);
            TryRestoreItemsAtHomeRnv(rnv.Value, gameManager.Value);
            TryApplyClassUpgradeSpoof(rnv.Value, gameManager.Value);
            TryApplySgtGlyphSpoof(rnv.Value);
        }

        SyncTableAccessFromItems();

        UpdatePendingChecksLabel();

        if (_tickCount % InventoryIntervalTicks == 0)
        {
            ReconcileTrackedItems(gameManager.Value);
        }

        if (ClassUpgradeHook.IsInstalled)
        {
            int? attemptedClass = ClassUpgradeHook.PollAttemptedClass(_mem);
            if (attemptedClass is not null)
            {
                if (LocationChecks.TryGetClassUpgradeLocationName(attemptedClass.Value, out string locationName))
                {
                    AppendLog($"ClassUpgradeHook: blocked setPassengerClass({PassengerClassNames.GetName(attemptedClass.Value)}) from taking effect - real class stays item-gated");

                    bool handedOff = _apConnection.SendLocationCheck(locationName);
                    if (LocationChecks.TryGetClassUpgradeEventLocationName(attemptedClass.Value, out string eventLocationName))
                        _apConnection.SendLocationCheck(eventLocationName);
                    ShowActionResult(handedOff, handedOff
                        ? $"DeskBot upgrade attempt ({PassengerClassNames.GetName(attemptedClass.Value)}) -> {locationName}"
                        : $"DeskBot upgrade attempt ({PassengerClassNames.GetName(attemptedClass.Value)}) queued (offline) -> {locationName}");
                }
                else if (attemptedClass.Value == SpecialPassengerClassValues.BridgeAccessClassValue)
                {
                    if (_currentInventoryRoom is not null)
                    {
                        bool applied = GameActions.SetPassengerClassFull(_mem, gameManager.Value, _currentInventoryRoom.Value, attemptedClass.Value);

                        const string titaniaRepairedLocation = "Titania's Room - Repair Titania";
                        bool handedOff = _apConnection.SendLocationCheck(titaniaRepairedLocation);
                        ShowActionResult(applied && handedOff, handedOff
                            ? $"Bridge access granted -> {titaniaRepairedLocation}"
                            : $"Bridge access granted -> {titaniaRepairedLocation} (queued offline or class-apply failed)");
                        UpdatePendingChecksLabel();
                    }
                    else
                    {
                        ShowActionResult(false, "Bridge-access class upgrade blocked - inventory not resolved yet, will retry on next attempt");
                    }
                }
                else if (attemptedClass.Value == (int)PassengerClass.Third && _chkAllowInitialUpgrade.Checked)
                {
                    if (_currentInventoryRoom is not null)
                    {
                        bool ok = GameActions.SetPassengerClassFull(_mem, gameManager.Value, _currentInventoryRoom.Value, attemptedClass.Value);
                        ShowActionResult(ok, "Initial class upgrade (None -> Third) applied directly - no AP item for this yet");
                    }
                    else
                    {
                        ShowActionResult(false, "Initial class upgrade blocked - inventory not resolved yet, will retry on next attempt");
                    }
                }
                else
                {
                    ShowActionResult(false, $"DeskBot upgrade attempt for unrecognized class {attemptedClass.Value}");
                }
            }
        }

        if (RoomAssignHook.IsInstalled)
        {
            int? attemptedClass = RoomAssignHook.PollAttemptedClass(_mem);
            if (attemptedClass is not null)
            {
                AppendLog($"RoomAssignHook: blocked petReassignRoom({PassengerClassNames.GetName(attemptedClass.Value)}) from taking effect - room assignment stays item-gated");
            }
        }

        if (GetLiftEye2GateHook.IsInstalled)
        {
            if (GetLiftEye2GateHook.PollBlockedAttempt(_mem))
            {
                HandleBlockedLiftEyeDrag();
            }
        }
    }
}
