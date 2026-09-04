namespace StarshipTitanicAp;

public sealed partial class MainForm
{
    private readonly Button _btnDetach = new() { Text = "Detach", Width = 110, Enabled = false };

    private readonly Button _btnMarkAllDirty = new() { Text = "Call markAllDirty() [experimental]", Width = 240 };
    private readonly Button _btnResetPet = new() { Text = "Call CPetControl::reset()", Width = 240 };
    private readonly Button _btnInstallHook = new() { Text = "Install PET Command Hook", Width = 220 };
    private readonly Button _btnUninstallHook = new() { Text = "Uninstall Hook", Width = 220, Enabled = false };
    private readonly Label _lblHookStatus = new() { Text = "Hook not installed", AutoSize = true };
    private readonly Button _btnInstallClassLockHook = new() { Text = "Install Class Upgrade Lock", Width = 220 };
    private readonly Button _btnUninstallClassLockHook = new() { Text = "Uninstall Lock", Width = 220, Enabled = false };
    private readonly Label _lblClassLockHookStatus = new() { Text = "Lock not installed", AutoSize = true };
    private readonly Button _btnInstallMaitreDHook = new() { Text = "Install Maitre'D Table Lock", Width = 220 };
    private readonly Button _btnUninstallMaitreDHook = new() { Text = "Uninstall Lock", Width = 220, Enabled = false };
    private readonly Label _lblMaitreDHookStatus = new() { Text = "Lock not installed", AutoSize = true };
    private readonly Button _btnInstallGetLiftEye2GateHook = new() { Text = "Install Eye Gate", Width = 220 };
    private readonly Button _btnUninstallGetLiftEye2GateHook = new() { Text = "Uninstall Gate", Width = 220, Enabled = false };
    private readonly Label _lblGetLiftEye2GateHookStatus = new() { Text = "Gate not installed", AutoSize = true };
    private readonly CheckBox _chkAllowInitialUpgrade = new()
    {
        Text = "Allow initial upgrade (None -> Third) - no AP item for this yet",
        AutoSize = true,
        Checked = true,
    };
    private readonly CheckBox _chkEnforceSaveSeedGuard = new()
    {
        Text = "Enforce save/AP-seed guard",
        AutoSize = true,
        Checked = true,
    };
    private readonly Button _btnForceTagSaveSeed = new() { Text = "Force-Tag Save With Current Seed", Width = 240 };
    private readonly CheckBox _chkTakeUngrantedItems = new()
    {
        Text = "Take ungranted items away from player's inventory",
        AutoSize = true,
        Checked = true,
    };
    private readonly TextBox _txtItemAddr = new() { Width = 150, PlaceholderText = "item address (hex)" };
    private readonly TextBox _txtRoomAddrOverride = new() { Width = 150, PlaceholderText = "room address (hex)" };
    private readonly Button _btnMoveItem = new() { Text = "Move", Width = 70 };
    private readonly Button _btnForceReconcileItems = new() { Text = "Force Reconcile Items", Width = 220 };
    private readonly Button _btnScanShipSettings = new() { Text = "Scan Fuse Box CShipSetting Objects", Width = 260 };
    private readonly Button _btnFindGetLiftEye2 = new() { Text = "Find CGetLiftEye2 Object", Width = 260 };
    private readonly Button _btnClearGhostFuseSockets = new() { Text = "Clear Stale Beam/Chicken Socket Ghosts", Width = 260 };

    private readonly TextBox _txtDiffScanAddr = new() { Width = 150, PlaceholderText = "base address (hex)" };
    private readonly TextBox _txtDiffScanSize = new() { Width = 90, PlaceholderText = "size (hex)", Text = "1000" };
    private readonly Button _btnDiffSnapshotA = new() { Text = "Snapshot A", Width = 110 };
    private readonly Button _btnDiffSnapshotB = new() { Text = "Snapshot B / Diff", Width = 130 };
    private readonly Label _lblDiffScanStatus = new() { Text = "No snapshot taken", AutoSize = true };
    private byte[]? _diffScanSnapshotA;
    private long _diffScanSnapshotAddr;
    private int _diffScanSnapshotSize;

    private readonly TextBox _txtValueScanTarget = new() { Width = 110, PlaceholderText = "target uint32 (hex)" };
    private readonly Button _btnValueScanUseCurrentRoomFlags = new() { Text = "Use My Current Room Flags", Width = 190 };
    private readonly TextBox _txtValueScanAddr = new() { Width = 150, PlaceholderText = "base address (hex)" };
    private readonly TextBox _txtValueScanSize = new() { Width = 90, PlaceholderText = "size (hex)", Text = "100000" };
    private readonly Button _btnValueScan = new() { Text = "Scan For Value", Width = 130 };

