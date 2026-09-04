namespace StarshipTitanicAp;

public sealed partial class MainForm
{
    // --- Live tab ---
    private readonly Label _lblRoomNodeView = new() { Text = "Room: -   Node: -   View: -", AutoSize = true };
    private readonly Label _lblCurrentLocation = new() { Text = "Location: -", AutoSize = true };
    private readonly Label _lblClass = new() { Text = "Class: -", AutoSize = true };
    private readonly Label _lblSaveSeedGuardStatus = new() { Text = "", AutoSize = true, ForeColor = Color.DimGray };

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

        page.Controls.Add(guardPanel);
        page.Controls.Add(classPanel);
        page.Controls.Add(locationPanel);
        page.Controls.Add(rnvPanel);
        return page;
    }

    /// <summary>Reflects the save/AP-seed guard's current state; read-only display data (Live/Mail/Items tabs, RNV,
    /// location, class) keeps updating regardless, but this tells the player when AP-facing writes (item syncing,
    /// location checks, mail delivery, etc.) are paused because the guard hasn't verified this save yet.</summary>
    private void UpdateSaveSeedGuardStatusLabel()
    {
        if (!_chkEnforceSaveSeedGuard.Checked)
        {
            _lblSaveSeedGuardStatus.Text = "";
            return;
        }

        _lblSaveSeedGuardStatus.Text = _saveSeedGuardState switch
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
