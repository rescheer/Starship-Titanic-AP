namespace StarshipTitanicAp;

public sealed partial class MainForm
{
    // --- Hooks tab ---
    private readonly Button _btnToggleHook = new() { Text = "Install", Width = 100 };
    private readonly Label _lblHookStatus = new() { Text = "Hook not installed", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Button _btnToggleClassLockHook = new() { Text = "Install", Width = 100 };
    private readonly Label _lblClassLockHookStatus = new() { Text = "Lock not installed", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Button _btnToggleMaitreDHook = new() { Text = "Install", Width = 100 };
    private readonly Label _lblMaitreDHookStatus = new() { Text = "Lock not installed", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Button _btnToggleGetLiftEye2GateHook = new() { Text = "Install", Width = 100 };
    private readonly Label _lblGetLiftEye2GateHookStatus = new() { Text = "Gate not installed", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Button _btnToggleRoomAssignHook = new() { Text = "Install", Width = 100 };
    private readonly Label _lblRoomAssignHookStatus = new() { Text = "Lock not installed", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Button _btnToggleRoomGateHook = new() { Text = "Install", Width = 100 };
    private readonly Label _lblRoomGateHookStatus = new() { Text = "Gate not installed", AutoSize = true, Anchor = AnchorStyles.Left };

    private TabPage BuildHooksTab()
    {
        var page = new TabPage("Hooks");
        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(10),
            AutoScroll = true,
        };

        AddHookSection(layout, "PET Talk Command Hook", _btnToggleHook, _lblHookStatus);
        AddHookSection(layout, "Class Upgrade Lock", _btnToggleClassLockHook, _lblClassLockHookStatus);
        AddHookSection(layout, "Maitre'D Table Lock", _btnToggleMaitreDHook, _lblMaitreDHookStatus);
        AddHookSection(layout, "Broken Elevator Eye Gate", _btnToggleGetLiftEye2GateHook, _lblGetLiftEye2GateHookStatus);
        AddHookSection(layout, "Room Assignment Lock", _btnToggleRoomAssignHook, _lblRoomAssignHookStatus);
        AddHookSection(layout, "Room Key Gate", _btnToggleRoomGateHook, _lblRoomGateHookStatus);

        page.Controls.Add(layout);
        return page;
    }

    private static void AddHookSection(FlowLayoutPanel layout, string title, Button toggle, Label status)
    {
        if (layout.Controls.Count > 0)
            layout.Controls.Add(Spacer());
        layout.Controls.Add(SectionLabel(title));
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.Add(toggle);
        row.Controls.Add(status);
        layout.Controls.Add(row);
    }

    private static void SetHookToggleState(Button toggle, bool installed) =>
        toggle.Text = installed ? "Uninstall" : "Install";

    /// <summary>Resets every hook's status label and toggle button to "not installed" (used on detach).</summary>
    private void ResetHookControls()
    {
        _lblHookStatus.Text = "Hook not installed";
        _lblClassLockHookStatus.Text = "Lock not installed";
        _lblMaitreDHookStatus.Text = "Lock not installed";
        _lblGetLiftEye2GateHookStatus.Text = "Gate not installed";
        _lblRoomAssignHookStatus.Text = "Lock not installed";
        _lblRoomGateHookStatus.Text = "Gate not installed";
        foreach (var toggle in new[]
        {
            _btnToggleHook, _btnToggleClassLockHook, _btnToggleMaitreDHook,
            _btnToggleGetLiftEye2GateHook, _btnToggleRoomAssignHook, _btnToggleRoomGateHook,
        })
        {
            SetHookToggleState(toggle, false);
        }
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
            SetHookToggleState(_btnToggleHook, true);
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
            SetHookToggleState(_btnToggleHook, false);
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
            SetHookToggleState(_btnToggleClassLockHook, true);
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
            SetHookToggleState(_btnToggleClassLockHook, false);
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
            SetHookToggleState(_btnToggleMaitreDHook, true);
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
            SetHookToggleState(_btnToggleMaitreDHook, false);
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
            SetHookToggleState(_btnToggleGetLiftEye2GateHook, true);
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
            SetHookToggleState(_btnToggleGetLiftEye2GateHook, false);
        }
        ShowActionResult(ok, "Uninstall broken-elevator Eye gate");
    }

    private void DoInstallRoomGateHook()
    {
        if (!_mem.IsAttached)
        {
            ShowActionResult(false, "Not attached");
            return;
        }

        bool ok = RoomGateHook.Install(_mem);
        if (ok)
        {
            _lblRoomGateHookStatus.Text = "Installed";
            SetHookToggleState(_btnToggleRoomGateHook, true);
        }
        ShowActionResult(ok, "Install room key gate");
    }

    private void DoUninstallRoomGateHook()
    {
        if (!_mem.IsAttached)
        {
            ShowActionResult(false, "Not attached");
            return;
        }

        bool ok = RoomGateHook.Uninstall(_mem);
        if (ok)
        {
            _lblRoomGateHookStatus.Text = "Gate not installed";
            SetHookToggleState(_btnToggleRoomGateHook, false);
        }
        ShowActionResult(ok, "Uninstall room key gate");
    }

    private void DoInstallRoomAssignHook()
    {
        if (!_mem.IsAttached)
        {
            ShowActionResult(false, "Not attached");
            return;
        }

        bool ok = RoomAssignHook.Install(_mem);
        if (ok)
        {
            _lblRoomAssignHookStatus.Text = "Installed";
            SetHookToggleState(_btnToggleRoomAssignHook, true);
        }
        ShowActionResult(ok, "Install room assignment lock");
    }

    private void DoUninstallRoomAssignHook()
    {
        if (!_mem.IsAttached)
        {
            ShowActionResult(false, "Not attached");
            return;
        }

        bool ok = RoomAssignHook.Uninstall(_mem);
        if (ok)
        {
            _lblRoomAssignHookStatus.Text = "Lock not installed";
            SetHookToggleState(_btnToggleRoomAssignHook, false);
        }
        ShowActionResult(ok, "Uninstall room assignment lock");
    }
}
