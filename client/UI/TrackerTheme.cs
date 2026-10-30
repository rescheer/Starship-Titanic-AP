using System.Runtime.InteropServices;

namespace StarshipTitanicAp;

/// <summary>The main window's dark look: its colors, fonts and the Windows calls that darken what WinForms can't.</summary>
internal static class TrackerTheme
{
    public static readonly Color Background = ColorTranslator.FromHtml("#16181C");
    public static readonly Color Surface = ColorTranslator.FromHtml("#1D2025");
    public static readonly Color CompleteFill = ColorTranslator.FromHtml("#24282E");
    public static readonly Color LogBackground = ColorTranslator.FromHtml("#0E0F12");
    public static readonly Color Line = ColorTranslator.FromHtml("#363A42");
    public static readonly Color LineDim = ColorTranslator.FromHtml("#2E3238");
    public static readonly Color LineStrong = ColorTranslator.FromHtml("#6A6F78");
    public static readonly Color Text = ColorTranslator.FromHtml("#ECEBE7");
    public static readonly Color TextMuted = ColorTranslator.FromHtml("#A5A9B0");
    public static readonly Color TextDim = ColorTranslator.FromHtml("#6E737C");
    public static readonly Color PipLocked = ColorTranslator.FromHtml("#5A5F68");
    public static readonly Color Accent = ColorTranslator.FromHtml("#D2B06A");
    public static readonly Color AccentFill = ColorTranslator.FromHtml("#2A2620");
    public static readonly Color Error = ColorTranslator.FromHtml("#FF8A80");

    // Ships with Windows 10 and later
    private static readonly string UiFamily = FontFamily.Families.Any(f => f.Name == "Bahnschrift") ? "Bahnschrift" : "Segoe UI";

    public static readonly Font CaptionFont = new(UiFamily, 8f);
    public static readonly Font TileNameFont = new(UiFamily, 9f);
    public static readonly Font BodyFont = new(UiFamily, 10f);
    public static readonly Font MarkFont = new("Segoe UI Symbol", 20f);
    public static readonly Font PartCountFont = new(UiFamily, 20f);
    public static readonly Font ParrotValueFont = new(UiFamily, 42f);
    public static readonly Font ClueCountFont = new(UiFamily, 22f);
    public static readonly Font LogFont = new("Segoe UI", 10f);
    public static readonly Font LogBoldFont = new("Segoe UI", 10f, FontStyle.Bold);
    public static readonly Font RecentFont = new("Segoe UI", 9.5f);

    public static void StyleButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = Background;
        button.ForeColor = Text;
        button.Font = TileNameFont;
        button.FlatAppearance.BorderColor = LineStrong;
        button.FlatAppearance.MouseOverBackColor = Surface;
        button.FlatAppearance.MouseDownBackColor = AccentFill;
        button.UseVisualStyleBackColor = false;
    }

    public static void StyleTextBox(TextBox textBox)
    {
        textBox.BackColor = LogBackground;
        textBox.ForeColor = Text;
        textBox.BorderStyle = BorderStyle.FixedSingle;
    }

    /// <summary>Asks Windows for a dark title bar. Harmless on versions that don't support it.</summary>
    public static void UseDarkTitleBar(Form form)
    {
        const int useImmersiveDarkMode = 20;
        int enabled = 1;
        _ = DwmSetWindowAttribute(form.Handle, useImmersiveDarkMode, ref enabled, sizeof(int));
    }

    /// <summary>Asks Windows for dark scrollbars on a control.</summary>
    public static void UseDarkScrollBars(Control control) => _ = SetWindowTheme(control.Handle, "DarkMode_Explorer", null);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);
}