    private readonly TextBox _txtDumpAddr = new() { Width = 150, PlaceholderText = "address (hex)" };
    private readonly CheckBox _chkDumpDereference = new() { Text = "Treat as pointer (dereference first)", AutoSize = true };
    private readonly TextBox _txtDumpSize = new() { Width = 90, PlaceholderText = "size (hex)", Text = "200" };
    private readonly Button _btnDumpMemory = new() { Text = "Dump", Width = 90 };

    private readonly Button _btnListRoomGlyphs = new() { Text = "List PET Room Glyphs", Width = 190 };

    private TabPage BuildDebugTab()
    {
        var page = new TabPage("Debug");
        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(10),
            AutoScroll = true,
        };

        layout.Controls.Add(SectionLabel("Connection"));
        layout.Controls.Add(_btnDetach);
        layout.Controls.Add(HelpLabel("Detaches from the game process. There's rarely a reason to do this deliberately - closing the app detaches automatically - but it's here for testing reattach behavior."));

        layout.Controls.Add(Spacer());
        layout.Controls.Add(SectionLabel("Save / AP-Seed Guard"));
        layout.Controls.Add(_chkEnforceSaveSeedGuard);
        layout.Controls.Add(HelpLabel("See SaveSeedGuard.cs. When on (default), the app refuses to read/write save data, send location checks, or grant items until the attached save's guard tag is confirmed to match the connected AP seed. Turn off only for deliberate testing across mismatched saves/seeds."));
        layout.Controls.Add(_btnForceTagSaveSeed);
        layout.Controls.Add(HelpLabel("Overwrites BeamBridge's guard tag with the currently connected AP seed unconditionally - including over an existing tag from a different seed. Use this to permanently associate a save with its current seed/server (e.g. after confirming by hand that they actually match)."));

        layout.Controls.Add(Spacer());
        layout.Controls.Add(SectionLabel("Item Tracking"));
        layout.Controls.Add(_chkTakeUngrantedItems);
        layout.Controls.Add(HelpLabel("When on (default), an ungranted item the player naturally picks up is hidden away until AP actually grants it (this app's normal design). Turn off to leave such items in the player's inventory instead - a debug/testing escape hatch."));

        layout.Controls.Add(Spacer());
        layout.Controls.Add(SectionLabel("Force Refresh"));
        layout.Controls.Add(_btnResetPet);
        layout.Controls.Add(HelpLabel("CPetControl::reset() - rebuilds the PET display from current state."));
        layout.Controls.Add(_btnMarkAllDirty);
        layout.Controls.Add(HelpLabel("CGameManager::markAllDirty() - kept for reference; not sufficient alone for inventory/class color."));

        layout.Controls.Add(Spacer());
        layout.Controls.Add(SectionLabel("Move Item to Custom Address"));
        var moveRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        moveRow.Controls.Add(_txtItemAddr);
        moveRow.Controls.Add(_txtRoomAddrOverride);
        moveRow.Controls.Add(_btnMoveItem);
        layout.Controls.Add(moveRow);
        layout.Controls.Add(HelpLabel("Moves an item to an arbitrary address instead of the inventory or mail system. PET refreshes automatically if either side happens to be the inventory."));

        layout.Controls.Add(Spacer());
        layout.Controls.Add(SectionLabel("Force Reconcile Items"));
        layout.Controls.Add(_btnForceReconcileItems);
        layout.Controls.Add(HelpLabel("Runs MainForm.GameLogic.cs's ReconcileTrackedItems on demand, for testing - it already runs automatically every ~1s, this just skips the wait. Safe to run repeatedly/anytime; only covers items with a confirmed AP item mapping - see ItemTracking.cs's class doc comment for what's excluded and why."));

        layout.Controls.Add(Spacer());
        layout.Controls.Add(SectionLabel("Ghost Fuse Investigation"));
        layout.Controls.Add(_btnScanShipSettings);
        layout.Controls.Add(HelpLabel("Read-only diagnostic. Finds every CShipSetting under the Titania room's [37,12,1] view and scans each one's memory for embedded CString structures, to discover the live offsets of _itemName/_target/_frameTarget (per ScummVM's titanic/game/ship_setting.cpp). Results go to the Log tab."));
        layout.Controls.Add(_btnClearGhostFuseSockets);
        layout.Controls.Add(HelpLabel("Resets any Fuse Box socket currently (mis)recorded as holding BeamBridge or ChickenBridge: clears _itemName, resets its cursor, and reloads its displayed frame - fixing already-corrupted saves where the ghost bug left a socket stuck showing (and mouse-cursor-reacting to) a fuse that isn't really there. The automatic hide-on-attach path now does this itself going forward; this is a one-off cleanup for existing saves."));

        layout.Controls.Add(Spacer());
        layout.Controls.Add(SectionLabel("Find CGetLiftEye2 (RE helper)"));
        layout.Controls.Add(_btnFindGetLiftEye2);
        layout.Controls.Add(HelpLabel("Read-only. Walks the 'Lift' room's (room 21) subtree for the CGetLiftEye2 instance (the broken elevator's 'take the Eye' hotspot) via its vtable class name, and reports its address plus its own _cursorId/_visible field addresses - useful for setting a targeted write-breakpoint on one of those instance fields in an external debugger, instead of a name-string breakpoint that fires from unrelated code too. Results go to the Log tab."));

