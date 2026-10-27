namespace StarshipTitanicAp;

public sealed partial class MainForm
{
    // --- Items tab ---
    private readonly ListView _lvInventory = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        GridLines = true,
        MultiSelect = false,
    };
    private readonly Button _btnCopyInventoryAddress = new() { Text = "Copy Address", Width = 140, Enabled = false };
    private readonly Button _btnCopyParentAddress = new() { Text = "Copy Parent Address", Width = 140, Enabled = false };
    private readonly Button _btnRefreshAllItems = new() { Text = "Refresh", Width = 140 };
    private readonly Button _btnExportParentSnapshot = new() { Text = "Export Parent Snapshot", Width = 160 };
    private readonly Label _lblMoveTo = new() { Text = "Move to...", AutoSize = true, Margin = new Padding(3, 12, 3, 2) };
    private readonly Button _btnMoveToInventory = new() { Text = "Inventory", Width = 140, Enabled = false };
    private readonly Button _btnMoveToMailman = new() { Text = "Mailman", Width = 140, Enabled = false };
    private readonly Button _btnMoveToHiddenRoom = new() { Text = "HiddenRoom", Width = 140, Enabled = false };
    private readonly Button _btnViewFields = new() { Text = "View Fields", Width = 140, Enabled = false, Margin = new Padding(3, 12, 3, 2) };
    private readonly Label _lblAllItemsStatus = new() { Text = "", AutoSize = true, Margin = new Padding(12, 4, 0, 0), ForeColor = Color.DimGray };

    private enum MoveDestination { Inventory, Mailman, HiddenRoom }

    private TabPage BuildItemsTab()
    {
        var page = new TabPage("Items");

        var listLabel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 24,
            Padding = new Padding(8, 4, 0, 0),
            WrapContents = false,
        };
        listLabel.Controls.Add(new Label { Text = "All items:", AutoSize = true });
        listLabel.Controls.Add(_lblAllItemsStatus);

        var listButtonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 4, 8, 4),
        };
        listButtonPanel.Controls.Add(_btnCopyInventoryAddress);
        listButtonPanel.Controls.Add(_btnCopyParentAddress);
        listButtonPanel.Controls.Add(_btnRefreshAllItems);
        listButtonPanel.Controls.Add(_btnExportParentSnapshot);
        listButtonPanel.Controls.Add(_lblMoveTo);
        listButtonPanel.Controls.Add(_btnMoveToInventory);
        listButtonPanel.Controls.Add(_btnMoveToMailman);
        listButtonPanel.Controls.Add(_btnMoveToHiddenRoom);
        listButtonPanel.Controls.Add(_btnViewFields);

        _lvInventory.Columns.Add("Item", 180);
        _lvInventory.Columns.Add("Location", 200);
        _lvInventory.Columns.Add("Address", 140);
        _lvInventory.Columns.Add("Parent Address", 140);

        page.Controls.Add(_lvInventory);
        page.Controls.Add(listButtonPanel);
        page.Controls.Add(listLabel);
        return page;
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

            var locationSub = new ListViewItem.ListViewSubItem(lvi, LocationLabel(item.ParentAddress, item.ParentName))
            {
                Tag = item.ParentAddress,
            };
            lvi.SubItems.Add(locationSub);
            lvi.SubItems.Add($"0x{item.Address:X}");
            lvi.SubItems.Add(item.ParentAddress is long parentAddr ? $"0x{parentAddr:X}" : "(unknown)");
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

    /// <summary>Writes the current full item/parent listing to a timestamped CSV under %AppData%\StarshipTitanicAp\ParentSnapshots.</summary>
    private void DoExportParentSnapshot()
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

        List<CarryItemLocation> items = GameState.FindAllCarryItems(_mem, _currentProject.Value);
        RoomNodeView? rnv = _currentGameManager is long gm ? GameState.ReadRoomNodeView(_mem, gm) : null;

        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StarshipTitanicAp", "ParentSnapshots");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"snapshot_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# ModuleBase=0x{_mem.ModuleBase:X} PID={_mem.ProcessId} CapturedAt={DateTime.Now:O} RNV={(rnv is { } r ? $"{r.Room}/{r.Node}/{r.View}" : "(unknown)")}");
        sb.AppendLine("ItemName,ItemAddress,ItemAddrDelta,ParentAddress,ParentAddrDelta,ParentName,ParentClassName,"
            + "AncestorChain,ChainDepth,ChainStopReason,"
            + "RoomAddr,RoomAddrDelta,RoomNameAt0x40,RoomNameScan,RoomClass");

        foreach (CarryItemLocation item in items.OrderBy(i => i.Name))
        {
            string parentClass = item.ParentAddress is long pAddr
                ? GameState.TryGetClassName(_mem, pAddr) ?? ""
                : "";
            string itemDelta = $"0x{item.Address - _mem.ModuleBase:X}";
            string parentAddrStr = item.ParentAddress is long pa ? $"0x{pa:X}" : "";
            string parentDelta = item.ParentAddress is long pa2 ? $"0x{pa2 - _mem.ModuleBase:X}" : "";

            (long? roomAddr, string chain, int depth, string stopReason) = WalkToRoom(item.ParentAddress);

            string rAddr = "", rDelta = "", rName40 = "", rNameScan = "", rClass = "";
            if (roomAddr is long ra)
            {
                rAddr = $"0x{ra:X}";
                rDelta = $"0x{ra - _mem.ModuleBase:X}";
                rName40 = DescribeNameAt0x40(ra);
                rNameScan = TryReadNameSafe(ra);
                rClass = GameState.TryGetClassName(_mem, ra) ?? "";
            }

            sb.AppendLine(string.Join(",",
                CsvField(item.Name),
                $"0x{item.Address:X}",
                itemDelta,
                parentAddrStr,
                parentDelta,
                CsvField(item.ParentName ?? ""),
                CsvField(parentClass),
                CsvField(chain),
                depth.ToString(),
                CsvField(stopReason),
                CsvField(rAddr), CsvField(rDelta), CsvField(rName40), CsvField(rNameScan), CsvField(rClass)));
        }

        File.WriteAllText(path, sb.ToString());
        ShowActionResult(true, $"Exported {items.Count} items to {path}");
    }

    /// <summary>Walks Parent repeatedly starting from an item's immediate parent until it reaches a CRoomItem.</summary>
    private (long? roomAddr, string chain, int depth, string stopReason) WalkToRoom(long? startParent)
    {
        const int maxDepth = 16;
        var chainParts = new List<string>();
        var visited = new HashSet<long>();

        long? current = startParent;
        int depth = 0;

        while (true)
        {
            if (current is not long addr || addr == 0)
                return (null, string.Join(" > ", chainParts), depth, "null parent");

            if (!visited.Add(addr))
                return (null, string.Join(" > ", chainParts), depth, "cycle detected");

            string className = GameState.TryGetClassName(_mem, addr) ?? "(unknown class)";
            string name = TryReadNameSafe(addr);
            string name40 = DescribeNameAt0x40(addr);
            string displayName = !string.IsNullOrEmpty(name40) ? name40
                : !string.IsNullOrEmpty(name) ? name
                : "(unnamed)";
            chainParts.Add($"{className}:{displayName}"
                + (name40 != name ? $" [scan={(string.IsNullOrEmpty(name) ? "-" : name)} direct40={(string.IsNullOrEmpty(name40) ? "-" : name40)}]" : "")
                + $"@0x{addr:X}");
            depth++;

            if (className == "CRoomItem")
                return (addr, string.Join(" > ", chainParts), depth, "reached CRoomItem");

            if (depth >= maxDepth)
                return (null, string.Join(" > ", chainParts), depth, "depth cap reached");

            current = _mem.ReadInt64(addr + GameOffsets.Parent);
        }
    }

    /// <summary>Best-effort read of the hypothesized inline _name text at object+0x40.</summary>
    private string DescribeNameAt0x40(long addr)
    {
        byte[]? raw = _mem.ReadBytes(addr + 0x40, 24);
        if (raw is null)
            return "";

        var chars = new System.Text.StringBuilder();
        foreach (byte b in raw)
        {
            if (b == 0)
                break;
            if (b < 0x20 || b > 0x7E)
                return "";
            chars.Append((char)b);
        }
        return chars.Length > 0 ? chars.ToString() : "";
    }

    /// <summary>Null-safe wrapper around GameState.TryReadName for the export.</summary>
    private string TryReadNameSafe(long addr) => GameState.TryReadName(_mem, addr) ?? "";

    private static string CsvField(string value) =>
        value.Contains(',') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

    /// <summary>Opens a live, read-only viewer over every known field offset on the selected item.</summary>
    private void DoViewFields()
    {
        if (_lvInventory.SelectedItems.Count == 0)
        {
            ShowActionResult(false, "Select an item first");
            return;
        }

        ListViewItem selected = _lvInventory.SelectedItems[0];
        long itemAddr = (long)selected.Tag!;
        var form = new ItemFieldsForm(_mem, itemAddr, selected.Text);
        form.Show(this);
    }

    /// <summary>Friendly label for an item's current parent.</summary>
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

        string? className = GameState.TryGetClassName(_mem, parentAddr.Value);
        if (!string.IsNullOrEmpty(className))
            return className;

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

    private void DoCopyParentAddress()
    {
        if (_lvInventory.SelectedItems.Count == 0)
        {
            ShowActionResult(false, "Select an item first");
            return;
        }

        long? parentAddr = _lvInventory.SelectedItems[0].SubItems[1].Tag as long?;
        if (parentAddr is null)
        {
            ShowActionResult(false, "Parent address unknown");
            return;
        }

        Clipboard.SetText($"0x{parentAddr.Value:X}");
        ShowActionResult(true, $"Copied parent address 0x{parentAddr.Value:X}");
    }

    private void DoMoveSelectedTo(MoveDestination destination)
    {
        if (!RequireAttachedAndResolved(out long gameManager))
            return;

        if (_lvInventory.SelectedItems.Count == 0)
        {
            ShowActionResult(false, "Select an item first");
            return;
        }
        long itemAddr = (long)_lvInventory.SelectedItems[0].Tag!;

        if (destination == MoveDestination.HiddenRoom)
        {
            DoMoveSelectedToHiddenRoom(itemAddr, gameManager);
            return;
        }

        long roomAddr;
        bool toMail = destination == MoveDestination.Mailman;
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

    /// <summary>Handles the HiddenRoom case separately from MoveItemAndReport.</summary>
    private void DoMoveSelectedToHiddenRoom(long itemAddr, long gameManager)
    {
        bool ok = _currentInventoryRoom is not null
            ? GameActions.MoveItemToHiddenRoomFull(_mem, itemAddr, _currentInventoryRoom.Value, gameManager)
            : GameActions.MoveItemToHiddenRoom(_mem, itemAddr);

        if (ok)
        {
            if (GameActions.IsItemToolPlaced(_mem, itemAddr))
                GameActions.UnmarkItemAsToolPlaced(_mem, itemAddr);

            _lastMailItems = null;
            DoRefreshAllItems();
        }

        ShowActionResult(ok, $"Move item 0x{itemAddr:X} -> hidden room");
    }

    /// <summary>Shared move+refresh+report logic for all three "move item" entry points.</summary>
    private void MoveItemAndReport(long gameManager, long itemAddr, long roomAddr, bool isMailDestination)
    {
        bool ok = GameActions.MoveItemSmart(_mem, itemAddr, roomAddr, _currentInventoryRoom, gameManager);
        string message;

        if (ok && isMailDestination)
        {
            string destRoomName = _currentRoomName is not null && ChevronCodes.HasStation(_currentRoomName)
                ? _currentRoomName
                : "EmbLobby";
            uint? liveRoomFlags = _currentInventoryRoom is long petControl ? GameState.ReadCurrentRoomFlags(_mem, petControl) : null;
            ChevronCodes.TryGetCode(destRoomName, liveRoomFlags, out uint code);

            ok = GameActions.SetItemMailDestination(_mem, itemAddr, code);
            if (ok)
                GameActions.MarkItemAsToolPlaced(_mem, itemAddr);

            _lastMailItems = null;
            message = $"Move item 0x{itemAddr:X} -> mail, destination {destRoomName}";
        }
        else
        {
            if (ok && GameActions.IsItemToolPlaced(_mem, itemAddr))
                GameActions.UnmarkItemAsToolPlaced(_mem, itemAddr);
            message = $"Move item 0x{itemAddr:X} -> 0x{roomAddr:X}";
        }

        if (ok)
            DoRefreshAllItems();

        ShowActionResult(ok, message);
    }
}