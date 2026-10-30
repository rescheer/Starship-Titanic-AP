using Archipelago.MultiClient.Net.Colors;
using Archipelago.MultiClient.Net.MessageLog.Parts;

namespace StarshipTitanicAp;

/// <summary>Main application window: the player-facing view of a <see cref="ClientSession"/>.</summary>
public sealed class MainForm : Form
{
    private readonly ClientSession _session;

    private readonly Button _btnAttach = new() { Text = "Attach", Width = 110, Height = 40 };
    private readonly Button _btnApToggle = new() { Text = "Connect to AP...", Width = 110, Height = 40 };

    // Server log
    private readonly RichTextBox _txtServerLog = new()
    {
        Multiline = true,
        ReadOnly = true,
        Dock = DockStyle.Fill,
        ScrollBars = RichTextBoxScrollBars.Vertical,
        Font = new Font(Control.DefaultFont.FontFamily, 11f),
        WordWrap = true,
    };

    private readonly TextBox _txtChatInput = new() { Dock = DockStyle.Fill };
    private readonly Button _btnChatSend = new() { Text = "Send", Width = 80, Dock = DockStyle.Right };

    private static readonly Font InfoFont = new(Control.DefaultFont.FontFamily, 11f);
    private readonly Label _lblInfoRoom = new() { Text = "Room: -", AutoSize = true, Font = InfoFont };
    private readonly Label _lblInfoMail = new() { Text = "Succ-U-Bus has 0 AP items ready for delivery", AutoSize = true, Font = InfoFont };
    private int _lastMailCount;
    private readonly Label _lblInfoChecks = new() { Text = "Checks: -/-", AutoSize = true, Font = InfoFont };
    private readonly Label _lblInfoStations = new() { Text = "Visited Succ-U-Bus Stations (-/-)", AutoSize = true, Font = InfoFont };
    // Parrot Hunt goal only
    private readonly Label _lblInfoParrot = new() { Text = "", AutoSize = true, Font = InfoFont, Visible = false };
    private readonly TableLayoutPanel _infoPanel = new()
    {
        Dock = DockStyle.Top,
        ColumnCount = 2,
        RowCount = 3,
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

    // Room Access panel - one heading per entry requirement (rules.py's entrance rules), each followed by the
    // Access Cards of the rooms behind it
    private enum RoomRequirement { SgtClass, SecondClass, FirstClass, TitaniaRepaired }

    private static readonly (RoomRequirement Requirement, string Heading, string[] Cards)[] RoomAccessGroups =
    {
        (RoomRequirement.SgtClass, "SGT Class", new[]
        {
            "Top of the Well Access Card",
            "Bottom of the Well Access Card",
            "Parrot Lobby Access Card",
            "Bilge Room Access Card",
            "Titania's Room Access Card",
            "SGT Class Staterooms Access Card",
        }),
        (RoomRequirement.SecondClass, "2nd Class", new[]
        {
            "Creator's Chamber Access Card",
            "Sculpture Chamber Access Card",
            "Promenade Deck Access Card",
            "Bar Access Card",
            "Music Room Access Card",
            "2nd Class Staterooms Access Card",
        }),
        (RoomRequirement.FirstClass, "1st Class", new[]
        {
            "Arboretum Access Card",
            "1st Class Restaurant Access Card",
            "1st Class Staterooms Access Card",
        }),
        (RoomRequirement.TitaniaRepaired, "Titania Repaired", new[]
        {
            "Bridge Access Card",
        }),
    };

    // Location-name prefixes ("<prefix> - ...") counted toward each card's "(x/y)", for cards whose rooms aren't
    // just the card's display name. Follows locations.py's regions: the Bomb Room's Succ-U-Bus is in Titania's Room,
    // and the Broken Elevator is boarded from the 2nd Class Lobby.
    private static readonly Dictionary<string, string[]> RoomLocationPrefixes = new()
    {
        ["Titania's Room Access Card"] = new[] { "Titania's Room", "Bomb Room" },
        ["SGT Class Staterooms Access Card"] = new[] { "SGT Class Lobby", "SGT Class Stateroom" },
        ["2nd Class Staterooms Access Card"] = new[] { "2nd Class Lobby", "2nd Class Stateroom", "Broken Elevator" },
        ["1st Class Staterooms Access Card"] = new[] { "1st Class Lobby", "1st Class Stateroom" },
    };

    private static readonly Font RoomAccessFont =new(Control.DefaultFont.FontFamily, 10f);
    private readonly FlowLayoutPanel _roomAccessPanel = new()
    {
        Dock = DockStyle.Right,
        Width = 280,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        Padding = new Padding(8, 6, 8, 6),
    };
    private readonly Dictionary<RoomRequirement, Label> _roomRequirementLabels = new();
    private readonly Dictionary<string, (Label Mark, Label Name)> _roomAccessLabels = new();
    // card -> the slot's location names in its rooms, rebuilt on each connection state change
    private readonly Dictionary<string, string[]> _roomLocationNames = new();
    // Lists each room's uncompleted checks on hover
    private readonly ToolTip _roomAccessToolTip = new() { AutoPopDelay = 30000, InitialDelay = 300 };

    public MainForm()
    {
        _session = new ClientSession();
        Text = AppInfo.TitleBarText;
        Width = 900;   // 500 * 1.5 * 1.20
        Height = 660;  // 600 * 1.10
        StartPosition = FormStartPosition.CenterScreen;

        BuildLayout();
        UpdateFooterAttachStatus();
        UpdateFooterApStatus();
        UpdateApToggleButton();

        _session.ServerLog += AppendServerLog;
        _session.ServerLogMessage += AppendServerLogMessage;
        _session.AttachChanged += () =>
        {
            UpdateFooterAttachStatus();
            _btnAttach.Enabled = !_session.Memory.IsAttached;
        };
        _session.ApStateChanged += (_, _) =>
        {
            UpdateFooterApStatus();
            RebuildRoomLocationNames();
            UpdateRoomAccess();
            UpdateApToggleButton();
            UpdateInfoChecks();
            UpdateInfoMailCount(_lastMailCount);
            UpdateInfoParrot();
        };
        _session.ItemReceived += UpdateRoomAccess;
        _session.ItemReceived += UpdateInfoParrot;
        _session.PendingChecksChanged += UpdateRoomAccess; // picks up the Titania repair check
        _session.MailChanged += items =>
        {
            if (items is not null)
                UpdateInfoMailCount(items.Count(i => i.ToolPlaced));
        };
        _session.RoomNodeViewChanged += (rnv, _) =>
        {
            if (rnv is { } r)
                UpdateInfoRoom(_session.CurrentRoomName ?? RoomNames.GetName(r.Room));
        };
        _session.Ticked += () =>
        {
            UpdateInfoChecks();
            UpdateInfoAreaSaveSeedGuard();
        };

        _btnAttach.Click += (_, _) => _session.Attach();
        _btnApToggle.Click += (_, _) => DoToggleApConnection();
        _btnChatSend.Click += (_, _) => DoSendChatMessage();
        _txtChatInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
                return;
            e.SuppressKeyPress = true;
            DoSendChatMessage();
        };

        _session.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _session.Dispose();
        base.OnFormClosed(e);
    }

