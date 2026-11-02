using System.Runtime.InteropServices;
using Archipelago.MultiClient.Net.Colors;
using Archipelago.MultiClient.Net.Enums;

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
    public static readonly Color ErrorFill = ColorTranslator.FromHtml("#2E2021");

    // The standard AP client's colors (Archipelago's data/client.kv). The library's own palette differs from them, its green most of all
    private static readonly Color ApRed = ColorTranslator.FromHtml("#EE0000");
    private static readonly Color ApGreen = ColorTranslator.FromHtml("#00FF7F");
    private static readonly Color ApYellow = ColorTranslator.FromHtml("#FAFAD2");
    private static readonly Color ApBlue = ColorTranslator.FromHtml("#6495ED");
    private static readonly Color ApMagenta = ColorTranslator.FromHtml("#EE00EE");
    private static readonly Color ApCyan = ColorTranslator.FromHtml("#00EEEE");
    private static readonly Color ApSlateBlue = ColorTranslator.FromHtml("#6D8BE8");
    private static readonly Color ApPlum = ColorTranslator.FromHtml("#AF99EF");
    private static readonly Color ApSalmon = ColorTranslator.FromHtml("#FA8072");

    /// <summary>A check with a hint on it that matters.</summary>
    public static readonly Color HintedDot = ApBlue;

    /// <summary>A check with a hint on it saying it can be ignored.</summary>
    public static readonly Color AvoidDot = ApSalmon;

    public static readonly Color LocationText = ApGreen;
    public static readonly Color EntranceText = ApBlue;

    /// <summary>A palette color from an AP message. White or unspecified is plain text, so it stays the normal text color.</summary>
    public static Color ApColor(PaletteColor? color) => color switch
    {
        PaletteColor.Black => Color.Black,
        PaletteColor.Red => ApRed,
        PaletteColor.Green => ApGreen,
        PaletteColor.Yellow => ApYellow,
        PaletteColor.Blue => ApBlue,
        PaletteColor.Magenta => ApMagenta,
        PaletteColor.Cyan => ApCyan,
        PaletteColor.SlateBlue => ApSlateBlue,
        PaletteColor.Plum => ApPlum,
        PaletteColor.Salmon => ApSalmon,
        _ => Text,
    };

    public static Color SlotColor(bool isThisSlot) => isThisSlot ? ApMagenta : ApYellow;

    /// <summary>An item's color by its type. A trap flag is read first, as an item can have more than one flag.</summary>
    public static Color ItemColor(ItemFlags flags) => true switch
    {
        _ when flags.HasFlag(ItemFlags.Trap) => ApSalmon,
        _ when flags.HasFlag(ItemFlags.Advancement) => ApPlum,
        _ when flags.HasFlag(ItemFlags.NeverExclude) => ApSlateBlue,
        _ => ApCyan,
    };

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
