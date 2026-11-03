namespace StarshipTitanicAp;

internal sealed partial class ClientSession
{
    /// <summary>Address rows reported by the session, cleared on detach.</summary>
    private static readonly string[] AddressLabels =
    {
        "Module base", "gameManager", "_project",
        "Player Inventory (CPetControl)", "Mail Inventory (CMailMan)", "PassengerClass field",
        "Conversations (CPetConversations)",
    };

    internal void Attach()
    {
        bool ok = _mem.Attach(ProcessName);
        if (ok)
        {
            AttachStatus = $"Attached (PID {_mem.ProcessId}, base 0x{_mem.ModuleBase:X})";
            AppendServerLog($"CLIENT: Attached to Starship Titanic (PID {_mem.ProcessId})");
            CheckReadyToPlay();
            ReportAddress("Module base", _mem.ModuleBase);
            ResetCachedState();
            InstallHooks();

            long? gameManager = GameState.ResolveGameManager(_mem);
            _ctx.GameManager = gameManager;
            if (gameManager is not null)
            {
                _ctx.Project = GameState.ResolveProject(_mem, gameManager.Value);
                if (_ctx.Project is not null)
                {
                    NotifyItemsChanged();

                    _saveSeedGuard.Evaluate(gameManager.Value);
                }
            }
        }
        else
        {
            AttachStatus = $"Could not find/attach to \"{ProcessName}\" - is the game running?";
            _readyToPlayLogged = false;
            AppendServerLog($"CLIENT: Failed to attach to \"{ProcessName}\" - is the game running?");
        }

        AttachChanged?.Invoke();
    }

    internal void Detach()
    {
        UninstallHooks();

        _mem.Detach();
        AttachStatus = "Not attached";
        _readyToPlayLogged = false;
        _heldPetMessages.Clear();
        ResetCachedState();

        foreach (string key in AddressLabels)
        {
            ReportAddress(key, null);
        }

        AttachChanged?.Invoke();
    }

    private void InstallHooks()
    {
        bool gameThreadHookOk = GameThreadHook.Install(_mem);
        ReportAction(gameThreadHookOk, "Install game thread call hook");
        if (!gameThreadHookOk)
            AppendLog("GameThreadHook: install failed (unexpected scummvm.exe build?) - calls into the game will run on a remote thread alongside the game, which can crash it");

        ReportAction(TextCommandHook.Install(_mem), "Install PET command hook");
        ReportAction(ClassUpgradeHook.Install(_mem), "Install class upgrade lock");
        ReportAction(MaitreDHook.Install(_mem), "Install Maitre'D table lock");
        ReportAction(MaitreDPacifiedHook.Install(_mem), "Install Maitre'D pacified-flag hold");
        ReportAction(GetLiftEye2GateHook.Install(_mem), "Install broken-elevator Eye gate");
        SyncRoomAssignHook();
        ReportAction(RoomGateHook.Install(_mem), "Install room key gate");
        SyncTravelSkipHook();
        SyncBarbotVodkaPatch();

        bool saveLoadHookOk = SaveLoadHook.Install(_mem);
        ReportAction(saveLoadHookOk, "Install save-load detection hook");
        if (!saveLoadHookOk)
            AppendLog("SaveLoadHook: install failed (unexpected scummvm.exe build?) - loading a save while attached won't refresh cached addresses; detach and reattach after loading");
    }

    /// <summary>Installs the room assignment lock unless the seed leaves staterooms vanilla. Re-run once slot data
    /// arrives, since the game can be attached before connecting - and so before the client knows the option.</summary>
    private void SyncRoomAssignHook()
    {
        if (!_mem.IsAttached)
            return;

        bool vanilla = StateroomAssignTracker.IsVanilla(_apConnection.KnownSlotData);
        if (!vanilla && !RoomAssignHook.IsInstalled)
        {
            ReportAction(RoomAssignHook.Install(_mem), "Install room assignment lock");
        }
        else if (vanilla && RoomAssignHook.IsInstalled)
        {
            // Uninstalling frees the mailbox, so first take any assignment blocked before the option was known.
            // Uninstalls right away even without the PET resolved: left installed, the DeskBot's check-in would be
            // blocked too, and the room it then reads back for its dialogue would be unassigned.
            if (RoomAssignHook.PollAttemptedClass(_mem) is { } attemptedClass)
                _pendingVanillaRoomAssignmentClass = attemptedClass;

            ReportAction(RoomAssignHook.Uninstall(_mem), "Uninstall room assignment lock (vanilla staterooms)");
        }

        TryApplyPendingRoomAssignment();
    }

