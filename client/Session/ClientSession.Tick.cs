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
        _itemSync.Reset(); // pending unrestores and the eaten Magazine were tracked against the old tree
        _classSync.Reset(); // the load overwrote PassengerClass and the glyphs, along with any spoof on them
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

        _itemSync.ProcessPendingDirtyReassert(gameManager.Value);

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
                // In game, only client commands run; server commands must go through /ap.
                if (_commands.Find(command) is { } runCommand)
                {
                    ReportCapturedCommand($"{command} (client command)");
                    runCommand();
                }
                else
                {
                    ReportCapturedCommand($"{command} (unknown command - not sent)");
                    _commands.ReportUnknown(command);
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
            _ctx.CurrentRoomName = roomName;
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

        ReleaseHeldPetMessages();

        // --- Save/seed guard gate: everything below reads and writes the save on the AP server's behalf
        // (item syncing, location checks, mail delivery, class upgrades).
        _saveSeedGuard.Evaluate(gameManager.Value);
        Ticked?.Invoke();
        if (Options.EnforceSaveSeedGuard && _saveSeedGuard.State != SaveSeedGuardState.Ok)
            return;

        _apConnection.NotifyGameVerifiedForSeed();

        // Runs before anything below reads or writes PassengerClass.
        _classSync.SyncFromItems(gameManager.Value);

        _itemSync.ProcessPendingUnrestoreChecks(gameManager.Value);

        if (rnv is not null && rnv != _lastRoomNodeView)
        {
            RoomNodeView? previousRnv = _lastRoomNodeView;
            int? previousRoom = previousRnv?.Room;
            _lastRoomNodeView = rnv;
            string roomName = _ctx.CurrentRoomName ?? RoomNames.GetName(rnv.Value.Room);

            if (rnv.Value.Room != previousRoom)
            {
                DeliverQueuedMailAtStation(roomName);
                TrySendRoomVisitCheck(roomName);
                if (roomName == MusicRoomControls.RoomName)
                    TrySolveMusicRoomControls();
            }

            // Runs before TrySendPointOfInterestCheck so a first visit's own check doesn't count as already checked.
            TryShowWaitingMailReminder(rnv.Value);
            TrySendPointOfInterestCheck(rnv.Value);
            TryShowItemNeededReminder(rnv.Value);

            if (previousRnv is not null)
            {
                _itemSync.TryUnrestoreItemsLeavingRnv(previousRnv.Value, gameManager.Value);
            }
            _classSync.RevertSpoofs(rnv.Value, gameManager.Value);
            _itemSync.TryRestoreItemsAtHomeRnv(rnv.Value, gameManager.Value);
            _classSync.ApplySpoofs(rnv.Value, gameManager.Value);
        }

        SyncTableAccessFromItems();
        SyncRoomGate();
        KeepTelevisionChannel4GlyphOn();
        KeepLiftEyeDestObjectNonNull();

        NotifyPendingChecksChanged();

        if (_ctx.TickCount % InventoryIntervalTicks == 0)
        {
            _itemSync.ReconcileTrackedItems(gameManager.Value);
        }

        _classSync.PollClassUpgradeHook(gameManager.Value);

        // Waits for the PET, which a blocked assignment under vanilla staterooms is applied to.
        if (RoomAssignHook.IsInstalled && _ctx.InventoryRoom is not null)
        {
            int? attemptedClass = RoomAssignHook.PollAttemptedClass(_mem);
            if (attemptedClass is not null && StateroomAssignTracker.IsVanilla(_apConnection.KnownSlotData))
            {
                _pendingVanillaRoomAssignmentClass = attemptedClass;
                SyncRoomAssignHook();
            }
            else if (attemptedClass is not null)
            {
                AppendLog($"RoomAssignHook: blocked petReassignRoom({PassengerClassNames.GetName(attemptedClass.Value)}) from taking effect - room assignment stays item-gated");
            }
        }
        TryApplyPendingRoomAssignment();

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
}
