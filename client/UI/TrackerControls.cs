using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;

namespace StarshipTitanicAp;

/// <summary>Base for the main window's custom-painted rectangles.</summary>
internal abstract class TrackerControl : Control
{
    protected const TextFormatFlags SingleLine = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

    protected TrackerControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        BackColor = TrackerTheme.Background;
        ForeColor = TrackerTheme.Text;
    }

    /// <summary>Scales a design-time pixel size for the monitor the window is on.</summary>
    protected int S(int pixels) => LogicalToDeviceUnits(pixels);

    protected void FillTile(Graphics g, Color fill, Color border)
    {
        using (var brush = new SolidBrush(fill))
            g.FillRectangle(brush, ClientRectangle);
        using var pen = new Pen(border);
        g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    /// <summary>Draws a check dot: filled once checked, otherwise a ring.</summary>
    protected void DrawPip(Graphics g, int x, int y, int size, Color color, bool filled)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (filled)
        {
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, x, y, size, size);
        }
        else
        {
            float thickness = Math.Max(1f, size / 6f);
            using var pen = new Pen(color, thickness);
            g.DrawEllipse(pen, x + thickness / 2, y + thickness / 2, size - thickness, size - thickness);
        }
        g.SmoothingMode = SmoothingMode.Default;
    }
}

/// <summary>What a room tile shows. Without checks (<paramref name="Total"/> 0) it's just a name and tag.</summary>
/// <param name="InLogic">How many of the room's unchecked checks can be done right now.</param>
/// <param name="IsLocked">Whether something's missing to enter the room, which dims the tile.</param>
/// <param name="IsHere">Whether the player's in this room, which highlights the tile.</param>
internal readonly record struct RoomTileState(string Name, int Checked, int InLogic, int Total, string Tag, bool IsLocked, bool IsHere, string ToolTip = "");

/// <summary>One room: its name, a dot per check, and a line on where it stands.</summary>
internal sealed class RoomTile : TrackerControl
{
    private RoomTileState _state;

    public RoomTileState State
    {
        get => _state;
        set
        {
            if (value == _state)
                return;
            _state = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        bool complete = _state.Total > 0 && _state.Checked == _state.Total;
        Color fill = _state.IsHere ? TrackerTheme.AccentFill : complete ? TrackerTheme.Surface : TrackerTheme.Background;
        Color border = true switch
        {
            _ when _state.IsHere => TrackerTheme.Accent,
            _ when _state.IsLocked => TrackerTheme.LineDim,
            _ when _state.Total == 0 => TrackerTheme.Line,
            _ => TrackerTheme.LineStrong,
        };
        Color nameColor = _state.IsLocked ? TrackerTheme.TextMuted : TrackerTheme.Text;
        FillTile(e.Graphics, fill, border);

        int left = S(6);
        TextRenderer.DrawText(e.Graphics, _state.Name, TrackerTheme.TileNameFont, new Rectangle(left, S(4), Width - left - S(2), S(16)),
            nameColor, SingleLine | TextFormatFlags.EndEllipsis);

        int pip = S(8), gap = S(3), x = left, y = S(22);
        for (int i = 0; i < _state.Total; i++)
        {
            if (x + pip > Width - S(2))
            {
                x = left;
                y += pip + gap;
            }
            if (i < _state.Checked)
                DrawPip(e.Graphics, x, y, pip, TrackerTheme.Accent, filled: true);
            else if (i < _state.Checked + _state.InLogic)
                DrawPip(e.Graphics, x, y, pip, TrackerTheme.Accent, filled: false);
            else
                DrawPip(e.Graphics, x, y, pip, TrackerTheme.PipLocked, filled: false);
            x += pip + gap;
        }

        TextRenderer.DrawText(e.Graphics, _state.Tag, TrackerTheme.CaptionFont, new Rectangle(left, Height - S(17), Width - left - S(2), S(14)),
            TrackerTheme.TextMuted, SingleLine | TextFormatFlags.EndEllipsis);
    }
}

internal enum GoalTileStyle
{
    /// <summary>Not known yet.</summary>
    Unknown,

    /// <summary>Known, and not in hand yet: a highlighted border only.</summary>
    Pending,

    /// <summary>Done: a highlighted border and fill.</summary>
    Done,

    /// <summary>A running total, rather than something to collect.</summary>
    Total,
}

/// <summary>A goal rectangle: a small label over a large value, with a border showing how far along it is.</summary>
internal sealed class GoalTile : TrackerControl
{
    private string _label = "";
    private string _value = "";
    private GoalTileStyle _style;
    private Font _valueFont = TrackerTheme.MarkFont;

