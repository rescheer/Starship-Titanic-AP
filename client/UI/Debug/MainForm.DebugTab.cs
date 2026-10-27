namespace StarshipTitanicAp;

public sealed partial class MainForm
{
    private readonly Button _btnDetach = new() { Text = "Detach", Width = 110, Enabled = false };

    private readonly TextBox _txtDebugLockedRooms = new() { Width = 300, PlaceholderText = "extra locked rooms, e.g. Bar, 12, PromenadeDeck" };
    private readonly CheckBox _chkAllowInitialUpgrade = new()
    {
        Text = "Allow initial upgrade (None -> Third) - no AP item for this yet",
        AutoSize = true,
        Checked = true,
    };
    private readonly CheckBox _chkEnforceSaveSeedGuard = new()
    {
        Text = "Enforce save/AP-seed guard",
        AutoSize = true,
        Checked = true,
    };
    private readonly Button _btnForceTagSaveSeed = new() { Text = "Force-Tag Save With Current Seed", Width = 240 };
    private readonly CheckBox _chkTakeUngrantedItems = new()
    {
        Text = "Take ungranted items away from player's inventory",
        AutoSize = true,
        Checked = true,
    };
    private readonly TextBox _txtItemAddr = new() { Width = 150, PlaceholderText = "item address (hex)" };
    private readonly TextBox _txtRoomAddrOverride = new() { Width = 150, PlaceholderText = "room address (hex)" };
    private readonly Button _btnMoveItem = new() { Text = "Move", Width = 70 };
    private readonly TextBox _txtMsgText = new() { Width = 300, PlaceholderText = "free text message" };
    private readonly Button _btnDisplayMessageText = new() { Text = "Display Free Text", Width = 180 };

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

        layout.Controls.Add(SectionLabel("Connection"));
        layout.Controls.Add(_btnDetach);

        layout.Controls.Add(Spacer());
        layout.Controls.Add(SectionLabel("Save / AP-Seed Guard"));
        layout.Controls.Add(_chkEnforceSaveSeedGuard);
        layout.Controls.Add(_btnForceTagSaveSeed);

        layout.Controls.Add(Spacer());
        layout.Controls.Add(SectionLabel("Item Tracking"));
        layout.Controls.Add(_chkTakeUngrantedItems);

        layout.Controls.Add(Spacer());
        layout.Controls.Add(SectionLabel("Class Upgrade Lock"));
        layout.Controls.Add(_chkAllowInitialUpgrade);

        layout.Controls.Add(Spacer());
        layout.Controls.Add(SectionLabel("Room Key Gate - Extra Locked Rooms"));
        layout.Controls.Add(_txtDebugLockedRooms);

        layout.Controls.Add(Spacer());
        layout.Controls.Add(SectionLabel("PET Free Text Message"));
        layout.Controls.Add(_txtMsgText);
        layout.Controls.Add(_btnDisplayMessageText);

        layout.Controls.Add(Spacer());
        layout.Controls.Add(SectionLabel("Move Item to Custom Address"));
        var moveRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        moveRow.Controls.Add(_txtItemAddr);
        moveRow.Controls.Add(_txtRoomAddrOverride);
        moveRow.Controls.Add(_btnMoveItem);
        layout.Controls.Add(moveRow);

        page.Controls.Add(layout);
        return page;
    }

    private void DoDisplayMessageText()
    {
        if (!_session.Memory.IsAttached)
        {
            ShowActionResult(false, "Not attached");
            return;
        }
        if (string.IsNullOrEmpty(_txtMsgText.Text))
        {
            ShowActionResult(false, "Enter a message first");
            return;
        }
        if (_session.InventoryRoom is null)
        {
            ShowActionResult(false, "PET control address not resolved yet");
            return;
        }

        bool ok = GameActions.DisplayMessageSmart(_session.Memory, _session.InventoryRoom.Value, _txtMsgText.Text);
        ShowActionResult(ok, $"Displayed message: \"{_txtMsgText.Text}\"");
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
}
