namespace StarshipTitanicAp;

public sealed partial class MainForm
{
    // --- Addresses tab ---
    private readonly ListView _lvAddresses = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        GridLines = true,
        MultiSelect = false,
    };
    private readonly Button _btnCopyAddress = new() { Text = "Copy Address", Width = 110, Enabled = false };
    private readonly Dictionary<string, ListViewItem> _addressRows = new();

    private TabPage BuildAddressesTab()
    {
        var page = new TabPage("Addresses");

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(8, 4, 8, 4) };
        buttonPanel.Controls.Add(_btnCopyAddress);

        _lvAddresses.Columns.Add("Field", 220);
        _lvAddresses.Columns.Add("Address", 180);

        AddAddressRow("Module base");
        AddAddressRow("gameManager");
        AddAddressRow("_project");
        AddAddressRow("Player Inventory (CPetControl)");
        AddAddressRow("Mail Inventory (CMailMan)");
        AddAddressRow("PassengerClass field");
        AddAddressRow("Conversations (CPetConversations)");

        page.Controls.Add(_lvAddresses);
        page.Controls.Add(buttonPanel);
        return page;
    }

    private void AddAddressRow(string label)
    {
        var lvi = new ListViewItem(label) { Tag = (string?)null };
        lvi.SubItems.Add("-");
        _addressRows[label] = lvi;
        _lvAddresses.Items.Add(lvi);
    }

    private void SetAddressRow(string label, long? value)
    {
        if (!_addressRows.TryGetValue(label, out ListViewItem? lvi))
            return;

        string text = value is null ? "-" : $"0x{value.Value:X}";
        lvi.SubItems[1].Text = text;
        lvi.Tag = value is null ? null : text;
    }

    private void DoCopyAddressRow()
    {
        if (_lvAddresses.SelectedItems.Count == 0)
        {
            ShowActionResult(false, "Select a row first");
            return;
        }

        ListViewItem selected = _lvAddresses.SelectedItems[0];
        if (selected.Tag is not string addrText)
        {
            ShowActionResult(false, "No address resolved for this field yet");
            return;
        }

        Clipboard.SetText(addrText);
        ShowActionResult(true, $"Copied {selected.Text}: {addrText}");
    }
}