    public void Set(string label, string value, GoalTileStyle style, Font valueFont)
    {
        if (label == _label && value == _value && style == _style && valueFont == _valueFont)
            return;
        _label = label;
        _value = value;
        _style = style;
        _valueFont = valueFont;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        (Color fill, Color border, Color valueColor) = _style switch
        {
            GoalTileStyle.Done => (TrackerTheme.AccentFill, TrackerTheme.Accent, TrackerTheme.Accent),
            GoalTileStyle.Pending => (TrackerTheme.Background, TrackerTheme.Accent, TrackerTheme.Accent),
            GoalTileStyle.Total => (TrackerTheme.Background, TrackerTheme.LineStrong, TrackerTheme.Text),
            _ => (TrackerTheme.Background, TrackerTheme.Line, TrackerTheme.TextDim),
        };
        FillTile(e.Graphics, fill, border);

        TextRenderer.DrawText(e.Graphics, _label, TrackerTheme.CaptionFont, new Rectangle(S(4), S(6), Width - S(8), Height / 2),
            TrackerTheme.TextMuted, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(e.Graphics, _value, _valueFont, new Rectangle(0, Height / 3, Width, Height * 2 / 3 - S(4)),
            valueColor, SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>A small labeled value in the command bar, highlighted when it needs attention.</summary>
internal sealed class StatusTile : TrackerControl
{
    private readonly string _label;
    private string _value = "";
    private bool _highlighted;

    public StatusTile(string label)
    {
        _label = label;
    }

    public void Set(string value, bool highlighted)
    {
        if (value == _value && highlighted == _highlighted)
            return;
        _value = value;
        _highlighted = highlighted;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        FillTile(e.Graphics, _highlighted ? TrackerTheme.AccentFill : TrackerTheme.Background, _highlighted ? TrackerTheme.Accent : TrackerTheme.Line);
        TextRenderer.DrawText(e.Graphics, _label, TrackerTheme.CaptionFont, new Rectangle(0, S(6), Width, S(14)),
            TrackerTheme.TextMuted, SingleLine | TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(e.Graphics, _value, TrackerTheme.BodyFont, new Rectangle(0, S(21), Width, S(19)),
            _highlighted ? TrackerTheme.Accent : TrackerTheme.Text, SingleLine | TextFormatFlags.HorizontalCenter);
    }
}

/// <summary>A connection in the command bar: what it's connected to, and the button that toggles it.</summary>
internal sealed class ConnectionTile : TrackerControl
{
    private readonly string _label;
    private string _value = "";
    private Color _valueColor = TrackerTheme.Text;
    private bool _hasError;

    public Button Button { get; } = new();

    public ConnectionTile(string label)
    {
        _label = label;
        TrackerTheme.StyleButton(Button);
        Controls.Add(Button);
    }

    public void Set(string value, Color valueColor, bool hasError, string buttonText, bool buttonEnabled)
    {
        if (Button.Text != buttonText)
        {
            Button.Text = buttonText;
            PerformLayout(); // the button is sized to its text
        }
        Button.Enabled = buttonEnabled;
        if (value == _value && valueColor == _valueColor && hasError == _hasError)
            return;
        _value = value;
        _valueColor = valueColor;
        _hasError = hasError;
        Invalidate();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int buttonWidth = TextRenderer.MeasureText(Button.Text, Button.Font).Width + S(16);
        Button.SetBounds(Width - buttonWidth - S(8), (Height - S(30)) / 2, buttonWidth, S(30));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        FillTile(e.Graphics, TrackerTheme.Background, _hasError ? TrackerTheme.Error : TrackerTheme.Line);
        int textWidth = Button.Left - S(14);
        TextRenderer.DrawText(e.Graphics, _label, TrackerTheme.CaptionFont, new Rectangle(S(10), S(6), textWidth, S(14)),
            TrackerTheme.TextMuted, SingleLine);
        TextRenderer.DrawText(e.Graphics, _value, TrackerTheme.BodyFont, new Rectangle(S(10), S(21), textWidth, S(19)),
            _valueColor, SingleLine | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>A section's title, with an optional legend on the right.</summary>
internal sealed class SectionHeader : TrackerControl
{
    private readonly string _title;
    private readonly (string Text, Color? Pip, bool PipFilled)[] _legend;

    public SectionHeader(string title, params (string Text, Color? Pip, bool PipFilled)[] legend)
    {
        _title = title;
        _legend = legend;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        TextRenderer.DrawText(e.Graphics, _title, TrackerTheme.CaptionFont, ClientRectangle, TrackerTheme.TextMuted, SingleLine | TextFormatFlags.VerticalCenter);

        int x = Width;
        int pip = S(8);
        for (int i = _legend.Length - 1; i >= 0; i--)
        {
            (string text, Color? pipColor, bool filled) = _legend[i];
            int textWidth = TextRenderer.MeasureText(e.Graphics, text, TrackerTheme.CaptionFont, Size.Empty, SingleLine).Width;
            x -= textWidth;
            TextRenderer.DrawText(e.Graphics, text, TrackerTheme.CaptionFont, new Rectangle(x, 0, textWidth, Height), TrackerTheme.TextMuted, SingleLine | TextFormatFlags.VerticalCenter);
            if (pipColor is { } color)
            {
                x -= pip + S(4);
                DrawPip(e.Graphics, x, (Height - pip) / 2, pip, color, filled);
            }
            x -= S(14);
        }
    }
}

/// <summary>How many Progressive Clues the Parrot Hunt has turned up, as a count and a row of dots.</summary>
internal sealed class ClueBar : TrackerControl
{
    private int _found;
    private int _total;

    public void Set(int found, int total)
    {
        if (found == _found && total == _total)
            return;
        _found = found;
        _total = total;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        FillTile(e.Graphics, TrackerTheme.Background, TrackerTheme.Line);
        TextRenderer.DrawText(e.Graphics, "CLUES", TrackerTheme.CaptionFont, new Rectangle(S(12), S(10), Width / 2, S(14)), TrackerTheme.TextMuted, SingleLine);
        TextRenderer.DrawText(e.Graphics, $"{_found} / {_total}", TrackerTheme.ClueCountFont, new Rectangle(S(12), S(26), Width / 2, Height - S(30)), TrackerTheme.Text, SingleLine);

        int pip = S(14), gap = S(6);
        int x = Width - S(12) - _total * (pip + gap) + gap;
        for (int i = 0; i < _total; i++, x += pip + gap)
            DrawPip(e.Graphics, x, (Height - pip) / 2, pip, i < _found ? TrackerTheme.Accent : TrackerTheme.PipLocked, i < _found);
    }
}

/// <summary>The latest AP lines involving this slot, newest first, so they aren't lost when the log floods.</summary>
internal sealed class RecentPanel : TrackerControl
{
    private const int MaxEntries = 20;

    private readonly record struct Entry((string Text, Color Color)[] Parts, DateTime Received);
    private readonly record struct Word(string Text, Color Color);

    private readonly List<Entry> _entries = new();
    private readonly System.Windows.Forms.Timer _ageTimer = new() { Interval = 20000 };

    public RecentPanel()
    {
        _ageTimer.Tick += (_, _) => Invalidate();
        _ageTimer.Start();
    }

    public void Add(IEnumerable<(string Text, Color Color)> parts, DateTime received)
    {
        _entries.Insert(0, new Entry(parts.ToArray(), received));
        if (_entries.Count > MaxEntries)
            _entries.RemoveAt(_entries.Count - 1);
        Invalidate();
    }

    public void Clear()
    {
        _entries.Clear();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _ageTimer.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        int y = 0;
        int padX = S(8), padY = S(5);
        int lineHeight = TextRenderer.MeasureText(g, "Ag", TrackerTheme.RecentFont, Size.Empty, SingleLine).Height + S(2);
        int spaceWidth = TextRenderer.MeasureText(g, "a a", TrackerTheme.RecentFont, Size.Empty, SingleLine).Width
            - TextRenderer.MeasureText(g, "aa", TrackerTheme.RecentFont, Size.Empty, SingleLine).Width;

        foreach (Entry entry in _entries)
        {
            string age = FormatAge(DateTime.Now - entry.Received);
            int ageWidth = TextRenderer.MeasureText(g, age, TrackerTheme.CaptionFont, Size.Empty, SingleLine).Width;
            int textWidth = Width - padX * 2 - ageWidth - S(8);

            List<List<(string Text, Color Color, int X)>> lines = WrapLine(g, entry, textWidth, spaceWidth);
            int height = lines.Count * lineHeight + padY * 2;
            if (y + height > Height)
                break;

            var card = new Rectangle(0, y, Width - 1, height - 1);
            using (var brush = new SolidBrush(TrackerTheme.LogBackground))
                g.FillRectangle(brush, card);
            using (var pen = new Pen(TrackerTheme.LineDim))
                g.DrawRectangle(pen, card);

            int lineY = y + padY;
            foreach (List<(string Text, Color Color, int X)> line in lines)
            {
                foreach ((string text, Color color, int x) in line)
                    TextRenderer.DrawText(g, text, TrackerTheme.RecentFont, new Point(padX + x, lineY), color, TrackerTheme.LogBackground, SingleLine);
                lineY += lineHeight;
            }
            TextRenderer.DrawText(g, age, TrackerTheme.CaptionFont, new Point(Width - padX - ageWidth, y + padY + S(1)), TrackerTheme.TextMuted, TrackerTheme.LogBackground, SingleLine);

            y += height + S(4);
        }
    }

    /// <summary>Lays one entry's colored parts out word by word, since TextRenderer can only wrap text of one color.</summary>
    private static List<List<(string Text, Color Color, int X)>> WrapLine(Graphics g, Entry entry, int width, int spaceWidth)
    {
        var lines = new List<List<(string Text, Color Color, int X)>> { new() };
        int x = 0;
        foreach ((string text, Color color) in entry.Parts)
        {
            foreach (Match match in Regex.Matches(text, @"\s+|\S+"))
            {
                if (string.IsNullOrWhiteSpace(match.Value))
                {
                    x += match.Length * spaceWidth;
                    continue;
                }

                int wordWidth = TextRenderer.MeasureText(g, match.Value, TrackerTheme.RecentFont, Size.Empty, SingleLine).Width;
                if (x + wordWidth > width && x > 0)
                {
                    lines.Add(new());
                    x = 0;
                }
                lines[^1].Add((match.Value, color, x));
                x += wordWidth;
            }
        }
        return lines;
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalMinutes < 1)
            return "now";
        return age.TotalHours < 1 ? $"{(int)age.TotalMinutes}m" : $"{(int)age.TotalHours}h";
    }
}
