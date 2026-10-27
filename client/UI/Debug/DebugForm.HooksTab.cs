namespace StarshipTitanicAp;

public sealed partial class DebugForm
{
    // --- Hooks tab ---

    /// <summary>One section of the Hooks tab: a toggle button and status label over one static hook class.</summary>
    private sealed class HookRow
    {
        public required string Title { get; init; }
        /// <summary>Used in action results, e.g. "Install {ActionName}".</summary>
        public required string ActionName { get; init; }
        public required string NotInstalledText { get; init; }
        public Func<string> InstalledText { get; init; } = () => "Installed";
        public required Func<bool> IsInstalled { get; init; }
        public required Func<MemoryReader, bool> Install { get; init; }
        public required Func<MemoryReader, bool> Uninstall { get; init; }

        public Button Toggle { get; } = new() { Text = "Install", Width = 100 };
        public Label Status { get; } = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    }

    private readonly HookRow[] _hookRows =
    {
        new()
        {
            Title = "PET Talk Command Hook", ActionName = "PET command hook", NotInstalledText = "Hook not installed",
            InstalledText = () => $"Installed. Stub @ 0x{TextCommandHook.StubAddress:X}, mailbox @ 0x{TextCommandHook.MailboxAddress:X}",
            IsInstalled = () => TextCommandHook.IsInstalled, Install = TextCommandHook.Install, Uninstall = TextCommandHook.Uninstall,
        },
        new()
        {
            Title = "Class Upgrade Lock", ActionName = "class upgrade lock", NotInstalledText = "Lock not installed",
            IsInstalled = () => ClassUpgradeHook.IsInstalled, Install = ClassUpgradeHook.Install, Uninstall = ClassUpgradeHook.Uninstall,
        },
        new()
        {
            Title = "Maitre'D Table Lock", ActionName = "Maitre'D table lock", NotInstalledText = "Lock not installed",
            IsInstalled = () => MaitreDHook.IsInstalled, Install = MaitreDHook.Install, Uninstall = MaitreDHook.Uninstall,
        },
        new()
        {
            Title = "Broken Elevator Eye Gate", ActionName = "broken-elevator Eye gate", NotInstalledText = "Gate not installed",
            IsInstalled = () => GetLiftEye2GateHook.IsInstalled, Install = GetLiftEye2GateHook.Install, Uninstall = GetLiftEye2GateHook.Uninstall,
        },
        new()
        {
            Title = "Room Assignment Lock", ActionName = "room assignment lock", NotInstalledText = "Lock not installed",
            IsInstalled = () => RoomAssignHook.IsInstalled, Install = RoomAssignHook.Install, Uninstall = RoomAssignHook.Uninstall,
        },
        new()
        {
            Title = "Room Key Gate", ActionName = "room key gate", NotInstalledText = "Gate not installed",
            IsInstalled = () => RoomGateHook.IsInstalled, Install = RoomGateHook.Install, Uninstall = RoomGateHook.Uninstall,
        },
    };

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

        foreach (HookRow hook in _hookRows)
        {
            if (layout.Controls.Count > 0)
                layout.Controls.Add(Spacer());
            layout.Controls.Add(SectionLabel(hook.Title));
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            row.Controls.Add(hook.Toggle);
            row.Controls.Add(hook.Status);
            layout.Controls.Add(row);

            hook.Toggle.Click += (_, _) => DoToggleHook(hook);
        }
        RefreshHookControls();

        page.Controls.Add(layout);
        return page;
    }

    private void DoToggleHook(HookRow hook)
    {
        if (!_session.Memory.IsAttached)
        {
            ShowActionResult(false, "Not attached");
            return;
        }

        bool uninstalling = hook.IsInstalled();
        bool ok = uninstalling ? hook.Uninstall(_session.Memory) : hook.Install(_session.Memory);
        RefreshHookControls();
        ShowActionResult(ok, $"{(uninstalling ? "Uninstall" : "Install")} {hook.ActionName}");
    }

    /// <summary>Syncs every hook's toggle and status label with whether it's actually installed.</summary>
    private void RefreshHookControls()
    {
        foreach (HookRow hook in _hookRows)
        {
            bool installed = hook.IsInstalled();
            hook.Toggle.Text = installed ? "Uninstall" : "Install";
            hook.Status.Text = installed ? hook.InstalledText() : hook.NotInstalledText;
        }
    }
}
