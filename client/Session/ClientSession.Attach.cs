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
            _currentGameManager = gameManager;
            if (gameManager is not null)
            {
                _currentProject = GameState.ResolveProject(_mem, gameManager.Value);
                if (_currentProject is not null)
                {
                    NotifyItemsChanged();

                    EvaluateSaveSeedGuard(gameManager.Value);
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
        ReportAction(RoomAssignHook.Install(_mem), "Install room assignment lock");
        ReportAction(RoomGateHook.Install(_mem), "Install room key gate");

        bool saveLoadHookOk = SaveLoadHook.Install(_mem);
        ReportAction(saveLoadHookOk, "Install save-load detection hook");
        if (!saveLoadHookOk)
            AppendLog("SaveLoadHook: install failed (unexpected scummvm.exe build?) - loading a save while attached won't refresh cached addresses; detach and reattach after loading");
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
        _currentGameManager = null;
        _currentProject = null;
        _currentInventoryRoom = null;
        _currentMailManRoom = null;
        _currentRoomName = null;
        _movers = null;
        _conversationsAddrShown = false;
        _saveSeedGuardState = SaveSeedGuardState.Unverified;
        _saveSeedGuardBeamBridgeMisses = 0;
        _saveSeedGuardTagMismatches = 0;
        GameActions.ClearHiddenRoomAddressCache();
        GameState.ClearClassNameCache();
    }
}