        layout.Controls.Add(Spacer());
        layout.Controls.Add(SectionLabel("Memory Diff Scanner (RE helper)"));
        var diffScanAddrRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        diffScanAddrRow.Controls.Add(_txtDiffScanAddr);
        diffScanAddrRow.Controls.Add(_txtDiffScanSize);
        layout.Controls.Add(diffScanAddrRow);
        var diffScanBtnRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        diffScanBtnRow.Controls.Add(_btnDiffSnapshotA);
        diffScanBtnRow.Controls.Add(_btnDiffSnapshotB);
        layout.Controls.Add(diffScanBtnRow);
        layout.Controls.Add(_lblDiffScanStatus);
        layout.Controls.Add(HelpLabel("Read-only. Snapshots `size` bytes at `base address`, then diffs a second snapshot of the same region against it and logs every 4-byte-aligned offset whose int32 value changed (old -> new, both decimal and hex). Take snapshot A, trigger whatever in-game state change you're chasing (e.g. reassignRoom firing), then take snapshot B. Useful for locating unknown fields (like CPetRoomsGlyph::_mode) without a disassembler - e.g. base address = the PET control address (shown on the Live tab) to look for CPetRooms's own inline fields, or any heap address already found by other means. Results go to the Log tab."));

        layout.Controls.Add(Spacer());
        layout.Controls.Add(SectionLabel("Value Scanner (RE helper)"));
        var valueScanTargetRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        valueScanTargetRow.Controls.Add(_txtValueScanTarget);
        valueScanTargetRow.Controls.Add(_btnValueScanUseCurrentRoomFlags);
        layout.Controls.Add(valueScanTargetRow);
        var valueScanRegionRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        valueScanRegionRow.Controls.Add(_txtValueScanAddr);
        valueScanRegionRow.Controls.Add(_txtValueScanSize);
        valueScanRegionRow.Controls.Add(_btnValueScan);
        layout.Controls.Add(valueScanRegionRow);
        layout.Controls.Add(HelpLabel("Read-only. Searches `size` bytes starting at `base address` for every 4-byte-aligned occurrence of the target uint32, reading in 64KB chunks so unreadable gaps in the range are skipped rather than aborting the whole scan. 'Use My Current Room Flags' fills the target from a live read of GameOffsets.PetControlCurrentRoomFlags (stand in the room you want to find first - e.g. your assigned SGT Class Stateroom - since that field reflects wherever you're currently standing). Point base address well below/above the PET control address (shown on the Live tab) and widen size to search the surrounding heap for the CPetRoomsGlyph holding that room's _roomFlags. Matches go to the Log tab as absolute addresses."));

        layout.Controls.Add(Spacer());
        layout.Controls.Add(SectionLabel("Memory Dump (RE helper)"));
        var dumpRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        dumpRow.Controls.Add(_txtDumpAddr);
        dumpRow.Controls.Add(_txtDumpSize);
        dumpRow.Controls.Add(_btnDumpMemory);
        layout.Controls.Add(dumpRow);
        layout.Controls.Add(_chkDumpDereference);
        layout.Controls.Add(HelpLabel("Read-only. Dumps `size` bytes at `address` as hex rows of 16 bytes, each annotated with its four aligned int32 values (decimal) for spotting small candidates like a RoomGlyphMode (0/1/2). Check 'Treat as pointer' to read an 8-byte pointer at `address` first and dump from *that* target instead - use this to follow a candidate pointer found via the Memory Diff Scanner (e.g. a glyph-list head pointer) straight to the object it points at. Output goes to the Log tab."));

        layout.Controls.Add(Spacer());
        layout.Controls.Add(SectionLabel("PET Room Glyphs (RE helper)"));
        layout.Controls.Add(_btnListRoomGlyphs);
        layout.Controls.Add(HelpLabel("Read-only. Walks CPetRooms::_glyphs' linked list the same way GameActions.FindGlyphByRoomFlags does, logging every node's glyph address, _roomFlags (raw hex + decoded elevator/class/floor/room), and _mode. Use this to sanity-check the list traversal itself - e.g. confirming the SGT Class Stateroom's glyph is (or isn't) still present - separately from whether a specific target value matches. Results go to the Log tab."));

