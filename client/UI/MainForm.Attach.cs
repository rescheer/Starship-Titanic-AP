namespace StarshipTitanicAp;

public sealed partial class MainForm
{
    private void AttemptAttach()
    {
        bool ok = _mem.Attach(ProcessName);
        if (ok)
        {
            _lblStatus.Text = $"Attached (PID {_mem.ProcessId}, base 0x{_mem.ModuleBase:X})";
            UpdateFooterAttachStatus();
            AppendServerLog($"CLIENT: Attached to Starship Titanic (PID {_mem.ProcessId})");
            CheckReadyToPlay();
            SetAddressRow("Module base", _mem.ModuleBase);
            _btnAttach.Enabled = false;
            _btnDetach.Enabled = true;
            ResetCachedState();
            DoInstallHook();
            DoInstallClassLockHook();
            DoInstallMaitreDHook();
            DoInstallGetLiftEye2GateHook();

            long? gameManager = GameState.ResolveGameManager(_mem);
            _currentGameManager = gameManager;
            if (gameManager is not null)
            {
                _currentProject = GameState.ResolveProject(_mem, gameManager.Value);
                if (_currentProject is not null)
                {
                    DoRefreshAllItems();

                    EvaluateSaveSeedGuard(gameManager.Value);
                }
            }
        }
        else
        {
            _lblStatus.Text = $"Could not find/attach to \"{ProcessName}\" - is the game running?";
            UpdateFooterAttachStatus();
            AppendServerLog($"CLIENT: Failed to attach to \"{ProcessName}\" - is the game running?");
        }
    }

    private void DoDetach()
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

        _mem.Detach();
        _lblStatus.Text = "Not attached";
        UpdateFooterAttachStatus();
        _lblRoomNodeView.Text = "Room: -   Node: -   View: -";
        _lblCurrentLocation.Text = "Location: -";
        _lblClass.Text = "Class: -";
        _lvInventory.Items.Clear();
        _lblAllItemsStatus.Text = "";
        _lvMail.Items.Clear();
        _lblMailCurrentRoom.Text = "Current room: -";
        _lblMailCount.Text = "";
        _lblHookStatus.Text = "Hook not installed";
        _btnInstallHook.Enabled = true;
        _btnUninstallHook.Enabled = false;
        _lblClassLockHookStatus.Text = "Lock not installed";
        _btnInstallClassLockHook.Enabled = true;
        _btnUninstallClassLockHook.Enabled = false;
        _btnAttach.Enabled = true;
        _btnDetach.Enabled = false;
        ResetCachedState();

        foreach (string key in new[]
        {
            "Module base", "gameManager", "_project",
            "Player Inventory (CPetControl)", "Mail Inventory (CMailMan)", "PassengerClass field",
            "Conversations (CPetConversations)"
        })
        {
            SetAddressRow(key, null);
        }
    }

    private void ResetCachedState()
    {
        _lastRoomNodeView = null;
        _lastRoomFlags = null;
        _lastPassengerClass = null;
        _lastInventory = null;
        _lastMailItems = null;
        _currentGameManager = null;
        _currentProject = null;
        _currentInventoryRoom = null;
        _currentMailManRoom = null;
        _currentRoomName = null;
        _conversationsAddrShown = false;
        _saveSeedGuardState = SaveSeedGuardState.Unverified;
        _saveSeedGuardBeamBridgeMisses = 0;
        _saveSeedGuardTagMismatches = 0;
        GameActions.ClearHiddenRoomAddressCache();
        GameState.ClearClassNameCache();
    }
}
