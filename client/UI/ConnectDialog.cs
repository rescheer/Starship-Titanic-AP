namespace StarshipTitanicAp;

/// <summary>Modal dialog for entering Archipelago server connection info.</summary>
public sealed class ConnectDialog : Form
{
    private readonly TextBox _txtServer = new() { Width = 220, PlaceholderText = "archipelago.gg:38281" };
    private readonly TextBox _txtSlot = new() { Width = 220, PlaceholderText = "Slot name" };
    private readonly TextBox _txtPassword = new() { Width = 220, PlaceholderText = "(optional)", UseSystemPasswordChar = true };

    public string Server => _txtServer.Text.Trim();
    public string Slot => _txtSlot.Text.Trim();
    public string Password => _txtPassword.Text;

    public ConnectDialog(string server, string slot, string password)
    {
        Text = "Connect to Archipelago";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(10);

        _txtServer.Text = server;
        _txtSlot.Text = slot;
        _txtPassword.Text = password;

        var fieldsLayout = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        fieldsLayout.Controls.Add(new Label { Text = "Server:", AutoSize = true, Margin = new Padding(0, 6, 6, 0) }, 0, 0);
        fieldsLayout.Controls.Add(_txtServer, 1, 0);
        fieldsLayout.Controls.Add(new Label { Text = "Slot:", AutoSize = true, Margin = new Padding(0, 6, 6, 0) }, 0, 1);
        fieldsLayout.Controls.Add(_txtSlot, 1, 1);
        fieldsLayout.Controls.Add(new Label { Text = "Password:", AutoSize = true, Margin = new Padding(0, 6, 6, 0) }, 0, 2);
        fieldsLayout.Controls.Add(_txtPassword, 1, 2);

        var btnConnect = new Button { Text = "Connect", DialogResult = DialogResult.OK, Width = 90 };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };
        var buttonRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, 10, 0, 0),
        };
        buttonRow.Controls.Add(btnCancel);
        buttonRow.Controls.Add(btnConnect);

        var root = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
        };
        root.Controls.Add(fieldsLayout);
        root.Controls.Add(buttonRow);
        Controls.Add(root);

        AcceptButton = btnConnect;
        CancelButton = btnCancel;
    }
}
