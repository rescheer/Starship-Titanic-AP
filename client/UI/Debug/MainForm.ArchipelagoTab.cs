namespace StarshipTitanicAp;

public sealed partial class MainForm
{
    // --- Archipelago tab ---
    private readonly TextBox _txtApServer = new() { Width = 220, PlaceholderText = "archipelago.gg:38281" };
    private readonly TextBox _txtApSlot = new() { Width = 220, PlaceholderText = "Slot name" };
    private readonly TextBox _txtApPassword = new() { Width = 220, PlaceholderText = "(optional)", UseSystemPasswordChar = true };
    private readonly Button _btnApConnect = new() { Text = "Connect", Width = 100 };
    private readonly Button _btnApDisconnect = new() { Text = "Disconnect", Width = 100, Enabled = false };
    private readonly Label _lblApStatus = new() { Text = "\u25CF Not connected", AutoSize = true, ForeColor = Color.DimGray };
    private readonly Label _lblPendingChecks = new() { Text = "", AutoSize = true, ForeColor = Color.DimGray };
    private readonly Button _btnClearPendingChecks = new() { Text = "Clear Pending Checks", Width = 160, AutoSize = true };
    private readonly ListBox _lbPendingChecks = new() { Width = 340, Height = 160, IntegralHeight = false };

    private TabPage BuildArchipelagoTab()
    {
        var page = new TabPage("Archipelago");
        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(10),
            AutoScroll = true,
        };

        layout.Controls.Add(SectionLabel("Server Connection"));

        var serverRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        serverRow.Controls.Add(new Label { Text = "Server:", AutoSize = true, Margin = new Padding(0, 6, 4, 0), Width = 70 });
        serverRow.Controls.Add(_txtApServer);
        layout.Controls.Add(serverRow);

        var slotRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        slotRow.Controls.Add(new Label { Text = "Slot:", AutoSize = true, Margin = new Padding(0, 6, 4, 0), Width = 70 });
        slotRow.Controls.Add(_txtApSlot);
        layout.Controls.Add(slotRow);

        var passwordRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        passwordRow.Controls.Add(new Label { Text = "Password:", AutoSize = true, Margin = new Padding(0, 6, 4, 0), Width = 70 });
        passwordRow.Controls.Add(_txtApPassword);
        layout.Controls.Add(passwordRow);

        var buttonRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
        buttonRow.Controls.Add(_btnApConnect);
        buttonRow.Controls.Add(_btnApDisconnect);
        layout.Controls.Add(buttonRow);

        layout.Controls.Add(_lblApStatus);

        var pendingRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        pendingRow.Controls.Add(_lblPendingChecks);
        pendingRow.Controls.Add(_btnClearPendingChecks);
        layout.Controls.Add(pendingRow);

        layout.Controls.Add(_lbPendingChecks);

        page.Controls.Add(layout);
        return page;
    }

    private void DoApConnect()
    {
        SetApInputsEnabled(false);
        _session.Connect(_txtApServer.Text, _txtApSlot.Text, _txtApPassword.Text);
    }

    /// <summary>Handles ClientSession.ApStateChanged.</summary>
    private void OnApStateChanged(ApConnectionState state, string message)
    {
        switch (state)
        {
            case ApConnectionState.Connecting:
                _lblApStatus.Text = $"\u25CF {message}";
                _lblApStatus.ForeColor = Color.DarkOrange;
                break;
            case ApConnectionState.Connected:
                _lblApStatus.Text = $"\u25CF {message}";
                _lblApStatus.ForeColor = Color.DarkGreen;
                SetApInputsEnabled(false);
                _btnApDisconnect.Enabled = true;
                break;
            case ApConnectionState.ConnectionFailed:
                _lblApStatus.Text = $"\u25CF Connection failed: {message}";
                _lblApStatus.ForeColor = Color.DarkRed;
                SetApInputsEnabled(true);
                break;
            case ApConnectionState.Disconnected:
            default:
                _lblApStatus.Text = $"\u25CF {message}";
                _lblApStatus.ForeColor = Color.DimGray;
                SetApInputsEnabled(true);
                break;
        }

        UpdateApTopStatus();
        UpdateFooterApStatus();
        UpdateAccessCards();
        UpdateApToggleButton();
        UpdateInfoChecks();
        AppendLog(_lblApStatus.Text);
    }

    /// <summary>Refreshes the condensed AP status line shown in the top bar next to the Attach button.</summary>
    private void UpdateApTopStatus()
    {
        string detail = "";
        if (_session.Ap.State == ApConnectionState.Connected)
        {
            string server = _session.Connection.Server;
            string slot = _session.Connection.Slot;
            detail = $"  (Server: {(server.Length > 0 ? server : "-")}, Slot: {(slot.Length > 0 ? slot : "-")})";
        }

        _lblApTopStatus.Text = $"AP: {_session.Ap.StatusMessage}{detail}";
        _lblApTopStatus.ForeColor = _lblApStatus.ForeColor;
    }

    private void UpdatePendingChecksLabel()
    {
        string[] pending = _session.Ap.GetPendingCheckNames();
        int count = pending.Length;
        _lblPendingChecks.Text = count == 0
            ? ""
            : $"{count} location check{(count == 1 ? "" : "s")} queued, waiting to reconnect";

        _lbPendingChecks.BeginUpdate();
        _lbPendingChecks.Items.Clear();
        foreach (string name in pending.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            long? id = _session.Ap.ResolveLocationId(name);
            _lbPendingChecks.Items.Add(id is not null ? $"{name} ({id})" : name);
        }
        _lbPendingChecks.EndUpdate();
    }

    private void DoClearPendingChecks()
    {
        int count = _session.Ap.PendingCheckCount;
        _session.Ap.ClearPendingChecks();
        UpdatePendingChecksLabel();
        ShowActionResult(true, count == 0
            ? "No pending checks to clear"
            : $"Cleared {count} pending check{(count == 1 ? "" : "s")}");
    }

    private void SetApInputsEnabled(bool enabled)
    {
        _txtApServer.Enabled = enabled;
        _txtApSlot.Enabled = enabled;
        _txtApPassword.Enabled = enabled;
        _btnApConnect.Enabled = enabled;
        _btnApDisconnect.Enabled = !enabled;
    }
}
