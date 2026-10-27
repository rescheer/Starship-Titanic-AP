namespace StarshipTitanicAp;

public sealed partial class DebugForm
{
    // --- Live tab ---
    private readonly Label _lblRoomNodeView = new() { Text = "Room: -   Node: -   View: -", AutoSize = true };
    private readonly Label _lblCurrentLocation = new() { Text = "Location: -", AutoSize = true };
    private readonly Label _lblClass = new() { Text = "Class: -", AutoSize = true };
    private readonly Label _lblSaveSeedGuardStatus = new() { Text = "", AutoSize = true, ForeColor = Color.DimGray };
    private readonly ComboBox _cmbClass = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly Button _btnSetClass = new() { Text = "Set Class", Width = 100 };

    private TabPage BuildLiveTab()
    {
        var page = new TabPage("Live");

        var rnvPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(8, 8, 8, 0) };
        rnvPanel.Controls.Add(_lblRoomNodeView);

        var locationPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(8, 0, 8, 0) };
        locationPanel.Controls.Add(_lblCurrentLocation);

        var classPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(8, 0, 8, 0) };
        classPanel.Controls.Add(_lblClass);

        var guardPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(8, 0, 8, 0) };
        guardPanel.Controls.Add(_lblSaveSeedGuardStatus);

        _cmbClass.Items.AddRange(new object[] { "1 - First Class", "2 - Second Class", "3 - Third Class", "4 - No Class" });
        _cmbClass.SelectedIndex = 0;

        var setClassPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(8, 0, 8, 0) };
        setClassPanel.Controls.Add(_cmbClass);
        setClassPanel.Controls.Add(_btnSetClass);

        page.Controls.Add(setClassPanel);
        page.Controls.Add(guardPanel);
        page.Controls.Add(classPanel);
        page.Controls.Add(locationPanel);
        page.Controls.Add(rnvPanel);
        return page;
    }

    private void DoSetClass()
    {
        if (!RequireAttachedAndResolved(out long gameManager))
            return;

        int newClass = _cmbClass.SelectedIndex + 1;

        if (_session.InventoryRoom is null)
        {
            bool wroteOnly = GameActions.SetPassengerClass(_session.Memory, gameManager, newClass);
            ShowActionResult(wroteOnly, $"Set class to {newClass} (CPetControl not resolved yet - color won't refresh immediately)");
            return;
        }

        bool ok = GameActions.SetPassengerClassFull(_session.Memory, gameManager, _session.InventoryRoom.Value, newClass);
        ShowActionResult(ok, $"Set class to {newClass}");
    }

    /// <summary>Reflects the save/AP-seed guard's current state; read-only display data (Live/Mail/Items tabs, RNV,
    /// location, class) keeps updating regardless, but this tells the player when AP-facing writes (item syncing,
    /// location checks, mail delivery, etc.) are paused because the guard hasn't verified this save yet.</summary>
    private void UpdateSaveSeedGuardStatusLabel()
    {
        if (!_session.Options.EnforceSaveSeedGuard)
        {
            _lblSaveSeedGuardStatus.Text = "";
            return;
        }

        _lblSaveSeedGuardStatus.Text = _session.SaveSeedGuardState switch
        {
            SaveSeedGuardState.Ok => "",
            SaveSeedGuardState.Blocked => "AP syncing PAUSED: save belongs to a different AP seed (see Log)",
            _ => "AP syncing paused: waiting on AP connection to verify this save",
        };
    }

    /// <summary>Formats a decoded roomFlags value for display on the Live tab.</summary>
    private static string FormatCurrentLocation(uint roomFlags)
    {
        if (RoomFlags.IsNamedRoom(roomFlags))
        {
            string name = ChevronCodes.TryGetRoomName(roomFlags) ?? $"Unknown static room (0x{roomFlags:X})";
            return $"Static Room: {name}";
        }

        var (elevatorNum, _, floorNum, roomNum) = RoomFlags.Decode(roomFlags);
        return $"Dynamic Room (Elevator {elevatorNum}, Floor {floorNum}, Room {roomNum})";
    }
}
