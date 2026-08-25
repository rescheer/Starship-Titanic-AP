namespace StarshipTitanicAp;

public sealed class MainForm : Form
{
    private const string ProcessName = "scummvm";
    private const int RoomNodeViewIntervalMs = 50;   // ~20Hz
    private const int InventoryIntervalTicks = 20;   // every 20 * 50ms = 1s
    private const int MailIntervalTicks = 20;        // every 20 * 50ms = 1s

    private readonly MemoryReader _mem = new();
    private readonly ArchipelagoConnection _apConnection = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = RoomNodeViewIntervalMs };
    private int _tickCount;

    private RoomNodeView? _lastRoomNodeView;
    private int? _lastPassengerClass;
    private List<CarryItemLocation>? _lastInventory;
    private List<GameState.MailItem>? _lastMailItems;

    // Rooms whose "Arrive for the First Time" check has already been sent
    // this run - purely to avoid spamming redundant sends on repeat visits;
    // the server itself is fine with duplicates, so this is just tidiness.
    private readonly HashSet<string> _sentRoomVisitChecks = new(StringComparer.OrdinalIgnoreCase);

    // Last-seen length of _apConnection.GetReceivedItemNames(), so the
    // class-upgrade sync below only does real work when something's
    // actually changed instead of recomputing on every tick.
    private int _lastItemsReceivedCount = -1;

    // Cached "current" values, refreshed every tick, used by other tabs so
    // the user isn't stuck re-running lookups.
    private long? _currentGameManager;
    private long? _currentProject;
    private long? _currentInventoryRoom;
    private long? _currentMailManRoom;
    private string? _currentRoomName;

    // --- Top bar (shared across tabs) ---
    private readonly Button _btnAttach = new() { Text = "Attach", Width = 90 };
    private readonly Button _btnDetach = new() { Text = "Detach", Width = 90, Enabled = false };
    private readonly Label _lblStatus = new() { Text = "Not attached", AutoSize = true };
    private readonly Label _lblActionResult = new() { Text = "", AutoSize = true, ForeColor = Color.DarkSlateGray };

    // --- Archipelago tab ---
    private readonly TextBox _txtApServer = new() { Width = 220, PlaceholderText = "archipelago.gg:38281" };
    private readonly TextBox _txtApSlot = new() { Width = 220, PlaceholderText = "Slot name" };
    private readonly TextBox _txtApPassword = new() { Width = 220, PlaceholderText = "(optional)", UseSystemPasswordChar = true };
    private readonly Button _btnApConnect = new() { Text = "Connect", Width = 100 };
    private readonly Button _btnApDisconnect = new() { Text = "Disconnect", Width = 100, Enabled = false };
    private readonly Label _lblApStatus = new() { Text = "\u25CF Not connected", AutoSize = true, ForeColor = Color.DimGray };
    private readonly Label _lblPendingChecks = new() { Text = "", AutoSize = true, ForeColor = Color.DimGray };
    private readonly Button _btnClearPendingChecks = new() { Text = "Clear Pending Checks", Width = 160, AutoSize = true };

    // --- Live tab ---
    private readonly Label _lblRoomNodeView = new() { Text = "Room: -   Node: -   View: -", AutoSize = true };
    private readonly Label _lblClass = new() { Text = "Class: -", AutoSize = true };

    // --- Items tab ---
    private readonly ListView _lvInventory = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        GridLines = true,
        MultiSelect = false,
    };
    private readonly Button _btnCopyInventoryAddress = new() { Text = "Copy Address", Width = 110, Enabled = false };
    private readonly Button _btnRefreshAllItems = new() { Text = "Refresh", Width = 80 };
    private readonly Button _btnMoveToInventory = new() { Text = "Move to Inventory", Width = 130, Enabled = false };
    private readonly Button _btnMoveToMail = new() { Text = "Move to Mail", Width = 110, Enabled = false };
    private readonly Label _lblAllItemsStatus = new() { Text = "", AutoSize = true, ForeColor = Color.DimGray };

    // --- PET tab (was Actions) ---
    private readonly ComboBox _cmbClass = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly Button _btnSetClass = new() { Text = "Set Class", Width = 100 };
    private readonly TextBox _txtMsgText = new() { Width = 300, PlaceholderText = "free text message" };
    private readonly Button _btnDisplayMessageText = new() { Text = "Display Free Text", Width = 180 };

    // --- Debug tab ---
    private readonly Button _btnMarkAllDirty = new() { Text = "Call markAllDirty() [experimental]", Width = 240 };
    private readonly Button _btnResetPet = new() { Text = "Call CPetControl::reset()", Width = 240 };
    private readonly Button _btnInstallHook = new() { Text = "Install PET Command Hook", Width = 220 };
    private readonly Button _btnUninstallHook = new() { Text = "Uninstall Hook", Width = 220, Enabled = false };
    private readonly Label _lblHookStatus = new() { Text = "Hook not installed", AutoSize = true };
    private readonly Button _btnInstallClassLockHook = new() { Text = "Install Class Upgrade Lock", Width = 220 };
    private readonly Button _btnUninstallClassLockHook = new() { Text = "Uninstall Lock", Width = 220, Enabled = false };
    private readonly Label _lblClassLockHookStatus = new() { Text = "Lock not installed", AutoSize = true };
    private readonly TextBox _txtItemAddr = new() { Width = 150, PlaceholderText = "item address (hex)" };
    private readonly TextBox _txtRoomAddrOverride = new() { Width = 150, PlaceholderText = "room address (hex)" };
    private readonly Button _btnMoveItem = new() { Text = "Move", Width = 70 };

    // --- Addresses tab ---
    private readonly ListView _lvAddresses = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        GridLines = true,
        MultiSelect = false,
    };
    private readonly Button _btnCopyAddress = new() { Text = "Copy Address", Width = 110, Enabled = false };
    private readonly Dictionary<string, ListViewItem> _addressRows = new();

    // --- Mail System tab ---
    private readonly ListView _lvMail = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        GridLines = true,
        MultiSelect = false,
    };
    private readonly Label _lblMailCurrentRoom = new() { Text = "Current room: -", AutoSize = true };
    private readonly Button _btnSetDestToCurrentRoom = new() { Text = "Set Destination to Current Room", Width = 240, Enabled = false };
    private readonly Label _lblMailCount = new() { Text = "", AutoSize = true, ForeColor = Color.DimGray };

    public MainForm()
    {
        Text = AppInfo.TitleBarText;
        Width = 500;
        Height = 600;
        StartPosition = FormStartPosition.CenterScreen;

        BuildLayout();

        ConnectionSettings.Data savedConnection = ConnectionSettings.Load();
        _txtApServer.Text = savedConnection.Server;
        _txtApSlot.Text = savedConnection.Slot;
        _txtApPassword.Text = savedConnection.Password;
        UpdatePendingChecksLabel();

        _btnAttach.Click += (_, _) => AttemptAttach();
        _btnDetach.Click += (_, _) => DoDetach();
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();

        _btnApConnect.Click += (_, _) => DoApConnect();
        _btnApDisconnect.Click += (_, _) => _apConnection.Disconnect();
        _btnClearPendingChecks.Click += (_, _) => DoClearPendingChecks();
        _apConnection.StateChanged += OnApStateChanged;
        _apConnection.MessageReceived += OnApMessageReceived;

        _btnSetClass.Click += (_, _) => DoSetClass();
        _btnMoveItem.Click += (_, _) => DoMoveToCustomAddress();
        _btnMoveToInventory.Click += (_, _) => DoMoveSelectedTo(toMail: false);
        _btnMoveToMail.Click += (_, _) => DoMoveSelectedTo(toMail: true);
        _btnMarkAllDirty.Click += (_, _) => DoMarkAllDirty();
        _btnResetPet.Click += (_, _) => DoResetPetControl();
        _btnInstallHook.Click += (_, _) => DoInstallHook();
        _btnUninstallHook.Click += (_, _) => DoUninstallHook();
        _btnInstallClassLockHook.Click += (_, _) => DoInstallClassLockHook();
        _btnUninstallClassLockHook.Click += (_, _) => DoUninstallClassLockHook();
        _btnDisplayMessageText.Click += (_, _) => DoDisplayMessageText();
        _btnSetDestToCurrentRoom.Click += (_, _) => DoSetMailDestToCurrentRoom();
        _btnCopyInventoryAddress.Click += (_, _) => DoCopyInventoryAddress();
        _btnRefreshAllItems.Click += (_, _) => DoRefreshAllItems();

        _lvMail.SelectedIndexChanged += (_, _) => UpdateMailButtonState();
        _lvInventory.SelectedIndexChanged += (_, _) =>
        {
            bool hasSelection = _lvInventory.SelectedItems.Count > 0;
            _btnCopyInventoryAddress.Enabled = hasSelection;
            _btnMoveToInventory.Enabled = hasSelection;
            _btnMoveToMail.Enabled = hasSelection;
        };
        _lvAddresses.SelectedIndexChanged += (_, _) => _btnCopyAddress.Enabled = _lvAddresses.SelectedItems.Count > 0;
        _btnCopyAddress.Click += (_, _) => DoCopyAddressRow();
    }


    private void BuildLayout()
    {
        var topPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(8),
        };
        topPanel.Controls.Add(_btnAttach);
        topPanel.Controls.Add(_btnDetach);

        var statusPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 26,
            Padding = new Padding(8, 0, 8, 0),
        };
        statusPanel.Controls.Add(_lblStatus);

        var feedbackPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 26,
            Padding = new Padding(8, 0, 8, 0),
        };
        feedbackPanel.Controls.Add(_lblActionResult);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildArchipelagoTab());
        tabs.TabPages.Add(BuildLiveTab());
        tabs.TabPages.Add(BuildItemsTab());
        tabs.TabPages.Add(BuildMailTab());
        tabs.TabPages.Add(BuildPetTab());
        tabs.TabPages.Add(BuildAddressesTab());
        tabs.TabPages.Add(BuildDebugTab());

        Controls.Add(tabs);
        Controls.Add(feedbackPanel);
        Controls.Add(statusPanel);
        Controls.Add(topPanel);
    }

    private TabPage BuildArchipelagoTab()
    {
        var page = new TabPage("Archipelago");
        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(10),
            AutoScroll = true,
        };

        layout.Controls.Add(SectionLabel("Server Connection"));

        var serverRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        serverRow.Controls.Add(new Label { Text = "Server:", AutoSize = true, Margin = new Padding(0, 6, 4, 0), Width = 70 });
        serverRow.Controls.Add(_txtApServer);
        layout.Controls.Add(serverRow);

        var slotRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        slotRow.Controls.Add(new Label { Text = "Slot:", AutoSize = true, Margin = new Padding(0, 6, 4, 0), Width = 70 });
        slotRow.Controls.Add(_txtApSlot);
        layout.Controls.Add(slotRow);

        var passwordRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        passwordRow.Controls.Add(new Label { Text = "Password:", AutoSize = true, Margin = new Padding(0, 6, 4, 0), Width = 70 });
        passwordRow.Controls.Add(_txtApPassword);
        layout.Controls.Add(passwordRow);

        var buttonRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
        buttonRow.Controls.Add(_btnApConnect);
        buttonRow.Controls.Add(_btnApDisconnect);
        layout.Controls.Add(buttonRow);

        layout.Controls.Add(_lblApStatus);

        var pendingRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        pendingRow.Controls.Add(_lblPendingChecks);
        pendingRow.Controls.Add(_btnClearPendingChecks);
        layout.Controls.Add(pendingRow);

        page.Controls.Add(layout);
        return page;
    }

    private void DoApConnect()
    {
        string server = _txtApServer.Text.Trim();
        string slot = _txtApSlot.Text.Trim();
        string password = _txtApPassword.Text;

        SetApInputsEnabled(false);
        _ = _apConnection.ConnectAsync(server, slot, password);
    }

    /// <summary>
    /// Handles ArchipelagoConnection.StateChanged, which can fire from a
    /// background thread - always hop back to the UI thread before
    /// touching any control.
    /// </summary>
    private void OnApStateChanged(ApConnectionState state, string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => OnApStateChanged(state, message)));
            return;
        }

        switch (state)
        {
            case ApConnectionState.Connecting:
                _lblApStatus.Text = $"\u25CF {message}";
                _lblApStatus.ForeColor = Color.DarkOrange;
                break;
            case ApConnectionState.Connected:
                _lblApStatus.Text = $"\u25CF {message}";
                _lblApStatus.ForeColor = Color.DarkGreen;
                SetApInputsEnabled(false);
                _btnApDisconnect.Enabled = true;
                _sentRoomVisitChecks.Clear(); // new server session - resend visit checks it hasn't seen yet
                _lastItemsReceivedCount = -1; // force a class-upgrade resync next tick, even if the count happens to match
                if (_currentRoomName is not null)
                    TrySendRoomVisitCheck(_currentRoomName); // already standing in this room - won't get a "room changed" tick
                UpdatePendingChecksLabel();
                ConnectionSettings.Save(new ConnectionSettings.Data
                {
                    Server = _txtApServer.Text.Trim(),
                    Slot = _txtApSlot.Text.Trim(),
                    Password = _txtApPassword.Text,
                });
                break;
            case ApConnectionState.ConnectionFailed:
                _lblApStatus.Text = $"\u25CF Connection failed: {message}";
                _lblApStatus.ForeColor = Color.DarkRed;
                SetApInputsEnabled(true);
                break;
            case ApConnectionState.Disconnected:
            default:
                _lblApStatus.Text = $"\u25CF {message}";
                _lblApStatus.ForeColor = Color.DimGray;
                SetApInputsEnabled(true);
                break;
        }
    }

    /// <summary>
    /// Handles ArchipelagoConnection.MessageReceived (item sends/receives,
    /// hints, chat, join/leave, etc. - anything the AP client library
    /// surfaces via session.MessageLog). Can fire from a background thread,
    /// same as OnApStateChanged.
    /// </summary>
    private void OnApMessageReceived(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => OnApMessageReceived(text)));
            return;
        }

        if (!_mem.IsAttached || _currentInventoryRoom is null)
            return; // nothing to display to yet - message is simply dropped

        bool ok = GameActions.DisplayPetMessageText(_mem, _currentInventoryRoom.Value, text, 0);
        if (!ok)
            ShowActionResult(false, $"Failed to display AP message: \"{text}\"");
    }

    private void UpdatePendingChecksLabel()
    {
        int count = _apConnection.PendingCheckCount;
        _lblPendingChecks.Text = count == 0
            ? ""
            : $"{count} location check{(count == 1 ? "" : "s")} queued, waiting to reconnect";
    }

    private void DoClearPendingChecks()
    {
        int count = _apConnection.PendingCheckCount;
        _apConnection.ClearPendingChecks();
        UpdatePendingChecksLabel();
        ShowActionResult(true, count == 0
            ? "No pending checks to clear"
            : $"Cleared {count} pending check{(count == 1 ? "" : "s")}");
    }

    private void SetApInputsEnabled(bool enabled)
    {
        _txtApServer.Enabled = enabled;
        _txtApSlot.Enabled = enabled;
        _txtApPassword.Enabled = enabled;
        _btnApConnect.Enabled = enabled;
        _btnApDisconnect.Enabled = !enabled;
    }

    private TabPage BuildLiveTab()
    {
        var page = new TabPage("Live");

        var rnvPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(8, 8, 8, 0) };
        rnvPanel.Controls.Add(_lblRoomNodeView);

        var classPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(8, 0, 8, 0) };
        classPanel.Controls.Add(_lblClass);

        page.Controls.Add(classPanel);
        page.Controls.Add(rnvPanel);
        return page;
    }

    private TabPage BuildItemsTab()
    {
        var page = new TabPage("Items");

        var listLabel = new Label { Text = "All items:", Dock = DockStyle.Top, Height = 24, Padding = new Padding(8, 4, 0, 0) };

        var listButtonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(8, 4, 8, 4) };
        listButtonPanel.Controls.Add(_btnCopyInventoryAddress);
        listButtonPanel.Controls.Add(_btnRefreshAllItems);
        listButtonPanel.Controls.Add(_btnMoveToInventory);
        listButtonPanel.Controls.Add(_btnMoveToMail);
        listButtonPanel.Controls.Add(_lblAllItemsStatus);

        _lvInventory.Columns.Add("Item", 150);
        _lvInventory.Columns.Add("Location", 150);
        _lvInventory.Columns.Add("Address", 120);

        page.Controls.Add(_lvInventory);
        page.Controls.Add(listButtonPanel);
        page.Controls.Add(listLabel);
        return page;
    }

    private TabPage BuildPetTab()
    {
        var page = new TabPage("PET");
        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(10),
            AutoScroll = true,
        };

        _cmbClass.Items.AddRange(new object[] { "1 - First Class", "2 - Second Class", "3 - Third Class", "4 - No Class" });
        _cmbClass.SelectedIndex = 0;

        layout.Controls.Add(SectionLabel("Passenger Class"));
        var classRow = new FlowLayoutPanel { AutoSize = true };
        classRow.Controls.Add(_cmbClass);
        classRow.Controls.Add(_btnSetClass);
        layout.Controls.Add(classRow);
        layout.Controls.Add(HelpLabel("Gates room access immediately and updates the PET color right away (writes class, then calls reset() + markAllDirty())."));

        layout.Controls.Add(Spacer());
        layout.Controls.Add(SectionLabel("PET Free Text Message"));
        layout.Controls.Add(_txtMsgText);
        layout.Controls.Add(_btnDisplayMessageText);
        layout.Controls.Add(HelpLabel("Arbitrary text via CPetControl::displayMessage(const CString&, int) - confirmed working."));

        layout.Controls.Add(Spacer());
        layout.Controls.Add(SectionLabel("PET Talk Command Hook [experimental]"));
        var hookRow = new FlowLayoutPanel { AutoSize = true };
        hookRow.Controls.Add(_btnInstallHook);
        hookRow.Controls.Add(_btnUninstallHook);
        layout.Controls.Add(hookRow);
        layout.Controls.Add(_lblHookStatus);
        layout.Controls.Add(HelpLabel("Intercepts textLineEntered(). Lines starting with '!' are captured here and blocked from reaching TrueTalk; anything else behaves normally. Installs automatically on attach. Captured commands appear in the feedback line at the top of the window. Verify the stub in x64dbg before relying on this - genuinely experimental compared to the rest of this app."));

        layout.Controls.Add(Spacer());
        layout.Controls.Add(SectionLabel("Class Upgrade Lock [experimental]"));
        var classLockRow = new FlowLayoutPanel { AutoSize = true };
        classLockRow.Controls.Add(_btnInstallClassLockHook);
        classLockRow.Controls.Add(_btnUninstallClassLockHook);
        layout.Controls.Add(classLockRow);
        layout.Controls.Add(_lblClassLockHookStatus);
        layout.Controls.Add(HelpLabel("Blocks CGameObject::setPassengerClass() so the DeskBot can't change PassengerClass on its own, and reports the attempted class as its matching location check ('DeskBot - Second/First Class Upgrade') - the actual upgrade still only ever applies from receiving the matching item over the multiworld. Installs automatically on attach. Try a legitimate DeskBot upgrade with the lock installed and confirm PassengerClass on the Live tab doesn't move, and that the attempt shows up in the feedback line."));

        page.Controls.Add(layout);
        return page;
    }

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

        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildAddressesTab()
    {
        var page = new TabPage("Addresses");

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(8, 4, 8, 4) };
        buttonPanel.Controls.Add(_btnCopyAddress);

        _lvAddresses.Columns.Add("Field", 220);
        _lvAddresses.Columns.Add("Address", 180);

        AddAddressRow("Module base");
        AddAddressRow("gameManager");
        AddAddressRow("_project");
        AddAddressRow("Player Inventory (CPetControl)");
        AddAddressRow("Mail Inventory (CMailMan)");
        AddAddressRow("PassengerClass field");

        page.Controls.Add(_lvAddresses);
        page.Controls.Add(buttonPanel);
        return page;
    }

    private void AddAddressRow(string label)
    {
        var lvi = new ListViewItem(label) { Tag = (string?)null };
        lvi.SubItems.Add("-");
        _addressRows[label] = lvi;
        _lvAddresses.Items.Add(lvi);
    }

    private void SetAddressRow(string label, long? value)
    {
        if (!_addressRows.TryGetValue(label, out ListViewItem? lvi))
            return;

        string text = value is null ? "-" : $"0x{value.Value:X}";
        lvi.SubItems[1].Text = text;
        lvi.Tag = value is null ? null : text;
    }

    private TabPage BuildMailTab()
    {
        var page = new TabPage("Mail System");

        var topRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(8, 8, 8, 0) };
        topRow.Controls.Add(_lblMailCurrentRoom);

        var actionRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(8, 0, 8, 0) };
        actionRow.Controls.Add(_btnSetDestToCurrentRoom);

        var listLabel = new Label { Text = "Items currently in the mail system:", Dock = DockStyle.Top, Height = 24, Padding = new Padding(8, 4, 0, 0) };

        var countRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 24, Padding = new Padding(8, 0, 8, 4) };
        countRow.Controls.Add(_lblMailCount);

        _lvMail.Columns.Add("Item", 160);
        _lvMail.Columns.Add("Status", 100);
        _lvMail.Columns.Add("Destination / Location", 150);
        _lvMail.Columns.Add("Source", 50);

        page.Controls.Add(_lvMail);
        page.Controls.Add(countRow);
        page.Controls.Add(listLabel);
        page.Controls.Add(actionRow);
        page.Controls.Add(topRow);
        return page;
    }

    private static Label SectionLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font(Control.DefaultFont, FontStyle.Bold),
        Margin = new Padding(0, 6, 0, 4),
    };

    private static Label HelpLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(380, 0),
        ForeColor = Color.DimGray,
        Margin = new Padding(0, 0, 0, 4),
    };

    private static Panel Spacer() => new() { Height = 10, Width = 1 };

    // ------------------------------------------------------------------
    // Attach / Detach
    // ------------------------------------------------------------------

    private void AttemptAttach()
    {
        bool ok = _mem.Attach(ProcessName);
        if (ok)
        {
            _lblStatus.Text = $"Attached (PID {_mem.ProcessId}, base 0x{_mem.ModuleBase:X})";
            SetAddressRow("Module base", _mem.ModuleBase);
            _btnAttach.Enabled = false;
            _btnDetach.Enabled = true;
            ResetCachedState();
            DoInstallHook(); // auto-install the PET talk command hook on attach
            DoInstallClassLockHook(); // auto-install the class upgrade lock on attach (no-ops until GameOffsets.SetPassengerClassFunc is filled in)

            // Resolve the project chain synchronously (rather than waiting
            // for the next tick) so the Items tab can refresh right away.
            // If the game is still at a menu/loading screen this won't
            // resolve yet - that's fine, normal tick polling picks it up
            // once it's ready, same as any other cold attach.
            long? gameManager = GameState.ResolveGameManager(_mem);
            _currentGameManager = gameManager;
            if (gameManager is not null)
            {
                _currentProject = GameState.ResolveProject(_mem, gameManager.Value);
                if (_currentProject is not null)
                    DoRefreshAllItems();
            }
        }
        else
        {
            _lblStatus.Text = $"Could not find/attach to \"{ProcessName}\" - is the game running?";
        }
    }

    private void DoDetach()
    {
        if (TextCommandHook.IsInstalled)
        {
            TextCommandHook.Uninstall(_mem); // restore original bytes before losing the process handle
        }
        if (ClassUpgradeHook.IsInstalled)
        {
            ClassUpgradeHook.Uninstall(_mem); // restore original bytes before losing the process handle
        }

        _mem.Detach();
        _lblStatus.Text = "Not attached";
        _lblRoomNodeView.Text = "Room: -   Node: -   View: -";
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
            "Player Inventory (CPetControl)", "Mail Inventory (CMailMan)", "PassengerClass field"
        })
        {
            SetAddressRow(key, null);
        }
    }

    private void ResetCachedState()
    {
        _lastRoomNodeView = null;
        _lastPassengerClass = null;
        _lastInventory = null;
        _lastMailItems = null;
        _currentGameManager = null;
        _currentProject = null;
        _currentInventoryRoom = null;
        _currentMailManRoom = null;
        _currentRoomName = null;
        // Tool-placed tracking no longer needs anything cleared here - it
        // now lives in each item's own _destRoomFlags sentinel (see
        // GameActions.MarkItemAsToolPlaced), so there's no local cache to
        // go stale across detach/reattach or a game restart.
    }

    // ------------------------------------------------------------------
    // Live polling
    // ------------------------------------------------------------------

    private void OnTick()
    {
        if (!_mem.IsAttached)
            return;

        _tickCount++;

        long? gameManager = GameState.ResolveGameManager(_mem);
        _currentGameManager = gameManager;

        if (gameManager is null)
        {
            _lblRoomNodeView.Text = "Room: -   Node: -   View: -   (menu / loading)";
            SetAddressRow("gameManager", null);
            return;
        }

        SetAddressRow("gameManager", gameManager.Value);
        SetAddressRow("PassengerClass field", gameManager.Value + GameOffsets.PassengerClass);

        // --- Room / node / view: every tick ---
        RoomNodeView? rnv = GameState.ReadRoomNodeView(_mem, gameManager.Value);
        if (rnv is not null && rnv != _lastRoomNodeView)
        {
            int? previousRoom = _lastRoomNodeView?.Room;
            _lastRoomNodeView = rnv;
            string roomName = RoomNames.GetName(rnv.Value.Room);
            _currentRoomName = roomName;
            _lblRoomNodeView.Text = $"Room: {rnv.Value.Room} ({roomName})   Node: {rnv.Value.Node}   View: {rnv.Value.View}";
            UpdateMailCurrentRoomLabel();

            // Only on an actual room change (not just node/view movement
            // within the same room) - and only once per arrival.
            if (rnv.Value.Room != previousRoom)
            {
                DeliverQueuedMailAtStation(roomName);
                TrySendRoomVisitCheck(roomName);
            }
        }

        // --- Passenger class: every tick, cheap single read ---
        int? passengerClass = GameState.ReadPassengerClass(_mem, gameManager.Value);
        if (passengerClass is not null && passengerClass != _lastPassengerClass)
        {
            _lastPassengerClass = passengerClass;
            _lblClass.Text = $"Class: {PassengerClassNames.GetName(passengerClass.Value)}";
        }

        // --- Class upgrade from received AP items: cheap count check
        // first, only does real work when something's actually changed ---
        SyncPassengerClassFromItems(gameManager.Value);

        // --- Inventory / Mail: only every N ticks (heavier tree walks) ---
        if (_tickCount % InventoryIntervalTicks == 0)
        {
            UpdateInventory(gameManager.Value);
        }
        if (_tickCount % MailIntervalTicks == 0)
        {
            UpdateMail(gameManager.Value);
        }

        // --- PET command hook: poll every tick, cheap (2 small reads) ---
        if (TextCommandHook.IsInstalled)
        {
            string? command = TextCommandHook.PollCommand(_mem);
            if (command is not null)
            {
                if (_apConnection.IsConnected)
                {
                    // SendCommand hands the send off to a background task
                    // (see ArchipelagoConnection.TrySendAsync) - this
                    // confirms it was dispatched, not that the server has
                    // it yet. A genuinely dead connection surfaces via the
                    // AP status label going to Disconnected, not here.
                    _apConnection.SendCommand(command);
                    ShowCapturedCommand($"{command} (sent to server)");
                }
                else
                {
                    ShowCapturedCommand($"{command} (not connected - not sent)");
                }
            }
        }

        // --- Class upgrade lock hook: poll every tick, cheap (1 small
        // read). A blocked DeskBot upgrade attempt still needs to be
        // reported to AP as its own location check - SendLocationCheck
        // queues automatically if we're not connected. ---
        if (ClassUpgradeHook.IsInstalled)
        {
            int? attemptedClass = ClassUpgradeHook.PollAttemptedClass(_mem);
            if (attemptedClass is not null)
            {
                if (LocationChecks.TryGetClassUpgradeLocationId(attemptedClass.Value, out long locationId))
                {
                    bool handedOff = _apConnection.SendLocationCheck(locationId);
                    ShowActionResult(handedOff, handedOff
                        ? $"DeskBot upgrade attempt ({PassengerClassNames.GetName(attemptedClass.Value)}) -> location {locationId}"
                        : $"DeskBot upgrade attempt ({PassengerClassNames.GetName(attemptedClass.Value)}) queued (offline) -> location {locationId}");
                }
                else
                {
                    ShowActionResult(false, $"DeskBot upgrade attempt for unrecognized class {attemptedClass.Value}");
                }
            }
        }
    }

    /// <summary>
    /// Applies the class upgrade implied by AP items received so far, if
    /// any (see ClassUpgradeTracker). Cheap-checks the received-item count
    /// first so this only does real work on ticks where something's
    /// actually new - safe to call unconditionally every tick otherwise.
    /// Requires the class-upgrade lock hook to already be blocking the
    /// vanilla DeskBot trigger, or this and vanilla gameplay will fight
    /// over the same field.
    /// </summary>
    private void SyncPassengerClassFromItems(long gameManager)
    {
        IReadOnlyDictionary<string, object>? slotData = _apConnection.SlotData;
        if (slotData is null)
            return;
        if (_currentInventoryRoom is null)
            return; // need the PET control address for SetPassengerClassFull

        string[] receivedItems = _apConnection.GetReceivedItemNames();
        if (receivedItems.Length == _lastItemsReceivedCount)
            return; // nothing new since last tick
        _lastItemsReceivedCount = receivedItems.Length;

        int? targetClass = ClassUpgradeTracker.ComputeClass(receivedItems, slotData);
        if (targetClass is null)
            return; // not enough upgrade items yet - leave the current class alone

        int? currentClass = GameState.ReadPassengerClass(_mem, gameManager);
        if (currentClass == targetClass)
            return; // already there

        bool ok = GameActions.SetPassengerClassFull(_mem, gameManager, _currentInventoryRoom.Value, targetClass.Value);
        ShowActionResult(ok, $"Class upgrade from items: {PassengerClassNames.GetName(targetClass.Value)}");
    }

    /// <summary>
    /// Sends the AP location check for a room's "Arrive for the First
    /// Time" location, if the room has a known mapping (see
    /// LocationChecks.cs). Skips silently for an unmapped room. If we're
    /// not connected (or the send fails), ArchipelagoConnection queues it
    /// automatically and retries on next connect - so this always marks
    /// the room as handled either way, it just reports whether it went
    /// out immediately or got queued.
    /// </summary>
    private void TrySendRoomVisitCheck(string roomName)
    {
        if (!LocationChecks.TryGetLocationId(roomName, out long locationId))
            return; // no known mapping for this room - see LocationChecks.cs

        if (!_sentRoomVisitChecks.Add(roomName))
            return; // already sent (or queued) this run

        bool handedOff = _apConnection.SendLocationCheck(locationId);
        ShowActionResult(handedOff, handedOff
            ? $"Location check: {roomName} -> {locationId}"
            : $"Location check queued (offline): {roomName} -> {locationId}");
        UpdatePendingChecksLabel();
    }

    private void UpdateInventory(long gameManager)
    {
        long? project = GameState.ResolveProject(_mem, gameManager);
        _currentProject = project;
        SetAddressRow("_project", project);

        if (project is null)
            return;

        long? inventoryRoom = GameState.FindInventoryRoom(_mem, project.Value);
        _currentInventoryRoom = inventoryRoom;
        SetAddressRow("Player Inventory (CPetControl)", inventoryRoom);
    }

    private void UpdateMail(long gameManager)
    {
        long? project = _currentProject;
        if (project is null)
            return;

        long? mailManRoom = GameState.FindMailManRoom(_mem, project.Value);
        _currentMailManRoom = mailManRoom;
        SetAddressRow("Mail Inventory (CMailMan)", mailManRoom);

        if (mailManRoom is null)
        {
            _lblMailCount.Text = "CMailMan not resolved";
            return;
        }

        List<GameState.MailItem> items = GameState.ReadMailItems(_mem, mailManRoom.Value);

        bool changed = _lastMailItems is null
            || items.Count != _lastMailItems.Count
            || !items.Select(i => (i.Name, i.IsPendingMail, i.DestRoomFlags, i.RoomFlags))
                     .SequenceEqual(_lastMailItems.Select(i => (i.Name, i.IsPendingMail, i.DestRoomFlags, i.RoomFlags)));

        if (!changed)
            return;

        _lastMailItems = items;
        _lvMail.Items.Clear();
        foreach (GameState.MailItem item in items)
        {
            string status;
            string dest;

            if (item.RoomFlags != 0)
            {
                status = "Delivered / waiting";
                dest = ChevronCodes.TryGetRoomName(item.RoomFlags) is { } roomName
                    ? roomName
                    : $"(unknown code 0x{item.RoomFlags:X})";
            }
            else if (item.IsPendingMail)
            {
                status = "In Tray";
                dest = ChevronCodes.TryGetRoomName(item.DestRoomFlags) is { } roomName
                    ? $"-> {roomName}"
                    : $"-> (unknown code 0x{item.DestRoomFlags:X})";
            }
            else
            {
                status = "(unknown state)";
                dest = "-";
            }

            var lvi = new ListViewItem(item.Name) { Tag = item.Address };
            lvi.SubItems.Add(status);
            lvi.SubItems.Add(dest);
            lvi.SubItems.Add(item.DestRoomFlags == GameOffsets.ToolPlacedSentinel ? "Tool" : "Game");
            _lvMail.Items.Add(lvi);
        }

        _lblMailCount.Text = $"{items.Count} item(s) in the mail system";
        UpdateMailButtonState();
    }

    /// <summary>
    /// Called on every real room change (not node/view movement within a
    /// room). If the new room has a SuccUBus station, every mail item this
    /// app itself placed into the mail system - identified live by the
    /// ToolPlacedSentinel in _destRoomFlags (see
    /// GameActions.MarkItemAsToolPlaced), regardless of its current
    /// delivered/queued status - gets its _roomFlags retargeted to this
    /// room's chevron code. SetItemMailDestination never touches
    /// _destRoomFlags, so the sentinel survives the retarget and the item
    /// stays recognized as ours. Items placed there by normal gameplay
    /// (the real SuccUBus flow) are never touched, since we have no way
    /// to tell the game's own routing was "wrong" - only our own
    /// placements are ours to move.
    /// </summary>
    private void DeliverQueuedMailAtStation(string roomName)
    {
        if (_currentMailManRoom is null)
            return;
        if (!ChevronCodes.TryGetCode(roomName, out uint code))
            return;

        List<GameState.MailItem> items = GameState.ReadMailItems(_mem, _currentMailManRoom.Value);
        int delivered = 0;
        foreach (GameState.MailItem item in items)
        {
            if (item.DestRoomFlags != GameOffsets.ToolPlacedSentinel)
                continue;

            if (GameActions.SetItemMailDestination(_mem, item.Address, code))
                delivered++;
        }

        if (delivered > 0)
        {
            _lastMailItems = null; // force the Mail tab to refresh next tick
            ShowActionResult(true, $"Updated {delivered} tool-placed item(s) to {roomName} station");
        }
    }

    private void UpdateMailCurrentRoomLabel()
    {
        if (_currentRoomName is null)
        {
            _lblMailCurrentRoom.Text = "Current room: -";
        }
        else if (ChevronCodes.HasStation(_currentRoomName))
        {
            _lblMailCurrentRoom.Text = $"Current room: {_currentRoomName} (has a SuccUBus station)";
        }
        else
        {
            _lblMailCurrentRoom.Text = $"Current room: {_currentRoomName} (no SuccUBus station here)";
        }
        UpdateMailButtonState();
    }

    private void UpdateMailButtonState()
    {
        bool hasSelection = _lvMail.SelectedItems.Count > 0;
        bool hasStationHere = _currentRoomName is not null && ChevronCodes.HasStation(_currentRoomName);
        _btnSetDestToCurrentRoom.Enabled = hasSelection && hasStationHere && _mem.IsAttached;
    }

    // ------------------------------------------------------------------
    // Actions
    // ------------------------------------------------------------------

    private void DoSetClass()
    {
        if (!RequireAttachedAndResolved(out long gameManager))
            return;

        int newClass = _cmbClass.SelectedIndex + 1; // combo items are 1-indexed in display order

        if (_currentInventoryRoom is null)
        {
            bool wroteOnly = GameActions.SetPassengerClass(_mem, gameManager, newClass);
            ShowActionResult(wroteOnly, $"Set class to {newClass} (CPetControl not resolved yet - color won't refresh immediately)");
            return;
        }

        bool ok = GameActions.SetPassengerClassFull(_mem, gameManager, _currentInventoryRoom.Value, newClass);
        ShowActionResult(ok, $"Set class to {newClass}");
    }

    private void DoMoveSelectedTo(bool toMail)
    {
        if (!RequireAttachedAndResolved(out long gameManager))
            return;

        if (_lvInventory.SelectedItems.Count == 0)
        {
            ShowActionResult(false, "Select an item first");
            return;
        }
        long itemAddr = (long)_lvInventory.SelectedItems[0].Tag!;

        long roomAddr;
        if (toMail)
        {
            if (_currentMailManRoom is null)
            {
                ShowActionResult(false, "Mail system not resolved yet");
                return;
            }
            roomAddr = _currentMailManRoom.Value;
        }
        else
        {
            if (_currentInventoryRoom is null)
            {
                ShowActionResult(false, "Inventory not resolved yet");
                return;
            }
            roomAddr = _currentInventoryRoom.Value;
        }

        MoveItemAndReport(gameManager, itemAddr, roomAddr, isMailDestination: toMail);
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

    /// <summary>
    /// Shared move+refresh+report logic for all three "move item" entry
    /// points (to inventory, to mail, to a custom address). MoveItemSmart
    /// refreshes whichever side of the move - source or destination - is
    /// actually the inventory, so this stays correct whether the item is
    /// entering, leaving, or neither. When the destination is the mail
    /// system, also pairs the move with a real SetItemMailDestination()
    /// call and marks the item as tool-placed via the _destRoomFlags
    /// sentinel (see GameActions.MarkItemAsToolPlaced) - part of the
    /// item's own serialized state, so it survives detach/reattach and
    /// game restarts/save-loads with no external bookkeeping needed. On
    /// success, re-runs the full item list (DoRefreshAllItems already
    /// preserves selection/scroll position) so the Items tab reflects the
    /// move immediately instead of waiting for the next manual refresh.
    /// </summary>
    private void MoveItemAndReport(long gameManager, long itemAddr, long roomAddr, bool isMailDestination)
    {
        bool ok = GameActions.MoveItemSmart(_mem, itemAddr, roomAddr, _currentInventoryRoom, gameManager);
        string message;

        if (ok && isMailDestination)
        {
            // A plain tree move into CMailMan leaves _roomFlags/_isPendingMail
            // as garbage - that's what produced the stuck, non-interactive
            // "In Tray" capsule. Pair it with a real destination: the current
            // room's station if it has one, else EmbLobby as a safe fallback.
            string destRoomName = _currentRoomName is not null && ChevronCodes.HasStation(_currentRoomName)
                ? _currentRoomName
                : "EmbLobby";
            ChevronCodes.TryGetCode(destRoomName, out uint code);

            // Mark AFTER the real destination is set - MarkItemAsToolPlaced
            // is only safe once _roomFlags holds a real value, since that's
            // the point findMailByFlags stops consulting _destRoomFlags.
            ok = GameActions.SetItemMailDestination(_mem, itemAddr, code);
            if (ok)
                GameActions.MarkItemAsToolPlaced(_mem, itemAddr);

            _lastMailItems = null; // force the Mail tab to refresh next tick
            message = $"Move item 0x{itemAddr:X} -> mail, destination {destRoomName}";
        }
        else
        {
            if (ok)
            {
                // Item left the mail system (or never entered it) via this
                // move. Only clear the sentinel if WE actually set it -
                // never touch _destRoomFlags on an item we didn't mark,
                // so an organically-mailed item's real pending-destination
                // value is left alone.
                int? destRoomFlags = _mem.ReadInt32(itemAddr + GameOffsets.ItemDestRoomFlags);
                if (destRoomFlags is int drf && unchecked((uint)drf) == GameOffsets.ToolPlacedSentinel)
                    GameActions.UnmarkItemAsToolPlaced(_mem, itemAddr);
            }
            message = $"Move item 0x{itemAddr:X} -> 0x{roomAddr:X}";
        }

        if (ok)
            DoRefreshAllItems();

        ShowActionResult(ok, message);
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

    private void DoDisplayMessageText()
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
        if (string.IsNullOrEmpty(_txtMsgText.Text))
        {
            ShowActionResult(false, "Enter a message first");
            return;
        }

        bool ok = GameActions.DisplayPetMessageText(_mem, _currentInventoryRoom.Value, _txtMsgText.Text, 0);
        ShowActionResult(ok, $"Display free text: \"{_txtMsgText.Text}\"");
    }

    private void DoSetMailDestToCurrentRoom()
    {
        if (!_mem.IsAttached)
        {
            ShowActionResult(false, "Not attached");
            return;
        }
        if (_lvMail.SelectedItems.Count == 0)
        {
            ShowActionResult(false, "Select an item first");
            return;
        }
        if (_currentRoomName is null || !ChevronCodes.TryGetCode(_currentRoomName, out uint code))
        {
            ShowActionResult(false, "Current room has no SuccUBus station");
            return;
        }

        long itemAddr = (long)_lvMail.SelectedItems[0].Tag!;
        bool ok = GameActions.SetItemMailDestination(_mem, itemAddr, code);
        if (ok)
            GameActions.MarkItemAsToolPlaced(_mem, itemAddr); // explicit tool write, not gameplay

        ShowActionResult(ok, $"Set mail destination to {_currentRoomName} (0x{code:X})");

        // Force a refresh next tick rather than waiting up to a second.
        _lastMailItems = null;
    }

    private void DoRefreshAllItems()
    {
        if (!_mem.IsAttached)
        {
            ShowActionResult(false, "Not attached");
            return;
        }
        if (_currentProject is null)
        {
            ShowActionResult(false, "Project not resolved yet");
            return;
        }

        // Preserve selection and scroll position across the rebuild - item
        // addresses are stable across a move (only the parent pointer
        // changes), so matching by address (Tag) works even when the
        // refresh was triggered by moving the selected item itself.
        long? previouslySelected = _lvInventory.SelectedItems.Count > 0
            ? (long)_lvInventory.SelectedItems[0].Tag!
            : null;
        long? previousTop = _lvInventory.TopItem?.Tag as long?;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        List<CarryItemLocation> items = GameState.FindAllCarryItems(_mem, _currentProject.Value);
        sw.Stop();

        _lastInventory = items;
        _lvInventory.Items.Clear();
        foreach (CarryItemLocation item in items.OrderBy(i => i.Name))
        {
            var lvi = new ListViewItem(item.Name) { Tag = item.Address };
            lvi.SubItems.Add(LocationLabel(item.ParentAddress, item.ParentName));
            lvi.SubItems.Add($"0x{item.Address:X}");
            _lvInventory.Items.Add(lvi);
        }

        if (previouslySelected is long selAddr)
        {
            ListViewItem? match = _lvInventory.Items.Cast<ListViewItem>()
                .FirstOrDefault(i => (long)i.Tag! == selAddr);
            if (match is not null)
            {
                match.Selected = true;
                match.Focused = true;
            }
        }
        if (previousTop is long topAddr)
        {
            ListViewItem? topMatch = _lvInventory.Items.Cast<ListViewItem>()
                .FirstOrDefault(i => (long)i.Tag! == topAddr);
            if (topMatch is not null)
                _lvInventory.TopItem = topMatch;
        }

        _lblAllItemsStatus.Text = $"{items.Count}/{ItemNames.All.Length} items found ({sw.ElapsedMilliseconds} ms)";
    }

    /// <summary>
    /// Friendly label for an item's current parent: the cached
    /// CPetControl/CMailMan addresses get their known names, an actual
    /// named container (a room/node) shows its name, and anything else
    /// falls back to its raw address.
    /// </summary>
    private string LocationLabel(long? parentAddr, string? parentName)
    {
        if (parentAddr is null)
            return "(unknown)";
        if (parentAddr == _currentInventoryRoom)
            return "Player Inventory";
        if (parentAddr == _currentMailManRoom)
            return "Mail System";
        if (!string.IsNullOrEmpty(parentName) && parentName != "NoName")
            return parentName;
        return $"0x{parentAddr:X}";
    }

    private void DoCopyInventoryAddress()
    {
        if (_lvInventory.SelectedItems.Count == 0)
        {
            ShowActionResult(false, "Select an item first");
            return;
        }

        long addr = (long)_lvInventory.SelectedItems[0].Tag!;
        Clipboard.SetText($"0x{addr:X}");
        ShowActionResult(true, $"Copied address 0x{addr:X}");
    }

    private void DoCopyAddressRow()
    {
        if (_lvAddresses.SelectedItems.Count == 0)
        {
            ShowActionResult(false, "Select a row first");
            return;
        }

        ListViewItem selected = _lvAddresses.SelectedItems[0];
        if (selected.Tag is not string addrText)
        {
            ShowActionResult(false, "No address resolved for this field yet");
            return;
        }

        Clipboard.SetText(addrText);
        ShowActionResult(true, $"Copied {selected.Text}: {addrText}");
    }

    private bool RequireAttachedAndResolved(out long gameManager)
    {
        gameManager = 0;
        if (!_mem.IsAttached)
        {
            ShowActionResult(false, "Not attached");
            return false;
        }
        if (_currentGameManager is null)
        {
            ShowActionResult(false, "gameManager not resolved yet (menu / loading?)");
            return false;
        }
        gameManager = _currentGameManager.Value;
        return true;
    }

    private void ShowActionResult(bool ok, string what)
    {
        string ts = DateTime.Now.ToString("HH:mm:ss");
        _lblActionResult.ForeColor = ok ? Color.DarkGreen : Color.DarkRed;
        _lblActionResult.Text = $"[{ts}] {what}: {(ok ? "OK" : "FAILED")}";
    }

    /// <summary>
    /// Surfaces a PET talk command captured by the hook in the same
    /// top-level feedback line as ShowActionResult, rather than a
    /// separate log list - there's no pass/fail here, just "here's what
    /// came through", so it gets its own neutral-colored format.
    /// </summary>
    private void ShowCapturedCommand(string command)
    {
        string ts = DateTime.Now.ToString("HH:mm:ss");
        _lblActionResult.ForeColor = Color.DarkSlateBlue;
        _lblActionResult.Text = $"[{ts}] PET command captured: {command}";
    }

    private static bool TryParseHex(string text, out long value)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text[2..];
        return long.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out value);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (TextCommandHook.IsInstalled)
        {
            TextCommandHook.Uninstall(_mem);
        }
        if (ClassUpgradeHook.IsInstalled)
        {
            ClassUpgradeHook.Uninstall(_mem);
        }
        _timer.Stop();
        _mem.Dispose();
        _apConnection.Dispose();
        base.OnFormClosed(e);
    }
}
