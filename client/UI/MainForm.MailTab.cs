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

    private void UpdateMail(long gameManager)
    {
        long? project = _currentProject;
        if (project is null)
            return;

        // See UpdateInventory's matching comment: FindMailManRoom is the same shallow, budget-capped walk that
        // has been observed to transiently fail in certain rooms even though the address is still valid - only
        // ever replace the cached address with a fresh non-null result, never null it back out on a miss.
        long? mailManRoom = GameState.FindMailManRoom(_mem, project.Value);
        if (mailManRoom is not null)
            _currentMailManRoom = mailManRoom;
        SetAddressRow("Mail Inventory (CMailMan)", _currentMailManRoom);

        if (_currentMailManRoom is null)
        {
            _lblMailCount.Text = "CMailMan not resolved";
            return;
        }

        List<GameState.MailItem> items = GameState.ReadMailItems(_mem, _currentMailManRoom.Value);
        UpdateInfoMailCount(items.Count(i => i.ToolPlaced));

        bool changed = _lastMailItems is null
            || items.Count != _lastMailItems.Count
            || !items.Select(i => (i.Name, i.IsPendingMail, i.DestRoomFlags, i.RoomFlags, i.ToolPlaced))
                     .SequenceEqual(_lastMailItems.Select(i => (i.Name, i.IsPendingMail, i.DestRoomFlags, i.RoomFlags, i.ToolPlaced)));

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
            lvi.SubItems.Add(item.ToolPlaced ? "Tool" : "Game");
            _lvMail.Items.Add(lvi);
        }

        _lblMailCount.Text = $"{items.Count} item(s) in the mail system";
        UpdateMailButtonState();
    }

    /// <summary>Called on every real room change; retargets any tool-placed mail item to this room's station.</summary>
    private void DeliverQueuedMailAtStation(string roomName)
    {
        if (_currentMailManRoom is null)
            return;
        uint? liveRoomFlags = _currentInventoryRoom is long petControl ? GameState.ReadCurrentRoomFlags(_mem, petControl) : null;
        if (!ChevronCodes.TryGetCode(roomName, liveRoomFlags, out uint code))
            return;

        List<GameState.MailItem> items = GameState.ReadMailItems(_mem, _currentMailManRoom.Value);
        int delivered = 0;
        foreach (GameState.MailItem item in items)
        {
            if (!item.ToolPlaced)
                continue;

            if (GameActions.SetItemMailDestination(_mem, item.Address, code))
                delivered++;
        }

        if (delivered > 0)
        {
            _lastMailItems = null;
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
        uint? liveRoomFlags = _currentInventoryRoom is long petControl ? GameState.ReadCurrentRoomFlags(_mem, petControl) : null;
        if (_currentRoomName is null || !ChevronCodes.TryGetCode(_currentRoomName, liveRoomFlags, out uint code))
        {
            ShowActionResult(false, "Current room has no SuccUBus station");
            return;
        }

        long itemAddr = (long)_lvMail.SelectedItems[0].Tag!;
        bool ok = GameActions.SetItemMailDestination(_mem, itemAddr, code);
        if (ok)
            GameActions.MarkItemAsToolPlaced(_mem, itemAddr);

        ShowActionResult(ok, $"Set mail destination to {_currentRoomName} (0x{code:X})");

        _lastMailItems = null;
    }
}
