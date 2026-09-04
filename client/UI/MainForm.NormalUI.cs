namespace StarshipTitanicAp;

public sealed partial class MainForm
{
    // --- Normal (non-debug) UI ---

    private readonly Button _btnApToggle = new() { Text = "Connect to AP...", Width = 110, Height = 40 };

    // Server log
    private readonly RichTextBox _txtServerLog = new()
    {
        Multiline = true,
        ReadOnly = true,
        Dock = DockStyle.Fill,
        ScrollBars = RichTextBoxScrollBars.Vertical,
        Font = new Font(FontFamily.GenericMonospace, 12),
        WordWrap = true,
    };

    private readonly TextBox _txtChatInput = new() { Dock = DockStyle.Fill };
    private readonly Button _btnChatSend = new() { Text = "Send", Width = 80, Dock = DockStyle.Right };

    private static readonly Font InfoFont = new(Control.DefaultFont.FontFamily, 11f);
    private readonly Label _lblInfoRoom = new() { Text = "Room: -", AutoSize = true, Font = InfoFont };
    private readonly Label _lblInfoMail = new() { Text = "Succ-U-Bus has 0 AP items ready for delivery.", AutoSize = true, Font = InfoFont };
    private readonly Label _lblInfoChecks = new() { Text = "Checks: -/-", AutoSize = true, Font = InfoFont };
    private readonly Label _lblInfoStations = new() { Text = "Visited Succ-U-Bus Stations (-/-)", AutoSize = true, Font = InfoFont };
    private readonly TableLayoutPanel _infoPanel = new()
    {
        Dock = DockStyle.Top,
        ColumnCount = 2,
        RowCount = 2,
        AutoSize = true,
        Padding = new Padding(6),
    };
    private readonly Label _lblInfoSaveSeedMismatch = new()
    {
        Text = "Save seed does not match server seed! AP sync paused",
        Dock = DockStyle.Top,
        Height = 40,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font(InfoFont, FontStyle.Bold),
        ForeColor = Color.Red,
        Visible = false,
    };

    private readonly Label _lblFooterAp = new() { Text = "✖ Not connected to an Archipelago server", AutoSize = true, Anchor = AnchorStyles.Right, ForeColor = Color.Red };
    private readonly Label _lblFooterAttach = new() { Text = "✖ Not attached to a game instance", AutoSize = true, Anchor = AnchorStyles.Right, ForeColor = Color.Red };

    private bool _readyToPlayLogged;

