namespace StarshipTitanicAp;

/// <summary>Diagnostic window (--debug): tabs exposing a <see cref="ClientSession"/>'s live state, its hooks,
/// and manual item/mail/class tools.</summary>
public sealed partial class DebugForm : Form
{
    private readonly ClientSession _session;

    // --- Top bar (shared across tabs) ---
    private readonly Button _btnAttach = new() { Text = "Attach", Width = 110, Height = 40 };
    private readonly Label _lblStatus = new() { Text = "Not attached", AutoSize = true };
    private readonly Label _lblApTopStatus = new() { Text = "AP: Not connected", AutoSize = true, ForeColor = Color.DimGray };
    private readonly Label _lblActionResult = new() { Text = "", AutoSize = true, ForeColor = Color.DarkSlateGray };

    public DebugForm()
    {
        _session = new ClientSession();
        Text = AppInfo.TitleBarText;
        Icon = AppInfo.AppIcon;
        Width = 900;   // 500 * 1.5 * 1.20
        Height = 660;  // 600 * 1.10
        StartPosition = FormStartPosition.CenterScreen;

        BuildLayout();

        _txtApServer.Text = _session.Connection.Server;
        _txtApSlot.Text = _session.Connection.Slot;
        _txtApPassword.Text = _session.Connection.Password;
        UpdatePendingChecksLabel();
        UpdateApTopStatus();

        _session.ActionReported += ShowActionResult;
        _session.CommandCaptured += ShowCapturedCommand;
        _session.DebugLog += AppendLog;
        _session.AddressChanged += SetAddressRow;
        _session.AttachChanged += OnAttachChanged;
        _session.ApStateChanged += OnApStateChanged;
        _session.PendingChecksChanged += UpdatePendingChecksLabel;
        _session.ItemsChanged += DoRefreshAllItems;
        _session.MailChanged += OnMailChanged;
        _session.RoomNodeViewChanged += OnRoomNodeViewChanged;
        _session.RoomFlagsChanged += flags => _lblCurrentLocation.Text = FormatCurrentLocation(flags);
        _session.PassengerClassChanged += cls => _lblClass.Text = $"Class: {PassengerClassNames.GetName(cls)}";
        _session.Ticked += UpdateSaveSeedGuardStatusLabel;

        _btnAttach.Click += (_, _) => _session.Attach();
        _btnDetach.Click += (_, _) => _session.Detach();

        _btnApConnect.Click += (_, _) => DoApConnect();
        _btnApDisconnect.Click += (_, _) => _session.Ap.Disconnect();
        _btnClearPendingChecks.Click += (_, _) => DoClearPendingChecks();

        _chkEnforceSaveSeedGuard.CheckedChanged += (_, _) => _session.Options.EnforceSaveSeedGuard = _chkEnforceSaveSeedGuard.Checked;
        _chkTakeUngrantedItems.CheckedChanged += (_, _) => _session.Options.TakeUngrantedItems = _chkTakeUngrantedItems.Checked;
        _chkAllowInitialUpgrade.CheckedChanged += (_, _) => _session.Options.AllowInitialUpgrade = _chkAllowInitialUpgrade.Checked;
        _txtDebugLockedRooms.TextChanged += (_, _) => _session.Options.ExtraLockedRooms = _txtDebugLockedRooms.Text;

        _btnSetClass.Click += (_, _) => DoSetClass();
        _btnMoveItem.Click += (_, _) => DoMoveToCustomAddress();
        _btnMoveToInventory.Click += (_, _) => DoMoveSelectedTo(MoveDestination.Inventory);
        _btnMoveToMailman.Click += (_, _) => DoMoveSelectedTo(MoveDestination.Mailman);
        _btnMoveToHiddenRoom.Click += (_, _) => DoMoveSelectedTo(MoveDestination.HiddenRoom);
        _btnDisplayMessageText.Click += (_, _) => DoDisplayMessageText();
        _btnSetDestToCurrentRoom.Click += (_, _) => DoSetMailDestToCurrentRoom();
        _btnCopyInventoryAddress.Click += (_, _) => DoCopyInventoryAddress();
        _btnRefreshAllItems.Click += (_, _) => DoRefreshAllItems();
        _btnExportParentSnapshot.Click += (_, _) => DoExportParentSnapshot();
        _btnCopyParentAddress.Click += (_, _) => DoCopyParentAddress();
        _btnViewFields.Click += (_, _) => DoViewFields();
        _btnForceTagSaveSeed.Click += (_, _) => _session.ForceTagSaveSeed();

        _lvMail.SelectedIndexChanged += (_, _) => UpdateMailButtonState();
        _lvInventory.SelectedIndexChanged += (_, _) =>
        {
            bool hasSelection = _lvInventory.SelectedItems.Count > 0;
            _btnCopyInventoryAddress.Enabled = hasSelection;
            _btnCopyParentAddress.Enabled = hasSelection;
            _btnMoveToInventory.Enabled = hasSelection;
            _btnMoveToMailman.Enabled = hasSelection;
            _btnMoveToHiddenRoom.Enabled = hasSelection;
            _btnViewFields.Enabled = hasSelection;
        };
        _lvAddresses.SelectedIndexChanged += (_, _) => _btnCopyAddress.Enabled = _lvAddresses.SelectedItems.Count > 0;
        _btnCopyAddress.Click += (_, _) => DoCopyAddressRow();
        _btnClearLog.Click += (_, _) => _txtLog.Clear();

        _session.Start();
    }

