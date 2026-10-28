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
        ReportAction(TextCommandHook.Install(_mem), "Install PET command hook");
        ReportAction(ClassUpgradeHook.Install(_mem), "Install class upgrade lock");
        ReportAction(MaitreDHook.Install(_mem), "Install Maitre'D table lock");
        ReportAction(GetLiftEye2GateHook.Install(_mem), "Install broken-elevator Eye gate");
        SyncRoomAssignHook();
        ReportAction(RoomGateHook.Install(_mem), "Install room key gate");

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
        if (SaveLoadHook.IsInstalled)
        {
            SaveLoadHook.Uninstall(_mem);
        }
    }

    private void ResetCachedState()
    {
        _lastRoomNodeView = null;
        _lastRoomFlags = null;
        _lastPassengerClass = null;
        _lastMailItems = null;
        _ctx.GameManager = null;
        _ctx.Project = null;
        _ctx.InventoryRoom = null;
        _ctx.MailManRoom = null;
        _ctx.CurrentRoomName = null;
        _movers = null;
        _conversationsAddrShown = false;
        _pendingVanillaRoomAssignmentClass = null;
        _saveSeedGuard.Reset();
        GameActions.ClearHiddenRoomAddressCache();
        GameState.ClearClassNameCache();
    }
}