    private void BuildLayout()
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
        _lblInfoParrot.Margin = new Padding(0, 8, 0, 0);
        _infoPanel.Controls.Add(_lblInfoParrot, 0, 2);
        _infoPanel.SetColumnSpan(_lblInfoParrot, 2);

        foreach ((RoomRequirement requirement, string heading, string[] cards) in RoomAccessGroups)
        {
            var headingLabel = new Label { Text = heading, AutoSize = true, Font = new Font(RoomAccessFont, FontStyle.Bold), Margin = new Padding(0, 6, 0, 4) };
            _roomRequirementLabels[requirement] = headingLabel;
            _roomAccessPanel.Controls.Add(headingLabel);

            // Each room is a ✔/✖ mark and a name in separate labels, so the two can be colored differently
            foreach (string card in cards)
            {
                var row = new FlowLayoutPanel
                {
                    FlowDirection = FlowDirection.LeftToRight,
                    WrapContents = false,
                    AutoSize = true,
                    Margin = new Padding(14, 0, 0, 4),
                    Padding = new Padding(0),
                };
                var mark = new Label { AutoSize = true, Font = RoomAccessFont, Margin = new Padding(0, 0, 2, 0) };
                var name = new Label { Text = AccessCardDisplayName(card), AutoSize = true, Font = RoomAccessFont, Margin = new Padding(0) };
                row.Controls.Add(mark);
                row.Controls.Add(name);
                _roomAccessLabels[card] = (mark, name);
                _roomAccessPanel.Controls.Add(row);
            }
        }