    /// <summary>Installs the travel skip if the seed has Quick Elevator or Quick Pellerator on, with only those halves
    /// enabled. Re-run once slot data arrives; until then the hook is left as it is.</summary>
    private void SyncTravelSkipHook()
    {
        if (!_mem.IsAttached || _apConnection.KnownSlotData is not { } slotData)
            return;

        bool lift = SlotDataFlag(slotData, "quick_elevator");
        bool pellerator = SlotDataFlag(slotData, "quick_pellerator");
        TravelSkipHook.SetEnabled(lift, pellerator);
        if ((lift || pellerator) && !TravelSkipHook.IsInstalled)
            ReportAction(TravelSkipHook.Install(_mem), "Install quick elevator/pellerator hook");
        else if (!lift && !pellerator && TravelSkipHook.IsInstalled)
            ReportAction(TravelSkipHook.Uninstall(_mem), "Uninstall quick elevator/pellerator hook (options off)");
    }

    /// <summary>Applies the Barbot vodka patch if the seed has Quick Barbot Freakout on. Re-run once slot data
    /// arrives; until then the patch is left as it is.</summary>
    private void SyncBarbotVodkaPatch()
    {
        if (!_mem.IsAttached || _apConnection.KnownSlotData is not { } slotData)
            return;

        bool enabled = SlotDataFlag(slotData, "quick_barbot_freakout");
        if (enabled && !BarbotVodkaPatch.IsInstalled)
            ReportAction(BarbotVodkaPatch.Install(_mem), "Install quick Barbot freakout patch");
        else if (!enabled && BarbotVodkaPatch.IsInstalled)
            ReportAction(BarbotVodkaPatch.Uninstall(_mem), "Uninstall quick Barbot freakout patch (option off)");
    }

    /// <summary>A boolean slot data key. False when missing (older seeds) or malformed.</summary>
    private static bool SlotDataFlag(IReadOnlyDictionary<string, object> slotData, string key)
    {
        if (!slotData.TryGetValue(key, out object? raw))
            return false;
        try
        {
            return Convert.ToBoolean(raw);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Performs a room assignment RoomAssignHook blocked, for seeds with vanilla stateroom assignment, once
    /// the PET is resolved.</summary>
    private void TryApplyPendingRoomAssignment()
    {
        if (_pendingVanillaRoomAssignmentClass is not { } attemptedClass)
            return;
        if (_ctx.GameManager is not { } gameManager || _ctx.InventoryRoom is not { } petControlAddr)
            return;

        _pendingVanillaRoomAssignmentClass = null;
        bool ok = GameActions.AssignNextRoom(_mem, gameManager, petControlAddr, attemptedClass);
        ReportAction(ok, $"Stateroom assignment ({PassengerClassNames.GetName(attemptedClass)}) applied (vanilla staterooms)");
    }

    private void UninstallHooks()
    {
        if (TextCommandHook.IsInstalled)
        {
            TextCommandHook.Uninstall(_mem);
        }
        if (ClassUpgradeHook.IsInstalled)
        {
            ClassUpgradeHook.Uninstall(_mem);
        }
        if (MaitreDHook.IsInstalled)
        {
            MaitreDHook.Uninstall(_mem);
        }
        if (MaitreDPacifiedHook.IsInstalled)
        {
            MaitreDPacifiedHook.Uninstall(_mem);
        }
        if (GetLiftEye2GateHook.IsInstalled)
        {
            GetLiftEye2GateHook.Uninstall(_mem);
        }
        if (RoomAssignHook.IsInstalled)
        {
            RoomAssignHook.Uninstall(_mem);
        }
        if (RoomGateHook.IsInstalled)
        {
            RoomGateHook.Uninstall(_mem);
        }
        if (TravelSkipHook.IsInstalled)
        {
            TravelSkipHook.Uninstall(_mem);
        }
        if (BarbotVodkaPatch.IsInstalled)
        {
            BarbotVodkaPatch.Uninstall(_mem);
        }
        if (SaveLoadHook.IsInstalled)
        {
            SaveLoadHook.Uninstall(_mem);
        }
        if (GameThreadHook.IsInstalled)
        {
            GameThreadHook.Uninstall(_mem);
        }
    }

    private void ResetCachedState()
    {
        _lastRoomNodeView = null;
        _lastRoomFlags = null;
        _lastPassengerClass = null;
        _lastTitaniaRepaired = null;
        _lastMailItems = null;
        _ctx.GameManager = null;
        _ctx.Project = null;
        _ctx.InventoryRoom = null;
        _ctx.MailManRoom = null;
        _ctx.CurrentRoomName = null;
        _movers = null;
        _conversationsAddrShown = false;
        _pendingVanillaRoomAssignmentClass = null;
        _parrotHuntSync.Reset();
        _skipIntro.Reset();
        _itemSync.ClearTitaniaPartsInHand();
        _saveSeedGuard.Reset();
        GameState.ClearClassNameCache();
    }
}
