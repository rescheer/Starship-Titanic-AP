namespace StarshipTitanicAp;

internal sealed partial class ClientSession
{
    /// <summary>Handles the game rebuilding its object tree (a save was loaded while attached): clears every address
    /// and spoof state cached against the old tree and resets the save/seed guard to Unverified.</summary>
    private void HandleTreeReloaded()
    {
        _ctx.InventoryRoom = null;
        _ctx.MailManRoom = null;
        _lastRoomNodeView = null;
        _lastDisplayedRnv = null;
        _lastRoomFlags = null;
        _lastPassengerClass = null;
        _lastMailItems = null;
        _pendingUnrestoreChecks.Clear();
        _classUpgradeSpoofOriginalClass = null; // the old PassengerClass value (and any spoof on it) was overwritten by the load
        _lastItemsReceivedCount = -1; // re-sync class and staterooms against the loaded save
        _lastStateroomItemsReceivedCount = -1;
        _sgtGlyphAddr = null; // the glyph lived inside the old CPetControl
        _sgtGlyphOriginalMode = null;
        _eatenMagazineAddr = null; // flagged against the old tree
        _movers = null; // scanned from the old tree
        _conversationsAddrShown = false;
        _saveSeedGuard.Reset();
        GameActions.ClearHiddenRoomAddressCache();
        TextCommandHook.ResetConversationsAddr(_mem);
        ReportAddress("Player Inventory (CPetControl)", null);
        ReportAddress("Conversations (CPetConversations)", null);
    }

    private void OnTick()
    {
        if (!_mem.IsAttached)
            return;

        if (_mem.HasProcessExited())
        {
            AppendServerLog("CLIENT: Starship Titanic (scummvm) closed - detaching");
            Detach();
            return;
        }

        switch (SaveLoadHook.Poll(_mem))
        {
            case SaveLoadHook.LoadState.Loading:
                // The tree is being torn down and rebuilt on the game thread right now - don't touch any of it.
                RoomNodeViewChanged?.Invoke(null, "loading save");
                return;
            case SaveLoadHook.LoadState.JustLoaded:
                AppendLog("SaveLoadHook: save loaded - dropping all cached tree addresses");
                HandleTreeReloaded();
                return; // postLoad itself may still be running on the game thread - pick up fresh on the next tick
        }

        _ctx.TickCount++;

        long? gameManager = GameState.ResolveGameManager(_mem);
        _ctx.GameManager = gameManager;

        if (gameManager is null)
        {
            RoomNodeViewChanged?.Invoke(null, "menu / loading");
            ReportAddress("gameManager", null);
            return;
        }

        ReportAddress("gameManager", gameManager.Value);
        ReportAddress("PassengerClass field", gameManager.Value + GameOffsets.PassengerClass);

        if (_pendingDirtyReassertTick is { } dueTick && _ctx.TickCount >= dueTick)
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

                if (_ctx.InventoryRoom is not null)
                {
                    long computed = GameState.ResolveConversationsAddr(_ctx.InventoryRoom.Value);
                    if (computed != TextCommandHook.ConversationsAddr)
                    {
                        ReportAction(false,
                            $"WARNING: static _conversations offset (0x{computed:X}) disagrees with live hook capture (0x{TextCommandHook.ConversationsAddr:X}) - PetConversationsFieldOffset may need re-checking");
                    }
                }
            }