        // Docking runs from the last-added control inward: footer, seed warning and info area across the full width,
        // then the room access panel down the right side, leaving the chat row and log to share what remains on the left
        Controls.Add(_txtServerLog);
        Controls.Add(chatRow);
        Controls.Add(_roomAccessPanel);
        Controls.Add(_infoPanel);
        Controls.Add(_lblInfoSaveSeedMismatch);
        Controls.Add(footerPanel);
    }

    private void DoToggleApConnection()
    {
        if (_session.Ap.State is ApConnectionState.Connected or ApConnectionState.Connecting)
        {
            _session.Ap.Disconnect();
            return;
        }

        ConnectionSettings.Data connection = _session.Connection;
        using var dlg = new ConnectDialog(connection.Server, connection.Slot, connection.Password);
        if (dlg.ShowDialog(this) == DialogResult.OK)
            _session.Connect(dlg.Server, dlg.Slot, dlg.Password);
    }

    private void DoSendChatMessage()
    {
        string text = _txtChatInput.Text.Trim();
        if (text.Length == 0)
            return;

        bool ok = _session.SubmitChat(text);
        if (ok)
            _txtChatInput.Clear();
        else
            AppendServerLog("CLIENT: Not connected - message not sent", bold: false);
    }

    /// <summary>Appends a line to the normal UI's server-only log.</summary>
    private void AppendServerLog(string line, bool bold)
    {
        AppendServerLogParts(new[] { (line, SystemColors.WindowText) }, bold);
    }

    /// <summary>Appends an AP server message to the server-only log, each part in its AP palette color.</summary>
    private void AppendServerLogMessage(MessagePart[] parts)
    {
        AppendServerLogParts(parts.Select(part => (part.Text, LogColor(part))), bold: false);
    }

    /// <summary>Appends one line built from differently colored pieces, trimming the oldest lines past the cap.</summary>
    private void AppendServerLogParts(IEnumerable<(string Text, Color Color)> parts, bool bold)
    {
        _txtServerLog.SelectionStart = _txtServerLog.TextLength;
        _txtServerLog.SelectionLength = 0;
        _txtServerLog.SelectionFont = bold ? new Font(_txtServerLog.Font, FontStyle.Bold) : _txtServerLog.Font;
        foreach ((string text, Color color) in parts)
        {
            _txtServerLog.SelectionColor = color;
            _txtServerLog.AppendText(text);
        }
        _txtServerLog.SelectionColor = SystemColors.WindowText;
        _txtServerLog.AppendText(Environment.NewLine);

        // Cut the oldest lines in place rather than reassigning Lines, which would drop every line's colors
        const int maxLines = 2000;
        if (_txtServerLog.Lines.Length > maxLines)
        {
            _txtServerLog.ReadOnly = false; // a read-only RichTextBox ignores SelectedText edits
            _txtServerLog.Select(0, _txtServerLog.GetFirstCharIndexFromLine(maxLines / 2));
            _txtServerLog.SelectedText = "";
            _txtServerLog.ReadOnly = true;
            _txtServerLog.SelectionStart = _txtServerLog.TextLength;
        }

        _txtServerLog.ScrollToCaret();
    }

    /// <summary>A message part's color, from AP's palette but darkened where the dark-background original would be
    /// hard to read on the log's white background. Plain text stays the normal text color.</summary>
    private static Color LogColor(MessagePart part) => part.IsBackgroundColor ? SystemColors.WindowText : part.PaletteColor switch
    {
        PaletteColor.Black => Color.Black,
        PaletteColor.Red => Color.Firebrick,
        PaletteColor.Green => Color.Green,
        PaletteColor.Blue => Color.Blue,
        PaletteColor.Cyan => Color.DarkCyan,
        PaletteColor.Magenta => Color.DarkMagenta,
        PaletteColor.Yellow => Color.DarkGoldenrod,
        PaletteColor.SlateBlue => Color.SlateBlue,
        PaletteColor.Salmon => Color.IndianRed,
        PaletteColor.Plum => Color.DarkOrchid,
        _ => SystemColors.WindowText, // White or unspecified - plain text
    };

    private void UpdateFooterAttachStatus()
    {
        if (_session.Memory.IsAttached)
        {
            _lblFooterAttach.Text = $"✔ Attached to Starship Titanic (PID {_session.Memory.ProcessId})";
            _lblFooterAttach.ForeColor = Color.Green;
        }
        else
        {
            _lblFooterAttach.Text = "✖ Not attached to a game instance";
            _lblFooterAttach.ForeColor = Color.Red;
        }
    }

    private void UpdateFooterApStatus()
    {
        switch (_session.Ap.State)
        {
            case ApConnectionState.Connected:
                _lblFooterAp.Text = "✔ Connected to Archipelago";
                _lblFooterAp.ForeColor = Color.Green;
                break;
            case ApConnectionState.Connecting:
                _lblFooterAp.Text = "Connecting...";
                _lblFooterAp.ForeColor = Color.Goldenrod;
                break;
            case ApConnectionState.Disconnected:
            case ApConnectionState.ConnectionFailed:
            default:
                _lblFooterAp.Text = "✖ Not connected to an Archipelago server";
                _lblFooterAp.ForeColor = Color.Red;
                break;
        }
    }

    /// <summary>Sets the information area's room line to the given engine room name's readable form.</summary>
    private void UpdateInfoRoom(string roomName)
    {
        _lblInfoRoom.Text = $"Room: {LocationChecks.GetReadableRoomName(roomName)}";
    }

    /// <summary>Sets the information area's mail line to the current count of tool-placed AP items sitting in the mail
    /// system, or to a fixed notice when the seed delivers items straight to the inventory.</summary>
    private void UpdateInfoMailCount(int count)
    {
        _lastMailCount = count;
        _lblInfoMail.Text = ItemSync.IsDirectToInventory(_session.Ap.KnownSlotData)
            ? "Direct to Inventory item mode enabled"
            : $"Succ-U-Bus has {count} AP item{(count == 1 ? "" : "s")} ready for delivery";
    }

    /// <summary>Sets the information area's "Checks: x/y" and "Visited Succ-U-Bus Stations (x/y)" lines.</summary>
    private void UpdateInfoChecks()
    {
        if (_session.Ap.GetLocationCheckSummary() is { } overall)
            _lblInfoChecks.Text = $"Checks: {overall.Checked}/{overall.Total}";
        else
            _lblInfoChecks.Text = "Checks: -/-";

        if (_session.Ap.GetLocationCheckSummary(LocationChecks.SuccUBusStationLocationNames) is { } stations)
            _lblInfoStations.Text = $"Visited Succ-U-Bus Stations ({stations.Checked}/{stations.Total})";
        else
            _lblInfoStations.Text = "Visited Succ-U-Bus Stations (-/-)";
    }

    /// <summary>Sets the information area's Parrot Hunt line (the clues revealed so far), shown only for
    /// that goal.</summary>
    private void UpdateInfoParrot()
    {
        string? clues = ParrotHuntSync.DescribeClues(_session.Ap);
        _lblInfoParrot.Visible = clues is not null;
        if (clues is not null)
            _lblInfoParrot.Text = $"Parrot Hunt: {clues}";
    }

    /// <summary>The class reached through the DeskBot's own upgrades, for seeds with vanilla class upgrades.</summary>
    private int? ClassFromUpgradeChecks()
    {
        foreach (PassengerClass passengerClass in new[] { PassengerClass.First, PassengerClass.Second })
        {
            if (LocationChecks.TryGetClassUpgradeLocationName((int)passengerClass, out string locationName)
                && _session.Ap.IsLocationCheckedOrQueued(locationName))
                return (int)passengerClass;
        }
        return null;
    }

    /// <summary>Marks the right-hand Room Access panel. Each requirement heading gets a green ✔ once met (passenger
    /// class from received upgrades, or Titania repaired) and a red ✖ until then. Each room is green once all its
    /// checks are done, whether or not it's accessible; otherwise a red ✖ while its required Access Card hasn't been
    /// received, a yellow ✔ when the card is in hand (or not required) but its heading isn't met yet, and a plain ✔
    /// when both are. Plain names while not connected.</summary>
    private void UpdateRoomAccess()
    {
        bool connected = _session.Ap.State == ApConnectionState.Connected;
        IReadOnlyDictionary<string, object>? slotData = _session.Ap.SlotData;
        HashSet<string> requiredCards = RoomKeys.GetRequiredCards(slotData);
        string[] receivedItems = _session.Ap.GetReceivedItemNames();
        int? passengerClass = slotData is null ? null
            : ClassUpgradeTracker.IsVanilla(slotData) ? ClassFromUpgradeChecks()
            : ClassUpgradeTracker.ComputeClass(receivedItems, slotData);

        foreach ((RoomRequirement requirement, string heading, string[] cards) in RoomAccessGroups)
        {
            bool met = requirement switch
            {
                RoomRequirement.SecondClass => passengerClass is (int)PassengerClass.Second or (int)PassengerClass.First,
                RoomRequirement.FirstClass => passengerClass is (int)PassengerClass.First,
                RoomRequirement.TitaniaRepaired => _session.Ap.IsLocationCheckedOrQueued(ClassSync.TitaniaRepairedLocation),
                _ => true,
            };

            if (!connected)
                SetStatusLabel(_roomRequirementLabels[requirement], heading, SystemColors.ControlText);
            else
                SetStatusLabel(_roomRequirementLabels[requirement], $"{(met ? "✔" : "✖")} {heading}", met ? Color.Green : Color.Red);

            foreach (string card in cards)
            {
                (Label markLabel, Label nameLabel) = _roomAccessLabels[card];
                string name = AccessCardDisplayName(card);
                bool allChecked = false;
                string toolTip = "";
                if (_roomLocationNames.TryGetValue(card, out string[]? locations)
                    && _session.Ap.GetLocationCheckSummary(locations) is { Total: > 0 } summary)
                {
                    name += $" ({summary.Checked}/{summary.Total})";
                    allChecked = summary.Checked == summary.Total;
                    toolTip = string.Join(Environment.NewLine, locations.Where(location => !_session.Ap.IsLocationChecked(location)));
                }
                foreach (Label label in new[] { markLabel, nameLabel })
                {
                    if (_roomAccessToolTip.GetToolTip(label) != toolTip)
                        _roomAccessToolTip.SetToolTip(label, toolTip);
                }

                bool hasCard = !requiredCards.Contains(card) || receivedItems.Contains(card, StringComparer.OrdinalIgnoreCase);
                (string mark, Color markColor, Color nameColor) = true switch
                {
                    _ when !connected => ("", SystemColors.ControlText, SystemColors.ControlText),
                    _ when allChecked => (hasCard ? "✔" : "✖", Color.Green, Color.Green),
                    _ when !hasCard => ("✖", Color.Red, Color.Red),
                    _ when !met => ("✔", Color.Goldenrod, Color.Red),
                    _ => ("✔", SystemColors.ControlText, SystemColors.ControlText),
                };
                SetStatusLabel(markLabel, mark, markColor);
                SetStatusLabel(nameLabel, name, nameColor);
            }
        }
    }

    /// <summary>Sorts the slot's location names under the Access Card of the room each is in, by name prefix.
    /// Empty while not connected.</summary>
    private void RebuildRoomLocationNames()
    {
        _roomLocationNames.Clear();
        string[] allLocations = _session.Ap.GetAllLocationNames();
        if (allLocations.Length == 0)
            return;

        foreach (string card in _roomAccessLabels.Keys)
        {
            string[] prefixes = RoomLocationPrefixes.TryGetValue(card, out string[]? custom) ? custom : new[] { AccessCardDisplayName(card) };
            _roomLocationNames[card] = allLocations
                .Where(location => prefixes.Any(prefix => location.StartsWith(prefix + " - ", StringComparison.Ordinal)))
                .ToArray();
        }
    }

    /// <summary>Sets a label's text and color, skipping unchanged values - UpdateRoomAccess runs every tick.</summary>
    private static void SetStatusLabel(Label label, string text, Color color)
    {
        if (label.Text != text)
            label.Text = text;
        if (label.ForeColor != color)
            label.ForeColor = color;
    }

    /// <summary>The card's item name without the " Access Card" suffix - redundant under the panel's heading.</summary>
    private static string AccessCardDisplayName(string card)
    {
        const string suffix = " Access Card";
        return card.EndsWith(suffix, StringComparison.Ordinal) ? card[..^suffix.Length] : card;
    }

    /// <summary>Swaps the info area between the normal room/mail/checks/stations labels and a single centered
    /// warning when AP syncing is paused because the attached save doesn't match the connected AP seed.</summary>
    private void UpdateInfoAreaSaveSeedGuard()
    {
        bool mismatch = _session.Options.EnforceSaveSeedGuard && _session.SaveSeedGuardState == SaveSeedGuardState.Blocked;
        _infoPanel.Visible = !mismatch;
        _lblInfoSaveSeedMismatch.Visible = mismatch;
    }

    private void UpdateApToggleButton()
    {
        bool connectedOrConnecting = _session.Ap.State is ApConnectionState.Connected or ApConnectionState.Connecting;
        _btnApToggle.Text = connectedOrConnecting ? "Disconnect" : "Connect to AP...";
        _btnApToggle.Enabled = _session.Ap.State != ApConnectionState.Connecting;
    }
}