    private void BuildNormalLayout()
    {
        var buttonRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0),
        };
        buttonRow.Controls.Add(_btnAttach);
        buttonRow.Controls.Add(_btnApToggle);

        var statusPanel = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0),
        };
        statusPanel.Controls.Add(_lblFooterAp, 0, 0);
        statusPanel.Controls.Add(_lblFooterAttach, 0, 1);

        var footerPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            Padding = new Padding(8, 4, 8, 6),
        };
        footerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footerPanel.Controls.Add(buttonRow, 0, 0);
        footerPanel.Controls.Add(statusPanel, 1, 0);

        var chatRow = new Panel { Dock = DockStyle.Bottom, Height = 30, Padding = new Padding(4, 2, 4, 2) };
        chatRow.Controls.Add(_btnChatSend);
        chatRow.Controls.Add(_txtChatInput);

        _infoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _infoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _lblInfoRoom.Margin = new Padding(0, 0, 16, 8);
        _lblInfoMail.Margin = new Padding(0, 0, 0, 8);
        _lblInfoChecks.Margin = new Padding(0, 0, 16, 0);
        _lblInfoStations.Margin = new Padding(0);
        _infoPanel.Controls.Add(_lblInfoRoom, 0, 0);
        _infoPanel.Controls.Add(_lblInfoMail, 1, 0);
        _infoPanel.Controls.Add(_lblInfoChecks, 0, 1);
        _infoPanel.Controls.Add(_lblInfoStations, 1, 1);

        Controls.Add(_txtServerLog);
        Controls.Add(_infoPanel);
        Controls.Add(_lblInfoSaveSeedMismatch);
        Controls.Add(chatRow);
        Controls.Add(footerPanel);
    }

    private void WireNormalUiEvents()
    {
        _btnApToggle.Click += (_, _) =>
        {
            if (_apConnection.State is ApConnectionState.Connected or ApConnectionState.Connecting)
            {
                _apConnection.Disconnect();
                return;
            }

            using var dlg = new ConnectDialog(_txtApServer.Text, _txtApSlot.Text, _txtApPassword.Text);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _txtApServer.Text = dlg.Server;
                _txtApSlot.Text = dlg.Slot;
                _txtApPassword.Text = dlg.Password;
                DoApConnect();
            }
        };

        _btnChatSend.Click += (_, _) => DoSendChatMessage();
        _txtChatInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
                return;
            e.SuppressKeyPress = true;
            DoSendChatMessage();
        };

        _apConnection.CheckQueued += name =>
        {
            if (_apConnection.State != ApConnectionState.Connected)
                AppendServerLog($"CLIENT: Location check queued: {name}");
        };
    }

    private void DoSendChatMessage()
    {
        string text = _txtChatInput.Text.Trim();
        if (text.Length == 0)
            return;

        if (string.Equals(text, "!force_seed", StringComparison.OrdinalIgnoreCase))
        {
            _txtChatInput.Clear();
            HandleForceSeedCommand();
            return;
        }

        bool ok = _apConnection.SendCommand(text);
        if (ok)
            _txtChatInput.Clear();
        else
            ShowActionResult(false, "Not connected - message not sent");
    }

    /// <summary>Appends a line to the normal UI's server-only log.</summary>
    private void AppendServerLog(string line, bool bold = false)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => AppendServerLog(line, bold)));
            return;
        }

        _txtServerLog.SelectionStart = _txtServerLog.TextLength;
        _txtServerLog.SelectionLength = 0;
        _txtServerLog.SelectionFont = bold ? new Font(_txtServerLog.Font, FontStyle.Bold) : _txtServerLog.Font;
        _txtServerLog.AppendText(line + Environment.NewLine);

        const int maxLines = 2000;
        string[] lines = _txtServerLog.Lines;
        if (lines.Length > maxLines)
        {
            _txtServerLog.Lines = lines[(lines.Length - maxLines / 2)..];
            _txtServerLog.SelectionStart = _txtServerLog.TextLength;
        }

        _txtServerLog.ScrollToCaret();
    }

    /// <summary>Logs "Ready to Play!" to the server log the moment both the game attach and the AP connection are simultaneously up.</summary>
    private void CheckReadyToPlay()
    {
        if (_readyToPlayLogged)
            return;
        if (!_mem.IsAttached || _apConnection.State != ApConnectionState.Connected)
            return;

        _readyToPlayLogged = true;
        AppendServerLog("Ready to Play!", bold: true);
    }

    private void UpdateFooterAttachStatus()
    {
        if (_mem.IsAttached)
        {
            _lblFooterAttach.Text = $"✔ Attached to Starship Titanic (PID {_mem.ProcessId})";
            _lblFooterAttach.ForeColor = Color.Green;
        }
        else
        {
            _lblFooterAttach.Text = "✖ Not attached to a game instance";
            _lblFooterAttach.ForeColor = Color.Red;
            _readyToPlayLogged = false;
        }
    }

    private void UpdateFooterApStatus()
    {
        switch (_apConnection.State)
        {
            case ApConnectionState.Connected:
                _lblFooterAp.Text = "✔ Connected to Archipelago";
                _lblFooterAp.ForeColor = Color.Green;
                break;
            case ApConnectionState.Connecting:
                _lblFooterAp.Text = "Connecting...";
                _lblFooterAp.ForeColor = Color.Goldenrod;
                _readyToPlayLogged = false;
                break;
            case ApConnectionState.Disconnected:
            case ApConnectionState.ConnectionFailed:
            default:
                _lblFooterAp.Text = "✖ Not connected to an Archipelago server";
                _lblFooterAp.ForeColor = Color.Red;
                _readyToPlayLogged = false;
                break;
        }
    }

    /// <summary>Sets the information area's room line to the given engine room name's readable form.</summary>
    private void UpdateInfoRoom(string roomName)
    {
        _lblInfoRoom.Text = $"Room: {LocationChecks.GetReadableRoomName(roomName)}";
    }

    /// <summary>Sets the information area's mail line to the current count of tool-placed AP items sitting in the mail system.</summary>
    private void UpdateInfoMailCount(int count)
    {
        _lblInfoMail.Text = $"Succ-U-Bus has {count} AP item{(count == 1 ? "" : "s")} ready for delivery.";
    }

    /// <summary>Sets the information area's "Checks: x/y" and "Visited Succ-U-Bus Stations (x/y)" lines.</summary>
    private void UpdateInfoChecks()
    {
        if (_apConnection.GetLocationCheckSummary() is { } overall)
            _lblInfoChecks.Text = $"Checks: {overall.Checked}/{overall.Total}";
        else
            _lblInfoChecks.Text = "Checks: -/-";

        if (_apConnection.GetLocationCheckSummary(LocationChecks.SuccUBusStationLocationNames) is { } stations)
            _lblInfoStations.Text = $"Visited Succ-U-Bus Stations ({stations.Checked}/{stations.Total})";
        else
            _lblInfoStations.Text = "Visited Succ-U-Bus Stations (-/-)";
    }

    /// <summary>Swaps the info area between the normal room/mail/checks/stations labels and a single centered
    /// warning when AP syncing is paused because the attached save doesn't match the connected AP seed.</summary>
    private void UpdateInfoAreaSaveSeedGuard()
    {
        bool mismatch = _chkEnforceSaveSeedGuard.Checked && _saveSeedGuardState == SaveSeedGuardState.Blocked;
        _infoPanel.Visible = !mismatch;
        _lblInfoSaveSeedMismatch.Visible = mismatch;
    }

    private void UpdateApToggleButton()
    {
        bool connectedOrConnecting = _apConnection.State is ApConnectionState.Connected or ApConnectionState.Connecting;
        _btnApToggle.Text = connectedOrConnecting ? "Disconnect" : "Connect to AP...";
        _btnApToggle.Enabled = _apConnection.State != ApConnectionState.Connecting;
    }
}