    private void BuildLayout()
    {
        var topPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 78,
            Padding = new Padding(8),
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = false,
        };
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        topPanel.Controls.Add(_btnAttach, 0, 0);

        var infoPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Margin = new Padding(12, 6, 0, 0),
        };
        infoPanel.Controls.Add(_lblStatus);
        infoPanel.Controls.Add(_lblApTopStatus);
        topPanel.Controls.Add(infoPanel, 1, 0);

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
        tabs.TabPages.Add(BuildHooksTab());
        tabs.TabPages.Add(BuildAddressesTab());
        tabs.TabPages.Add(BuildDebugTab());
        tabs.TabPages.Add(BuildLogTab());

        Controls.Add(tabs);
        Controls.Add(feedbackPanel);
        Controls.Add(topPanel);
    }

    private void OnAttachChanged()
    {
        bool attached = _session.Memory.IsAttached;
        _lblStatus.Text = _session.AttachStatus;
        _btnAttach.Enabled = !attached;
        _btnDetach.Enabled = attached;
        RefreshHookControls();

        if (!attached)
        {
            _lblRoomNodeView.Text = "Room: -   Node: -   View: -";
            _lblCurrentLocation.Text = "Location: -";
            _lblClass.Text = "Class: -";
            _lvInventory.Items.Clear();
            _lblAllItemsStatus.Text = "";
            _lvMail.Items.Clear();
            _lblMailCurrentRoom.Text = "Current room: -";
            _lblMailCount.Text = "";
        }
    }

    private void OnRoomNodeViewChanged(RoomNodeView? rnv, string? unavailableReason)
    {
        if (rnv is not { } r)
        {
            _lblRoomNodeView.Text = $"Room: -   Node: -   View: -   ({unavailableReason})";
            return;
        }

        _lblRoomNodeView.Text = $"Room: {r.Room}   Node: {r.Node}   View: {r.View}";
        UpdateMailCurrentRoomLabel();
    }

    private static Label SectionLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font(Control.DefaultFont, FontStyle.Bold),
        Margin = new Padding(0, 6, 0, 4),
    };

    private static Panel Spacer() => new() { Height = 10, Width = 1 };

    private bool RequireAttachedAndResolved(out long gameManager)
    {
        gameManager = 0;
        if (!_session.Memory.IsAttached)
        {
            ShowActionResult(false, "Not attached");
            return false;
        }
        if (_session.GameManager is null)
        {
            ShowActionResult(false, "gameManager not resolved yet (menu / loading?)");
            return false;
        }
        gameManager = _session.GameManager.Value;
        return true;
    }

    private void ShowActionResult(bool ok, string what)
    {
        string ts = DateTime.Now.ToString("HH:mm:ss");
        _lblActionResult.ForeColor = ok ? Color.DarkGreen : Color.DarkRed;
        _lblActionResult.Text = $"[{ts}] {what}: {(ok ? "OK" : "FAILED")}";
        AppendLog(_lblActionResult.Text);
    }

    /// <summary>Surfaces a PET talk command captured by the hook in the top-level feedback line.</summary>
    private void ShowCapturedCommand(string command)
    {
        string ts = DateTime.Now.ToString("HH:mm:ss");
        _lblActionResult.ForeColor = Color.DarkSlateBlue;
        _lblActionResult.Text = $"[{ts}] PET command captured: {command}";
        AppendLog(_lblActionResult.Text);
    }

    /// <summary>Appends a line to the Log tab's history.</summary>
    private void AppendLog(string line)
    {
        _txtLog.AppendText(line + Environment.NewLine);

        const int maxLines = 2000;
        string[] lines = _txtLog.Lines;
        if (lines.Length > maxLines)
        {
            _txtLog.Lines = lines[(lines.Length - maxLines / 2)..];
            _txtLog.SelectionStart = _txtLog.TextLength;
        }
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
        _session.Dispose();
        base.OnFormClosed(e);
    }
}
