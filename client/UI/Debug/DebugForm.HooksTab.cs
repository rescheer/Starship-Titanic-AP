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
        /// <summary>Settings shown under the toggle, if the hook has any.</summary>
        public Func<Control>? Extra { get; init; }

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
        new()
        {
            Title = "Elevator/Pellerator Travel Skip", ActionName = "travel skip", NotInstalledText = "Skip not installed",
            InstalledText = () => TravelSkipHook.ReadCounts() is { } c
                ? $"Installed. Lift ranges trimmed: {c.LiftTrimmed}, pellerator trips: {c.PelleratorTrips}, ranges dropped: {c.PelleratorSkipped}"
                : "Installed",
            IsInstalled = () => TravelSkipHook.IsInstalled, Install = TravelSkipHook.Install, Uninstall = TravelSkipHook.Uninstall,
            Extra = BuildTravelSkipControls,
        },
        new()
        {
            Title = "Quick Barbot Freakout", ActionName = "Barbot freakout patch", NotInstalledText = "Patch not installed",
            IsInstalled = () => BarbotVodkaPatch.IsInstalled, Install = BarbotVodkaPatch.Install, Uninstall = BarbotVodkaPatch.Uninstall,
        },
        new()
        {
            Title = "DeskBot Class Hook", ActionName = "DeskBot class hook", NotInstalledText = "Hook not installed",
            InstalledText = () => DeskBotClassHook.ReportedClass is { } c
                ? $"Installed. DeskBot conversation sees {PassengerClassNames.GetName(c)}"
                : "Installed. DeskBot conversation sees the real class",
            IsInstalled = () => DeskBotClassHook.IsInstalled, Install = DeskBotClassHook.Install, Uninstall = DeskBotClassHook.Uninstall,
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
            if (hook.Extra is { } extra)
                layout.Controls.Add(extra());

            hook.Toggle.Click += (_, _) => DoToggleHook(hook);
        }
        RefreshHookControls();
        page.Enter += (_, _) => RefreshHookControls(); // picks up the travel skip's counts

        page.Controls.Add(layout);
        return page;
    }

    /// <summary>Frames kept at the end of each lift range. Applies straight away, installed or not.</summary>
    private static Control BuildTravelSkipControls()
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.Add(new Label { Text = "Lift frames kept:", AutoSize = true, Anchor = AnchorStyles.Left });
        var lift = new NumericUpDown { Minimum = 0, Maximum = 300, Value = TravelSkipHook.LiftKeepFrames, Width = 60 };
        row.Controls.Add(lift);
        row.Controls.Add(new Label { Text = "(15 = 1 s)", AutoSize = true, Anchor = AnchorStyles.Left });

        lift.ValueChanged += (_, _) => TravelSkipHook.SetLiftKeepFrames((int)lift.Value);
        return row;
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