            if (command is not null)
            {
                if (string.Equals(command.Trim(), "force_seed", StringComparison.OrdinalIgnoreCase))
                {
                    ReportCapturedCommand($"{command} (handled locally - not sent to server)");
                    HandleForceSeedCommand();
                }
                else if (_apConnection.IsConnected)
                {
                    _apConnection.SendCommand(command);
                    ReportCapturedCommand($"{command} (sent to server)");
                }
                else
                {
                    ReportCapturedCommand($"{command} (not connected - not sent)");
                }
            }
        }

        long? liveProject = GameState.ResolveProject(_mem, gameManager.Value);
        if (liveProject != _ctx.Project)
        {
            // Normally only the first non-null read after attaching.
            HandleTreeReloaded();
            _ctx.Project = liveProject;
            ReportAddress("_project", _ctx.Project);
        }
        if (_ctx.Project is not null)
        {
            if (_ctx.InventoryRoom is null)
            {
                _ctx.InventoryRoom = GameState.FindInventoryRoom(_mem, _ctx.Project.Value);
                ReportAddress("Player Inventory (CPetControl)", _ctx.InventoryRoom);
            }
            if (_ctx.MailManRoom is null)
                _ctx.MailManRoom = GameState.FindMailManRoom(_mem, _ctx.Project.Value);
        }

        // --- Read-only display refresh: runs regardless of the save/seed guard below. Tracks _lastDisplayedRnv,
        // separately from the guarded _lastRoomNodeView.
        RoomNodeView? rnv = GameState.ReadRoomNodeView(_mem, gameManager.Value);
        if (rnv is not null && rnv != _lastDisplayedRnv)
        {
            _lastDisplayedRnv = rnv;
            string roomName = RoomNames.GetName(rnv.Value.Room);
            _currentRoomName = roomName;
            RoomNodeViewChanged?.Invoke(rnv, null);
        }

        if (_ctx.InventoryRoom is not null)
        {
            uint? roomFlags = GameState.ReadCurrentRoomFlags(_mem, _ctx.InventoryRoom.Value);
            if (roomFlags is not null && roomFlags != _lastRoomFlags)
            {
                _lastRoomFlags = roomFlags;
                RoomFlagsChanged?.Invoke(roomFlags.Value);
            }
        }

        int? passengerClass = GameState.ReadPassengerClass(_mem, gameManager.Value);
        if (passengerClass is not null && passengerClass != _lastPassengerClass)
        {
            _lastPassengerClass = passengerClass;
            PassengerClassChanged?.Invoke(passengerClass.Value);
        }

        if (_ctx.TickCount % InventoryIntervalTicks == 0)
        {
            RefreshProjectAddresses(gameManager.Value);
        }
        if (_ctx.TickCount % MailIntervalTicks == 0)
        {
            UpdateMail();
        }

        // --- Save/seed guard gate: everything below reads and writes the save on the AP server's behalf
        // (item syncing, location checks, mail delivery, class upgrades).
        _saveSeedGuard.Evaluate(gameManager.Value);
        Ticked?.Invoke();
        if (Options.EnforceSaveSeedGuard && _saveSeedGuard.State != SaveSeedGuardState.Ok)
            return;

        _apConnection.NotifyGameVerifiedForSeed();

        // Runs before anything below reads or writes PassengerClass.
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

            // Runs before TrySendPointOfInterestCheck so a first visit's own check doesn't count as already checked.
            TryShowWaitingMailReminder(rnv.Value);
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
        SyncRoomGate();
        KeepTelevisionChannel4GlyphOn();
        KeepLiftEyeDestObjectNonNull();

        NotifyPendingChecksChanged();

        if (_ctx.TickCount % InventoryIntervalTicks == 0)
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
                    ReportAction(handedOff, handedOff
                        ? $"DeskBot upgrade attempt ({PassengerClassNames.GetName(attemptedClass.Value)}) -> {locationName}"
                        : $"DeskBot upgrade attempt ({PassengerClassNames.GetName(attemptedClass.Value)}) queued (offline) -> {locationName}");
                }
                else if (attemptedClass.Value == SpecialPassengerClassValues.BridgeAccessClassValue)
                {
                    if (_ctx.InventoryRoom is not null)
                    {
                        bool applied = GameActions.SetPassengerClassFull(_mem, gameManager.Value, _ctx.InventoryRoom.Value, attemptedClass.Value);

                        bool handedOff = _apConnection.SendLocationCheck(TitaniaRepairedLocation);
                        ReportAction(applied && handedOff, handedOff
                            ? $"Bridge access granted -> {TitaniaRepairedLocation}"
                            : $"Bridge access granted -> {TitaniaRepairedLocation} (queued offline or class-apply failed)");
                        NotifyPendingChecksChanged();
                    }
                    else
                    {
                        ReportAction(false, "Bridge-access class upgrade blocked - inventory not resolved yet, will retry on next attempt");
                    }
                }
                else if (attemptedClass.Value == (int)PassengerClass.Third && Options.AllowInitialUpgrade)
                {
                    if (_ctx.InventoryRoom is not null)
                    {
                        bool ok = GameActions.SetPassengerClassFull(_mem, gameManager.Value, _ctx.InventoryRoom.Value, attemptedClass.Value);
                        ReportAction(ok, "Initial class upgrade (None -> Third) applied directly - no AP item for this yet");
                    }
                    else
                    {
                        ReportAction(false, "Initial class upgrade blocked - inventory not resolved yet, will retry on next attempt");
                    }
                }
                else
                {
                    ReportAction(false, $"DeskBot upgrade attempt for unrecognized class {attemptedClass.Value}");
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

        if (RoomGateHook.IsInstalled)
        {
            if (RoomGateHook.PollBlockedMove(_mem) is { } blocked)
            {
                HandleBlockedRoomMove(blocked.FromRoom, blocked.ToRoom);
            }
        }
    }
}