        page.Controls.Add(layout);
        return page;
    }

    private void DoMarkAllDirty()
    {
        if (!RequireAttachedAndResolved(out long gameManager))
            return;

        bool ok = GameActions.MarkAllDirty(_mem, gameManager);
        ShowActionResult(ok, "Called markAllDirty()");
    }

    private void DoInstallHook()
    {
        if (!_mem.IsAttached)
        {
            ShowActionResult(false, "Not attached");
            return;
        }

        bool ok = TextCommandHook.Install(_mem);
        if (ok)
        {
            _lblHookStatus.Text = $"Installed. Stub @ 0x{TextCommandHook.StubAddress:X}, mailbox @ 0x{TextCommandHook.MailboxAddress:X}";
            _btnInstallHook.Enabled = false;
            _btnUninstallHook.Enabled = true;
        }
        ShowActionResult(ok, "Install PET command hook");
    }

    private void DoUninstallHook()
    {
        if (!_mem.IsAttached)
        {
            ShowActionResult(false, "Not attached");
            return;
        }

        bool ok = TextCommandHook.Uninstall(_mem);
        if (ok)
        {
            _lblHookStatus.Text = "Hook not installed";
            _btnInstallHook.Enabled = true;
            _btnUninstallHook.Enabled = false;
        }
        ShowActionResult(ok, "Uninstall PET command hook");
    }

    private void DoInstallClassLockHook()
    {
        if (!_mem.IsAttached)
        {
            ShowActionResult(false, "Not attached");
            return;
        }

        bool ok = ClassUpgradeHook.Install(_mem);
        if (ok)
        {
            _lblClassLockHookStatus.Text = "Installed";
            _btnInstallClassLockHook.Enabled = false;
            _btnUninstallClassLockHook.Enabled = true;
        }
        ShowActionResult(ok, "Install class upgrade lock");
    }

    private void DoUninstallClassLockHook()
    {
        if (!_mem.IsAttached)
        {
            ShowActionResult(false, "Not attached");
            return;
        }

        bool ok = ClassUpgradeHook.Uninstall(_mem);
        if (ok)
        {
            _lblClassLockHookStatus.Text = "Lock not installed";
            _btnInstallClassLockHook.Enabled = true;
            _btnUninstallClassLockHook.Enabled = false;
        }
        ShowActionResult(ok, "Uninstall class upgrade lock");
    }

    private void DoInstallMaitreDHook()
    {
        if (!_mem.IsAttached)
        {
            ShowActionResult(false, "Not attached");
            return;
        }

        bool ok = MaitreDHook.Install(_mem);
        if (ok)
        {
            _lblMaitreDHookStatus.Text = "Installed";
            _btnInstallMaitreDHook.Enabled = false;
            _btnUninstallMaitreDHook.Enabled = true;
        }
        ShowActionResult(ok, "Install Maitre'D table lock");
    }

    private void DoUninstallMaitreDHook()
    {
        if (!_mem.IsAttached)
        {
            ShowActionResult(false, "Not attached");
            return;
        }

        bool ok = MaitreDHook.Uninstall(_mem);
        if (ok)
        {
            _lblMaitreDHookStatus.Text = "Lock not installed";
            _btnInstallMaitreDHook.Enabled = true;
            _btnUninstallMaitreDHook.Enabled = false;
        }
        ShowActionResult(ok, "Uninstall Maitre'D table lock");
    }

    private void DoInstallGetLiftEye2GateHook()
    {
        if (!_mem.IsAttached)
        {
            ShowActionResult(false, "Not attached");
            return;
        }

        bool ok = GetLiftEye2GateHook.Install(_mem);
        if (ok)
        {
            _lblGetLiftEye2GateHookStatus.Text = "Installed";
            _btnInstallGetLiftEye2GateHook.Enabled = false;
            _btnUninstallGetLiftEye2GateHook.Enabled = true;
        }
        ShowActionResult(ok, "Install broken-elevator Eye gate");
    }

    private void DoUninstallGetLiftEye2GateHook()
    {
        if (!_mem.IsAttached)
        {
            ShowActionResult(false, "Not attached");
            return;
        }

        bool ok = GetLiftEye2GateHook.Uninstall(_mem);
        if (ok)
        {
            _lblGetLiftEye2GateHookStatus.Text = "Gate not installed";
            _btnInstallGetLiftEye2GateHook.Enabled = true;
            _btnUninstallGetLiftEye2GateHook.Enabled = false;
        }
        ShowActionResult(ok, "Uninstall broken-elevator Eye gate");
    }

    private void DoResetPetControl()
    {
        if (!_mem.IsAttached)
        {
            ShowActionResult(false, "Not attached");
            return;
        }
        if (_currentInventoryRoom is null)
        {
            ShowActionResult(false, "CPetControl address not resolved yet (wait for inventory to resolve)");
            return;
        }

        bool ok = GameActions.ResetPetControl(_mem, _currentInventoryRoom.Value);
        ShowActionResult(ok, $"Called CPetControl::reset() on 0x{_currentInventoryRoom.Value:X}");
    }

    private void DoMoveToCustomAddress()
    {
        if (!RequireAttachedAndResolved(out long gameManager))
            return;

        if (!TryParseHex(_txtItemAddr.Text, out long itemAddr))
        {
            ShowActionResult(false, "Invalid item address");
            return;
        }
        if (!TryParseHex(_txtRoomAddrOverride.Text, out long roomAddr))
        {
            ShowActionResult(false, "Invalid room address");
            return;
        }

        MoveItemAndReport(gameManager, itemAddr, roomAddr, isMailDestination: false);
    }

    /// <summary>Manual, on-demand run of ReconcileTrackedItems.</summary>
    private void DoForceReconcileItems()
    {
        if (!RequireAttachedAndResolved(out long gameManager))
            return;

        ReconcileTrackedItems(gameManager);
        ShowActionResult(true, "Forced item reconciliation");
    }

    /// <summary>Unconditionally overwrites BeamBridge's save/seed guard tag with the currently connected AP seed.</summary>
    private void DoForceTagSaveSeed()
    {
        if (!RequireAttachedAndResolved(out long gameManager))
            return;
        if (_apConnection.SeedName is not { } seedName)
        {
            ShowActionResult(false, "Not connected to an AP server - no seed to tag with");
            return;
        }
        if (_currentProject is not { } project)
        {
            ShowActionResult(false, "Project not resolved yet");
            return;
        }

        long? beamBridgeAddr = SaveSeedGuard.FindBeamBridgeAddress(_mem, project);
        if (beamBridgeAddr is null)
        {
            ShowActionResult(false, "Could not locate BeamBridge to tag");
            return;
        }

        long tag = SaveSeedGuard.ComputeSeedTag(seedName);
        bool ok = SaveSeedGuard.WriteSeedTag(_mem, beamBridgeAddr.Value, tag);
        if (ok)
            _saveSeedGuardState = SaveSeedGuardState.Ok;
        ShowActionResult(ok, "Force-tagged save with current AP seed");
    }

    /// <summary>Read-only: finds the Fuse Box view's CShipSetting objects and dumps candidate CString fields
    /// found in their memory, to discover _itemName/_target/_frameTarget's live offsets (see ship_setting.cpp).</summary>
    private void DoScanShipSettings()
    {
        if (!RequireAttachedAndResolved(out long gameManager))
            return;
        if (_currentProject is not { } project)
        {
            ShowActionResult(false, "Project not resolved yet");
            return;
        }

        long? room = GameState.FindRoomByName(_mem, project, RoomNames.GetName(37));
        if (room is null)
        {
            ShowActionResult(false, $"Room '{RoomNames.GetName(37)}' (37) not found");
            return;
        }

        long? node = GameState.NthChildOfClass(_mem, room.Value, "CNodeItem", 12);
        if (node is null)
        {
            ShowActionResult(false, "CNodeItem #12 not found under Titania room");
            return;
        }

        long? view = GameState.NthChildOfClass(_mem, node.Value, "CViewItem", 1);
        if (view is null)
        {
            ShowActionResult(false, "CViewItem #1 not found under node 12");
            return;
        }

        AppendLog($"DEBUG: Fuse Box view [37,12,1] resolved to 0x{view.Value:X}");

        List<long> settings = GameState.FindAllDescendants(_mem, view.Value, "CShipSetting");
        if (settings.Count == 0)
        {
            AppendLog("DEBUG: no CShipSetting descendants found under that view");
            ShowActionResult(false, "No CShipSetting objects found");
            return;
        }

        foreach (long addr in settings)
        {
            string? name = GameState.TryReadName(_mem, addr);
            AppendLog($"DEBUG: CShipSetting 0x{addr:X} name='{name ?? "(none)"}'");

            List<(long Offset, int Size, string Text)> strings = GameState.ScanForCStrings(_mem, addr, 0x120, 0x300);
            if (strings.Count == 0)
            {
                AppendLog("  (no candidate CString fields found in 0x120..0x300)");
                continue;
            }

            foreach ((long offset, int size, string text) in strings)
                AppendLog($"  +0x{offset:X} (size={size}): \"{text}\"");
        }

        ShowActionResult(true, $"Scanned {settings.Count} CShipSetting object(s) - see Log tab");
    }

    /// <summary>Read-only: locates the CGetLiftEye2 instance (the broken elevator's "take the Eye" hotspot,
    /// room 21/"Lift") by walking the room's own subtree for that exact class name via its vtable, so a
    /// write-breakpoint can be set on one of its own instance fields (e.g. _cursorId or _visible) instead of a
    /// noisy breakpoint on the shared "GetLiftEye" name string, which fires from unrelated code too.</summary>
    private void DoFindGetLiftEye2()
    {
        if (!RequireAttachedAndResolved(out _))
            return;
        if (_currentProject is not { } project)
        {
            ShowActionResult(false, "Project not resolved yet");
            return;
        }

        const string liftRoomName = "Lift"; // RoomNames.GetName(21)
        long? room = GameState.FindRoomByName(_mem, project, liftRoomName);
        if (room is null)
        {
            ShowActionResult(false, $"Room '{liftRoomName}' (21) not found");
            return;
        }

        AppendLog($"DEBUG: '{liftRoomName}' room resolved to 0x{room.Value:X}");

        long? obj = GameState.FindDescendant(_mem, room.Value, null, "CGetLiftEye2");
        if (obj is null)
        {
            ShowActionResult(false, "No CGetLiftEye2 descendant found under the Lift room");
            return;
        }

        string? name = GameState.TryReadName(_mem, obj.Value);
        int? cursorId = _mem.ReadInt32(obj.Value + GameOffsets.GameObjectCursorIdOffset);
        byte[]? visible = _mem.ReadBytes(obj.Value + GameOffsets.GameObjectVisibleOffset, 1);

        AppendLog($"DEBUG: CGetLiftEye2 0x{obj.Value:X} name='{name ?? "(none)"}' " +
            $"_cursorId@0x{obj.Value + GameOffsets.GameObjectCursorIdOffset:X}={cursorId} " +
            $"_visible@0x{obj.Value + GameOffsets.GameObjectVisibleOffset:X}={(visible is null ? "?" : visible[0])}");
        ShowActionResult(true,
            $"CGetLiftEye2 @ 0x{obj.Value:X} - set a write breakpoint on 0x{obj.Value + GameOffsets.GameObjectCursorIdOffset:X} (_cursorId) or 0x{obj.Value + GameOffsets.GameObjectVisibleOffset:X} (_visible), then click the Eye - see Log tab");
    }

    /// <summary>Read-only: snapshots the configured base address/size for later diffing by <see cref="DoDiffSnapshotB"/>.</summary>
    private void DoDiffSnapshotA()
    {
        if (!_mem.IsAttached)
        {
            ShowActionResult(false, "Not attached");
            return;
        }
        if (!TryParseDiffScanRegion(out long addr, out int size))
            return;

        byte[]? bytes = _mem.ReadBytes(addr, size);
        if (bytes is null)
        {
            ShowActionResult(false, "Failed to read that region");
            return;
        }

        _diffScanSnapshotA = bytes;
        _diffScanSnapshotAddr = addr;
        _diffScanSnapshotSize = size;
        _lblDiffScanStatus.Text = $"Snapshot A: {size:X} bytes @ 0x{addr:X} - trigger the change, then Snapshot B";
        ShowActionResult(true, $"Took snapshot A (0x{size:X} bytes @ 0x{addr:X})");
    }

    /// <summary>Read-only: re-reads the same region and logs every 4-byte-aligned offset whose int32 value
    /// differs from <see cref="DoDiffSnapshotA"/>'s snapshot - a disassembler-free way to spot unknown fields
    /// (e.g. CPetRoomsGlyph::_mode) that change in response to some in-game action.</summary>
    private void DoDiffSnapshotB()
    {
        if (!_mem.IsAttached)
        {
            ShowActionResult(false, "Not attached");
            return;
        }
        if (_diffScanSnapshotA is null)
        {
            ShowActionResult(false, "Take Snapshot A first");
            return;
        }
        if (!TryParseDiffScanRegion(out long addr, out int size))
            return;
        if (addr != _diffScanSnapshotAddr || size != _diffScanSnapshotSize)
        {
            ShowActionResult(false, "Base address/size changed since Snapshot A - keep them the same for a diff");
            return;
        }

        byte[]? bytes = _mem.ReadBytes(addr, size);
        if (bytes is null)
        {
            ShowActionResult(false, "Failed to read that region");
            return;
        }

        AppendLog($"DEBUG: diffing 0x{size:X} bytes @ 0x{addr:X} against snapshot A");
        int diffCount = 0;
        for (int off = 0; off + 4 <= size; off += 4)
        {
            int oldVal = BitConverter.ToInt32(_diffScanSnapshotA, off);
            int newVal = BitConverter.ToInt32(bytes, off);
            if (oldVal == newVal)
                continue;

            diffCount++;
            AppendLog($"  +0x{off:X} (addr 0x{addr + off:X}): {oldVal} (0x{oldVal:X}) -> {newVal} (0x{newVal:X})");
        }

        _diffScanSnapshotA = bytes; // chain: B becomes the new baseline for a follow-up diff
        _lblDiffScanStatus.Text = $"Diffed - {diffCount} changed offset(s), see Log tab. New snapshot taken as baseline.";
        ShowActionResult(true, $"Diff complete: {diffCount} changed offset(s) - see Log tab");
    }

    private bool TryParseDiffScanRegion(out long addr, out int size)
    {
        addr = 0;
        size = 0;
        if (!TryParseHex(_txtDiffScanAddr.Text, out addr))
        {
            ShowActionResult(false, "Invalid base address");
            return false;
        }
        if (!TryParseHex(_txtDiffScanSize.Text, out long sizeLong) || sizeLong <= 0 || sizeLong > 0x100000)
        {
            ShowActionResult(false, "Invalid size (must be > 0 and <= 0x100000)");
            return false;
        }
        size = (int)sizeLong;
        return true;
    }

    /// <summary>Fills the value-scan target box from a live read of the current room-flags field - stand in the
    /// target room first (e.g. the assigned SGT Class Stateroom) since that field reflects wherever the player is
    /// currently standing (see GameOffsets.PetControlCurrentRoomFlags).</summary>
    private void DoUseCurrentRoomFlagsForValueScan()
    {
        if (_currentInventoryRoom is not { } petControlAddr)
        {
            ShowActionResult(false, "CPetControl address not resolved yet (wait for inventory to resolve)");
            return;
        }

        uint? roomFlags = GameState.ReadCurrentRoomFlags(_mem, petControlAddr);
        if (roomFlags is null)
        {
            ShowActionResult(false, "Failed to read current room flags");
            return;
        }

        _txtValueScanTarget.Text = roomFlags.Value.ToString("X");
        ShowActionResult(true, $"Target set to current room flags: 0x{roomFlags.Value:X}");
    }

    /// <summary>Read-only: scans a (possibly large) memory range in 4KB (page-granularity) chunks for every
    /// 4-byte-aligned occurrence of a target uint32 value, skipping chunks that fail to read instead of aborting -
    /// a disassembler-free way to locate an object (e.g. a CPetRoomsGlyph) holding a known field value (e.g.
    /// _roomFlags for a specific assigned room) somewhere in the heap. Chunk size deliberately matches the Windows
    /// VM page size (0x1000): ReadProcessMemory fails the *entire* call if any byte in the requested range is
    /// unmapped, so a larger chunk (e.g. 64KB) would silently lose whole neighborhoods of valid, readable memory
    /// any time it straddled a single unmapped page - including small, otherwise-isolated heap allocations.</summary>
    private void DoValueScan()
    {
        if (!_mem.IsAttached)
        {
            ShowActionResult(false, "Not attached");
            return;
        }
        if (!TryParseHex(_txtValueScanTarget.Text, out long targetLong))
        {
            ShowActionResult(false, "Invalid target value");
            return;
        }
        if (!TryParseHex(_txtValueScanAddr.Text, out long addr))
        {
            ShowActionResult(false, "Invalid base address");
            return;
        }
        if (!TryParseHex(_txtValueScanSize.Text, out long sizeLong) || sizeLong <= 0 || sizeLong > 0x8000000)
        {
            ShowActionResult(false, "Invalid size (must be > 0 and <= 0x8000000)");
            return;
        }

        int target = unchecked((int)targetLong);
        long size = sizeLong;
        const int ChunkSize = 0x1000;

        AppendLog($"DEBUG: scanning 0x{size:X} bytes @ 0x{addr:X} for value 0x{target:X8}");
        int matchCount = 0;
        int chunksSkipped = 0;
        for (long chunkStart = 0; chunkStart < size; chunkStart += ChunkSize)
        {
            int chunkLen = (int)Math.Min(ChunkSize, size - chunkStart);
            byte[]? bytes = _mem.ReadBytes(addr + chunkStart, chunkLen);
            if (bytes is null)
            {
                chunksSkipped++;
                continue;
            }

            for (int off = 0; off + 4 <= chunkLen; off += 4)
            {
                if (BitConverter.ToInt32(bytes, off) != target)
                    continue;

                matchCount++;
                AppendLog($"  match @ 0x{addr + chunkStart + off:X}");
            }
        }

        string skippedNote = chunksSkipped > 0 ? $" ({chunksSkipped} unreadable chunk(s) skipped)" : "";
        ShowActionResult(true, $"Scan complete: {matchCount} match(es){skippedNote} - see Log tab");
    }

    /// <summary>Read-only: dumps a memory region as hex rows of 16 bytes, each annotated with its four aligned
    /// int32 values - useful for eyeballing a candidate object for known-looking fields (e.g. a RoomGlyphMode
    /// 0/1/2, or a roomFlags-shaped uint) once a pointer to it has been found via the diff/value scanners.</summary>
    private void DoDumpMemory()
    {
        if (!_mem.IsAttached)
        {
            ShowActionResult(false, "Not attached");
            return;
        }
        if (!TryParseHex(_txtDumpAddr.Text, out long addr))
        {
            ShowActionResult(false, "Invalid address");
            return;
        }
        if (!TryParseHex(_txtDumpSize.Text, out long sizeLong) || sizeLong <= 0 || sizeLong > 0x4000)
        {
            ShowActionResult(false, "Invalid size (must be > 0 and <= 0x4000)");
            return;
        }
        int size = (int)sizeLong;

        if (_chkDumpDereference.Checked)
        {
            long? target = _mem.ReadInt64(addr);
            if (target is null)
            {
                ShowActionResult(false, "Failed to read pointer at that address");
                return;
            }
            AppendLog($"DEBUG: dereferenced pointer @ 0x{addr:X} -> 0x{target.Value:X}");
            addr = target.Value;
        }

        byte[]? bytes = _mem.ReadBytes(addr, size);
        if (bytes is null)
        {
            ShowActionResult(false, "Failed to read that region");
            return;
        }

        AppendLog($"DEBUG: dumping 0x{size:X} bytes @ 0x{addr:X}");
        for (int row = 0; row + 16 <= size || row < size; row += 16)
        {
            int rowLen = Math.Min(16, size - row);
            var hex = new System.Text.StringBuilder();
            for (int i = 0; i < rowLen; i++)
                hex.Append(bytes[row + i].ToString("X2")).Append(' ');

            var ints = new System.Text.StringBuilder();
            for (int i = 0; i + 4 <= rowLen; i += 4)
                ints.Append(BitConverter.ToInt32(bytes, row + i)).Append(' ');

            AppendLog($"  +0x{row:X4} (0x{addr + row:X}): {hex.ToString().PadRight(48)} | {ints}");
        }

        ShowActionResult(true, $"Dumped 0x{size:X} bytes @ 0x{addr:X} - see Log tab");
    }

    /// <summary>Read-only: walks CPetRooms::_glyphs' linked list the same way GameActions.FindGlyphByRoomFlags
    /// does and logs every node found, to sanity-check the traversal independently of any specific target value.</summary>
    private void DoListRoomGlyphs()
    {
        if (!_mem.IsAttached)
        {
            ShowActionResult(false, "Not attached");
            return;
        }
        if (_currentInventoryRoom is not { } petControlAddr)
        {
            ShowActionResult(false, "CPetControl address not resolved yet (wait for inventory to resolve)");
            return;
        }

        AppendLog($"DEBUG: _currentInventoryRoom (petControlAddr) = 0x{petControlAddr:X}");
        long glyphsAddr = petControlAddr + GameOffsets.PetRoomsOffset + GameOffsets.PetRoomsGlyphsOffset;
        long sentinel = glyphsAddr + 8;
        AppendLog($"DEBUG: walking _glyphs @ 0x{glyphsAddr:X} (sentinel=0x{sentinel:X})");

        long? head = _mem.ReadInt64(glyphsAddr + 0x10);
        AppendLog($"DEBUG: head (glyphs+0x10) = {(head is null ? "READ FAILED" : $"0x{head.Value:X}")}");
        if (head is null)
        {
            ShowActionResult(false, "Failed to read _glyphs head pointer");
            return;
        }

        int count = 0;
        long? current = head;
        for (int i = 0; i < 64 && current is { } node; i++)
        {
            if (node == sentinel)
            {
                AppendLog($"  [{i}] node=0x{node:X} == sentinel - end of list");
                break;
            }
            if (node == 0)
            {
                AppendLog($"  [{i}] node=0x0 - null, stopping (unexpected before sentinel)");
                break;
            }

            long? glyphAddr = _mem.ReadInt64(node + 0x10);
            long? next = _mem.ReadInt64(node + 0x08);

            if (glyphAddr is not { } addr || addr == 0)
            {
                AppendLog($"  [{i}] node=0x{node:X} -> glyph ptr READ FAILED or null, next={(next is null ? "?" : $"0x{next.Value:X}")}");
                current = next;
                continue;
            }

            uint? roomFlags = (uint?)_mem.ReadInt32(addr + GameOffsets.PetRoomsGlyphRoomFlagsOffset);
            int? mode = _mem.ReadInt32(addr + GameOffsets.PetRoomsGlyphModeOffset);
            string decoded = roomFlags is { } rf && !RoomFlags.IsNamedRoom(rf)
                ? RoomFlags.Decode(rf).ToString()
                : "(named/unset)";
            AppendLog($"  [{i}] node=0x{node:X} glyph=0x{addr:X} roomFlags={(roomFlags is null ? "?" : $"0x{roomFlags.Value:X}")} {decoded} mode={mode}");

            count++;
            current = next;
        }

        ShowActionResult(true, $"Listed {count} glyph(s) - see Log tab");
    }

    /// <summary>One-off cleanup for saves already corrupted by the ghost-fuse bug: clears _itemName on any
    /// socket still (mis)recorded as holding BeamBridge or ChickenBridge.</summary>
    private void DoClearGhostFuseSockets()
    {
        if (_currentProject is not { } project)
        {
            ShowActionResult(false, "Project not resolved yet");
            return;
        }

        if (GameState.FindFuseBoxView(_mem, project) is not { } fuseBoxView)
        {
            ShowActionResult(false, "Could not resolve the Fuse Box view [37,12,1]");
            return;
        }

        int cleared = 0;
        foreach (string fuseName in new[] { "BeamBridge", "ChickenBridge" })
        {
            foreach (long shipSetting in GameState.FindShipSettingsInstalledWith(_mem, project, fuseName))
            {
                bool ok = GameActions.ResetShipSetting(_mem, shipSetting, fuseBoxView);
                AppendLog($"DEBUG: reset CShipSetting 0x{shipSetting:X} (was '{fuseName}'): {(ok ? "OK" : "FAILED")}");
                if (ok)
                    cleared++;
            }
        }

        ShowActionResult(true, $"Cleared {cleared} stale socket(s) - see Log tab");
    }
}
