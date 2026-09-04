namespace StarshipTitanicAp;

public sealed partial class MainForm
{
    // --- Log tab ---
    private readonly TextBox _txtLog = new()
    {
        Multiline = true,
        ReadOnly = true,
        Dock = DockStyle.Fill,
        ScrollBars = ScrollBars.Vertical,
        Font = new Font(FontFamily.GenericMonospace, 9),
        WordWrap = false,
    };
    private readonly Button _btnClearLog = new() { Text = "Clear Log", Width = 100, AutoSize = true };

    private TabPage BuildLogTab()
    {
        var page = new TabPage("Log");

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(8, 4, 8, 0) };
        toolbar.Controls.Add(_btnClearLog);

        page.Controls.Add(_txtLog);
        page.Controls.Add(toolbar);
        return page;
    }
}
