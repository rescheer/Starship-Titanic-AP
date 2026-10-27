namespace StarshipTitanicAp;

public sealed partial class MainForm
{
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

    /// <summary>Handles ClientSession.MailChanged.</summary>
    private void OnMailChanged(List<GameState.MailItem>? items)
    {
        if (items is null)
        {
            _lblMailCount.Text = "CMailMan not resolved";
            return;
        }

        UpdateInfoMailCount(items.Count(i => i.ToolPlaced));

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
            lvi.SubItems.Add(item.ToolPlaced ? "Tool" : "Game");
            _lvMail.Items.Add(lvi);
        }

        _lblMailCount.Text = $"{items.Count} item(s) in the mail system";
        UpdateMailButtonState();
    }

    private void UpdateMailCurrentRoomLabel()
    {
        if (_session.CurrentRoomName is null)
        {
            _lblMailCurrentRoom.Text = "Current room: -";
        }
        else if (ChevronCodes.HasStation(_session.CurrentRoomName))
        {
            _lblMailCurrentRoom.Text = $"Current room: {_session.CurrentRoomName} (has a SuccUBus station)";
        }
        else
        {
            _lblMailCurrentRoom.Text = $"Current room: {_session.CurrentRoomName} (no SuccUBus station here)";
        }
        UpdateMailButtonState();
    }

    private void UpdateMailButtonState()
    {
        bool hasSelection = _lvMail.SelectedItems.Count > 0;
        bool hasStationHere = _session.CurrentRoomName is not null && ChevronCodes.HasStation(_session.CurrentRoomName);
        _btnSetDestToCurrentRoom.Enabled = hasSelection && hasStationHere && _session.Memory.IsAttached;
    }

    private void DoSetMailDestToCurrentRoom()
    {
        if (!_session.Memory.IsAttached)
        {
            ShowActionResult(false, "Not attached");
            return;
        }
        if (_lvMail.SelectedItems.Count == 0)
        {
            ShowActionResult(false, "Select an item first");
            return;
        }
        uint? liveRoomFlags = _session.InventoryRoom is long petControl ? GameState.ReadCurrentRoomFlags(_session.Memory, petControl) : null;
        if (_session.CurrentRoomName is null || !ChevronCodes.TryGetCode(_session.CurrentRoomName, liveRoomFlags, out uint code))
        {
            ShowActionResult(false, "Current room has no SuccUBus station");
            return;
        }

        long itemAddr = (long)_lvMail.SelectedItems[0].Tag!;
        bool ok = GameActions.SetItemMailDestination(_session.Memory, itemAddr, code);
        if (ok)
            GameActions.MarkItemAsToolPlaced(_session.Memory, itemAddr);

        ShowActionResult(ok, $"Set mail destination to {_session.CurrentRoomName} (0x{code:X})");

        _session.InvalidateMail();
    }
}
