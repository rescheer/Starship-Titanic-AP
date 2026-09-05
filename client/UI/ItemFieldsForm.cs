namespace StarshipTitanicAp;

/// <summary>Diagnostic viewer showing every known CGameObject/CTreeItem field offset on one item.</summary>
public sealed class ItemFieldsForm : Form
{
    private readonly MemoryReader _mem;
    private readonly long _itemAddr;

    private readonly ListView _lv = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        GridLines = true,
    };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };

    private enum FieldKind { Pointer, Int32, UInt32Hex, Byte, Bool, CString, PackedPoint, Rect, Bytes }

    private readonly record struct FieldDef(long Offset, string Name, FieldKind Kind, int Size = 0);

    private static readonly FieldDef[] Fields =
    {
        new(0x00, "_vtable", FieldKind.Pointer),
        new(0x08, "_parent (GameOffsets.Parent)", FieldKind.Pointer),
        new(0x10, "_nextSibling (GameOffsets.NextSibling)", FieldKind.Pointer),
        new(0x18, "_priorSibling", FieldKind.Pointer),
        new(0x20, "_firstChild (GameOffsets.FirstChild)", FieldKind.Pointer),
        new(0x30, "_name (NamedItemNameOffset)", FieldKind.CString),
        new(0x58, "_unused1 (this app's tool-placed mail marker)", FieldKind.UInt32Hex),
        new(0x60, "_unused2", FieldKind.Bytes, 8),
        new(0x68, "_unused3", FieldKind.Bytes, 8),
        new(0x70, "_nonvisual", FieldKind.Byte),
        new(0x71, "_toggleR", FieldKind.Byte),
        new(0x72, "_toggleG", FieldKind.Byte),
        new(0x73, "_toggleB", FieldKind.Byte),
        new(0x78, "_movieClips", FieldKind.Bytes, 0x18),
        new(0x90, "_initialFrame", FieldKind.Int32),
        new(0x98, "_movieRangeInfoList", FieldKind.Bytes, 0x18),
        new(0xB0, "_frameNumber", FieldKind.Int32),
        new(0xB8, "_text", FieldKind.Pointer),
        new(0xC0, "_textBorder", FieldKind.UInt32Hex),
        new(0xC4, "_textBorderRight", FieldKind.UInt32Hex),
        new(0xC8, "_savedPos", FieldKind.PackedPoint),
        new(0xD0, "_surface (GameObjectSurfaceOffset, READ-ONLY - never write)", FieldKind.Pointer),
        new(0xD8, "_resource (GameObjectResourceOffset, READ-ONLY - never write)", FieldKind.CString),
        new(0x100, "_unused4 (this app's ItemPersistedState)", FieldKind.Int32),
        new(0x104, "_bounds", FieldKind.Rect),
        new(0x10C, "_isPendingMail (ItemIsPendingMail)", FieldKind.Bool),
        new(0x110, "_destRoomFlags (ItemDestRoomFlags)", FieldKind.UInt32Hex),
        new(0x114, "_roomFlags (ItemRoomFlags)", FieldKind.UInt32Hex),
        new(0x118, "_handleMouseFlag", FieldKind.Bool),
        new(0x11C, "_cursorId", FieldKind.Int32),
        new(0x120, "_visible", FieldKind.Bool),

        new(0x124, "_unused5 (CCarry candidate)", FieldKind.Int32),
        new(0x128, "_doesNothingMsg (CCarry candidate)", FieldKind.CString),
        new(0x150, "_doesntWantMsg (CCarry candidate)", FieldKind.CString),
        new(0x178, "_unusedR (CCarry candidate)", FieldKind.Int32),
        new(0x17C, "_unusedG (CCarry candidate)", FieldKind.Int32),
        new(0x180, "_unusedB (CCarry candidate)", FieldKind.Int32),
        new(0x184, "_itemFrame (CCarry candidate)", FieldKind.Int32),
        new(0x188, "_unused6 (CCarry candidate)", FieldKind.CString),
        new(0x1B0, "_enterFrame (CCarry candidate)", FieldKind.Int32),
        new(0x1B4, "_enterFrameSet (CCarry candidate)", FieldKind.Bool),
        new(0x1B8, "_centroid (CCarry candidate)", FieldKind.PackedPoint),
        new(0x1BC, "_visibleFrame (CCarry candidate)", FieldKind.Int32),
        new(0x1C0, "_npcUse (CCarry candidate)", FieldKind.CString),
        new(0x1E8, "_canTake (CCarry candidate)", FieldKind.Bool),
        new(0x1EC, "_origPos (CCarry candidate)", FieldKind.PackedPoint),
        new(0x1F0, "_fullViewName (CCarry candidate)", FieldKind.CString),
    };

    public ItemFieldsForm(MemoryReader mem, long itemAddr, string itemName)
    {
        _mem = mem;
        _itemAddr = itemAddr;

        Text = $"Fields - {itemName} @ 0x{itemAddr:X}";
        Width = 760;
        Height = 600;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;

        _lv.Columns.Add("Offset", 70);
        _lv.Columns.Add("Field", 300);
        _lv.Columns.Add("Value", 220);
        _lv.Columns.Add("Notes", 220);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(8, 4, 8, 4) };
        var btnRefresh = new Button { Text = "Refresh", Width = 90 };
        btnRefresh.Click += (_, _) => RefreshValues();
        var btnCopyAddr = new Button { Text = "Copy Item Address", Width = 140 };
        btnCopyAddr.Click += (_, _) => Clipboard.SetText($"0x{_itemAddr:X}");
        buttonPanel.Controls.Add(btnRefresh);
        buttonPanel.Controls.Add(btnCopyAddr);

        Controls.Add(_lv);
        Controls.Add(buttonPanel);

        foreach (FieldDef f in Fields)
        {
            var lvi = new ListViewItem($"+0x{f.Offset:X}") { Tag = f };
            lvi.SubItems.Add(f.Name);
            lvi.SubItems.Add("");
            lvi.SubItems.Add("");
            _lv.Items.Add(lvi);
        }

        _timer.Tick += (_, _) => RefreshValues();
        FormClosed += (_, _) => _timer.Stop();
        Load += (_, _) => _timer.Start();

        RefreshValues();
    }

    private void RefreshValues()
    {
        if (!_mem.IsAttached)
        {
            foreach (ListViewItem lvi in _lv.Items)
            {
                lvi.SubItems[2].Text = "(not attached)";
                lvi.SubItems[3].Text = "";
            }
            return;
        }

        foreach (ListViewItem lvi in _lv.Items)
        {
            var f = (FieldDef)lvi.Tag!;
            (string value, string notes) = ReadField(_itemAddr + f.Offset, f);
            lvi.SubItems[2].Text = value;
            lvi.SubItems[3].Text = notes;
        }
    }

    private (string value, string notes) ReadField(long addr, FieldDef f)
    {
        switch (f.Kind)
        {
            case FieldKind.Pointer:
            {
                long? v = _mem.ReadInt64(addr);
                return (v is long p ? $"0x{p:X}" : "(read failed)", "");
            }

            case FieldKind.Int32:
            {
                int? v = _mem.ReadInt32(addr);
                if (v is not int raw)
                    return ("(read failed)", "");

                string notes = "";
                if (f.Offset == 0x100)
                {
                    ItemPersistedState st = ItemPersistedState.Decode(raw);
                    notes = $"Stage={st.Stage}, CheckFired={st.CheckFired}, PulledFrom={st.PulledFrom}";
                }
                return (raw.ToString(), notes);
            }

            case FieldKind.UInt32Hex:
            {
                int? v = _mem.ReadInt32(addr);
                if (v is not int raw)
                    return ("(read failed)", "");

                uint u = unchecked((uint)raw);
                string notes = "";
                if (f.Offset == 0x58 && u == GameOffsets.ToolPlacedSentinel)
                    notes = "ToolPlacedSentinel (this app's manual mail-placement marker)";
                else if (f.Offset is 0x110 or 0x114 && ChevronCodes.TryGetRoomName(u) is string roomName)
                    notes = $"= {roomName}";
                return ($"0x{u:X8}", notes);
            }

            case FieldKind.Byte:
            {
                byte[]? b = _mem.ReadBytes(addr, 1);
                return (b is null ? "(read failed)" : $"0x{b[0]:X2}", "");
            }

            case FieldKind.Bool:
            {
                byte[]? b = _mem.ReadBytes(addr, 1);
                return (b is null ? "(read failed)" : (b[0] != 0).ToString(), "");
            }

            case FieldKind.PackedPoint:
            {
                int? v = _mem.ReadInt32(addr);
                if (v is not int raw)
                    return ("(read failed)", "");

                short x = unchecked((short)(raw & 0xFFFF));
                short y = unchecked((short)((raw >> 16) & 0xFFFF));
                return ($"{x}, {y}", "");
            }

            case FieldKind.Rect:
            {
                int? lt = _mem.ReadInt32(addr);
                int? rb = _mem.ReadInt32(addr + 4);
                if (lt is not int ltv || rb is not int rbv)
                    return ("(read failed)", "");

                short l = unchecked((short)(ltv & 0xFFFF));
                short t = unchecked((short)((ltv >> 16) & 0xFFFF));
                short r = unchecked((short)(rbv & 0xFFFF));
                short b = unchecked((short)((rbv >> 16) & 0xFFFF));
                return ($"L{l} T{t} R{r} B{b}", "");
            }

            case FieldKind.CString:
            {
                int? size = _mem.ReadInt32(addr);
                long? dataPtr = _mem.ReadInt64(addr + 8);
                if (size is not int sz || dataPtr is not long dp)
                    return ("(read failed)", "");
                if (sz <= 0)
                    return ("(empty)", "size=0");

                string? text = _mem.ReadShortAsciiString(dp, Math.Min(sz, 128));
                return (text ?? "(unreadable/non-ASCII)", $"size={sz}");
            }

            case FieldKind.Bytes:
            {
                byte[]? raw = _mem.ReadBytes(addr, f.Size);
                return (raw is null ? "(read failed)" : Convert.ToHexString(raw), "");
            }

            default:
                return ("", "");
        }
    }
}
