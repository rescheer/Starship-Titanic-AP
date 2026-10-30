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

/// <summary>Where a check stands, in the order a room tile's dots show them.</summary>
internal enum CheckStatus { Checked, InLogic, Locked }

/// <summary>One of a room's checks.</summary>
/// <param name="Requires">What's still missing for a locked check, where that's known. Compared by reference, so reuse
/// the same array while it hasn't changed.</param>
internal readonly record struct CheckPip(string Name, CheckStatus Status, string[] Requires);

/// <summary>What a room tile shows. Without <paramref name="Checks"/> it's just a name and tag.</summary>
/// <param name="Checks">The room's checks, in the order to draw them.</param>
/// <param name="IsLocked">Whether something's missing to enter the room, which dims the tile.</param>
/// <param name="IsHere">Whether the player's in this room, which highlights the tile and adds a map pin.</param>
/// <param name="NeedsCard">Whether the room's Access Card is still missing, which adds a lock unless there's a map pin.</param>
internal readonly record struct RoomTileState(string Name, CheckPip[] Checks, string Tag, bool IsLocked, bool IsHere, bool NeedsCard);

/// <summary>One room: its name, a dot per check, and a line on where it stands.</summary>
internal sealed class RoomTile : TrackerControl
{
    private const int CornerIconSize = 12;
    private const int CornerIconInset = 5;

    private RoomTileState _state = new("", Array.Empty<CheckPip>(), "", false, false, false);
    private int _hoveredIndex = -1;
    private CheckPip? _hoveredPip;

    /// <summary>The dot under the mouse changed: the tile, its check (null for none) and its bounds in the tile.</summary>
    public event Action<RoomTile, CheckPip?, Rectangle>? PipHoverChanged;

    public RoomTileState State
    {
        get => _state;
        set
        {
            if (value with { Checks = _state.Checks } == _state && value.Checks.SequenceEqual(_state.Checks))
                return;
            _state = value;
            Invalidate();
            if (_hoveredIndex >= 0)
                UpdateHover(PointToClient(Cursor.Position));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        UpdateHover(e.Location);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        UpdateHover(new Point(-1, -1));
    }

    private void UpdateHover(Point mouse)
    {
        int index = PipAt(mouse);
        CheckPip? pip = index >= 0 ? _state.Checks[index] : null;
        if (index == _hoveredIndex && pip == _hoveredPip)
            return;
        _hoveredIndex = index;
        _hoveredPip = pip;
        PipHoverChanged?.Invoke(this, pip, index >= 0 ? PipBounds(index) : Rectangle.Empty);
    }

    /// <summary>The check at a point, counting the gaps around a dot as part of it, since the dots are small.</summary>
    private int PipAt(Point point)
    {
        for (int i = 0; i < _state.Checks.Length; i++)
        {
            if (Rectangle.Inflate(PipBounds(i), S(3), S(3)).Contains(point))
                return i;
        }
        return -1;
    }

    private Rectangle PipBounds(int index)
    {
        int left = S(6), pip = S(8), pitch = pip + S(3);
        int perRow = Math.Max(1, (Width - S(2) - left + S(3)) / pitch);
        return new Rectangle(left + index % perRow * pitch, S(22) + index / perRow * pitch, pip, pip);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        bool hasChecks = _state.Checks.Length > 0;
        bool complete = hasChecks && _state.Checks.All(check => check.Status == CheckStatus.Checked);
        Color fill = _state.IsHere ? TrackerTheme.AccentFill : complete ? TrackerTheme.CompleteFill : TrackerTheme.Background;
        Color border = true switch
        {
            _ when _state.IsHere => TrackerTheme.Accent,
            _ when _state.IsLocked => TrackerTheme.LineDim,
            _ when !hasChecks => TrackerTheme.Line,
            _ => TrackerTheme.LineStrong,
        };
        Color nameColor = _state.IsLocked ? TrackerTheme.TextMuted : TrackerTheme.Text;
        FillTile(e.Graphics, fill, border);

        int left = S(6);
        TextRenderer.DrawText(e.Graphics, _state.Name, TrackerTheme.TileNameFont, new Rectangle(left, S(4), Width - left - S(2), S(16)),
            nameColor, SingleLine | TextFormatFlags.EndEllipsis);

        for (int i = 0; i < _state.Checks.Length; i++)
        {
            Rectangle pip = PipBounds(i);
            CheckStatus status = _state.Checks[i].Status;
            DrawPip(e.Graphics, pip.X, pip.Y, pip.Width, status == CheckStatus.Locked ? TrackerTheme.PipLocked : TrackerTheme.Accent, status == CheckStatus.Checked);
        }

        // A tag stops short of the corner icon
        bool hasCornerIcon = _state.IsHere || _state.NeedsCard;
        int tagRight = hasCornerIcon ? Width - S(CornerIconInset + CornerIconSize + 3) : Width - S(2);
        TextRenderer.DrawText(e.Graphics, _state.Tag, TrackerTheme.CaptionFont, new Rectangle(left, Height - S(17), tagRight - left, S(14)),
            TrackerTheme.TextMuted, SingleLine | TextFormatFlags.EndEllipsis);

        if (_state.IsHere)
            DrawHerePin(e.Graphics);
        else if (_state.NeedsCard)
            DrawCardLock(e.Graphics);
    }

    /// <summary>Draws the map pin, from a <see cref="CornerIconSize"/> square design.</summary>
    private void DrawHerePin(Graphics g)
    {
        using var pin = new GraphicsPath();
        pin.AddArc(1.7f, 0.3f, 8.6f, 8.6f, 180f, 180f);
        pin.AddBezier(10.3f, 4.6f, 10.3f, 7.6f, 6f, 11.7f, 6f, 11.7f);
        pin.AddBezier(6f, 11.7f, 6f, 11.7f, 1.7f, 7.6f, 1.7f, 4.6f);
        pin.CloseFigure();
        pin.AddEllipse(4.6f, 3.2f, 2.8f, 2.8f); // the hole, by the path's alternate fill

        FillCornerIcon(g, pin, TrackerTheme.Accent);
    }

    /// <summary>Draws the lock, from a <see cref="CornerIconSize"/> square design.</summary>
    private void DrawCardLock(Graphics g)
    {
        using var padlock = new GraphicsPath();
        padlock.AddLine(1.7f, 5.3f, 3.2f, 5.3f);
        padlock.AddLine(3.2f, 5.3f, 3.2f, 3.3f);
        padlock.AddArc(3.2f, 0.5f, 5.6f, 5.6f, 180f, 180f);
        padlock.AddLine(8.8f, 3.3f, 8.8f, 5.3f);
        padlock.AddLine(8.8f, 5.3f, 10.3f, 5.3f);
        padlock.AddLine(10.3f, 5.3f, 10.3f, 11.5f);
        padlock.AddLine(10.3f, 11.5f, 1.7f, 11.5f);
        padlock.CloseFigure();

        // The holes in the shackle and for the key, by the path's alternate fill
        padlock.AddLine(4.5f, 5.3f, 4.5f, 3.3f);
        padlock.AddArc(4.5f, 1.8f, 3f, 3f, 180f, 180f);
        padlock.AddLine(7.5f, 3.3f, 7.5f, 5.3f);
        padlock.CloseFigure();
        padlock.AddEllipse(5f, 7.4f, 2f, 2f);

        FillCornerIcon(g, padlock, TrackerTheme.Error);
    }

    /// <summary>Fills a <see cref="CornerIconSize"/> square design in the lower right corner.</summary>
    private void FillCornerIcon(Graphics g, GraphicsPath icon, Color color)
    {
        using var transform = new Matrix();
        transform.Translate(Width - S(CornerIconInset + CornerIconSize), Height - S(CornerIconInset + CornerIconSize));
        transform.Scale(S(CornerIconSize) / (float)CornerIconSize, S(CornerIconSize) / (float)CornerIconSize);
        icon.Transform(transform);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(color);
        g.FillPath(brush, icon);
        g.SmoothingMode = SmoothingMode.Default;
    }
}

/// <summary>Details on what's under the mouse. Drawn over its siblings in the main window, rather than as a Windows
/// tooltip, so it can match the window and appear at once.</summary>
internal abstract class HoverPopup : TrackerControl
{
    protected const int Inset = 7;
    protected const int CaptionGap = 6;
    private const int WmNcHitTest = 0x84;
    private static readonly IntPtr HtTransparent = new(-1);

    protected HoverPopup()
    {
        Visible = false;
    }

    /// <summary>Shows the popup beside <paramref name="anchor"/> (in the parent's coordinates), inside the parent, sized to its content.</summary>
    protected void ShowBeside(Rectangle anchor, int contentWidth, int contentHeight)
    {
        int margin = S(4);
        Size parent = Parent!.ClientSize;
        int width = Math.Min(contentWidth + S(Inset) * 2, parent.Width - margin * 2);
        int height = contentHeight + S(Inset) * 2;

        int x = Math.Clamp(anchor.Left - S(Inset), margin, parent.Width - width - margin);
        int y = anchor.Bottom + margin;
        if (y + height > parent.Height - margin)
            y = Math.Max(margin, anchor.Top - margin - height);

        SetBounds(x, y, width, height);
        Visible = true;
        Invalidate();
    }

    protected static Size Measure(string text, Font font) => TextRenderer.MeasureText(text, font, Size.Empty, SingleLine);

    /// <summary>Lets the mouse through to the dots underneath, so the popup can't take the hover away from them.</summary>
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmNcHitTest)
            m.Result = HtTransparent;
        else
            base.WndProc(ref m);
    }

    /// <summary>Draws a line of text and returns where the next one goes.</summary>
    protected int DrawLine(Graphics g, string text, Font font, Color color, int x, int y)
    {
        int height = Measure(text, font).Height;
        TextRenderer.DrawText(g, text, font, new Rectangle(x, y, Width - x * 2, height), color, SingleLine | TextFormatFlags.EndEllipsis);
        return y + height;
    }
}

/// <summary>Names the check under the mouse and where it stands or, if it's locked, what it still requires.</summary>
internal sealed class CheckPopup : HoverPopup
{
    private CheckPip _check;

    /// <summary>Shows a check beside <paramref name="anchor"/> (in the parent's coordinates), inside the parent.</summary>
    public void ShowFor(CheckPip check, Rectangle anchor)
    {
        _check = check;

        Size nameSize = Measure(check.Name, TrackerTheme.TileNameFont);
        int width = nameSize.Width, height = nameSize.Height;
        if (Caption(check) is { } caption)
        {
            Size captionSize = Measure(caption, TrackerTheme.CaptionFont);
            width = Math.Max(width, captionSize.Width);
            height += S(CaptionGap) + captionSize.Height;
            foreach (string item in check.Requires)
            {
                Size size = Measure(item, TrackerTheme.TileNameFont);
                width = Math.Max(width, size.Width);
                height += size.Height;
            }
        }
        ShowBeside(anchor, width, height);
    }

    /// <summary>The line under the name. A locked check without known requirements has none.</summary>
    private static string? Caption(CheckPip check) => check.Status switch
    {
        CheckStatus.Checked => "COMPLETED",
        CheckStatus.InLogic => "IN LOGIC",
        _ => check.Requires.Length > 0 ? "REQUIRES" : null,
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        FillTile(e.Graphics, TrackerTheme.Surface, _check.Status == CheckStatus.Locked ? TrackerTheme.PipLocked : TrackerTheme.Accent);

        int x = S(Inset), y = S(Inset);
        y = DrawLine(e.Graphics, _check.Name, TrackerTheme.TileNameFont, TrackerTheme.Text, x, y);
        if (Caption(_check) is not { } caption)
            return;

        y = DrawLine(e.Graphics, caption, TrackerTheme.CaptionFont, TrackerTheme.TextMuted, x, y + S(CaptionGap));
        foreach (string item in _check.Requires)
            y = DrawLine(e.Graphics, item, TrackerTheme.TileNameFont, TrackerTheme.Text, x, y);
    }
}

/// <summary>Shows what the clue under the mouse said and whether it turned up anything.</summary>
internal sealed class CluePopup : HoverPopup
{
    private const int MaxTextWidth = 300;
    private const TextFormatFlags Wrapped = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak;

    private ClueEntry _clue;
    private Size _textSize;

    /// <summary>Shows a clue beside <paramref name="anchor"/> (in the parent's coordinates), inside the parent.</summary>
    public void ShowFor(ClueEntry clue, Rectangle anchor)
    {
        _clue = clue;

        int maxWidth = Math.Min(S(MaxTextWidth), Parent!.ClientSize.Width - S(4 + Inset) * 2);
        _textSize = TextRenderer.MeasureText(clue.Text, TrackerTheme.TileNameFont, new Size(maxWidth, int.MaxValue), Wrapped);

        Size titleSize = Measure(Title, TrackerTheme.CaptionFont);
        Size captionSize = Measure(Caption, TrackerTheme.CaptionFont);
        int width = Math.Max(Math.Max(titleSize.Width, _textSize.Width), captionSize.Width);
        int height = titleSize.Height + S(CaptionGap) + _textSize.Height + S(CaptionGap) + captionSize.Height;
        if (clue.Discovery is { } discovery)
        {
            Size discoverySize = Measure(discovery, TrackerTheme.TileNameFont);
            width = Math.Max(width, discoverySize.Width);
            height += discoverySize.Height;
        }
        ShowBeside(anchor, width, height);
    }

    private string Title => $"CLUE {_clue.Number}";

    private string Caption => _clue.Discovery is null ? "USELESS CLUE" : "NEW DISCOVERY";

    protected override void OnPaint(PaintEventArgs e)
    {
        FillTile(e.Graphics, TrackerTheme.Surface, _clue.Discovery is null ? TrackerTheme.PipLocked : TrackerTheme.Accent);

        int x = S(Inset), y = S(Inset);
        y = DrawLine(e.Graphics, Title, TrackerTheme.CaptionFont, TrackerTheme.TextMuted, x, y) + S(CaptionGap);
        TextRenderer.DrawText(e.Graphics, _clue.Text, TrackerTheme.TileNameFont, new Rectangle(x, y, Width - x * 2, _textSize.Height), TrackerTheme.Text, Wrapped);
        y += _textSize.Height + S(CaptionGap);

        y = DrawLine(e.Graphics, Caption, TrackerTheme.CaptionFont, TrackerTheme.TextMuted, x, y);
        if (_clue.Discovery is { } discovery)
            DrawLine(e.Graphics, discovery, TrackerTheme.TileNameFont, TrackerTheme.Accent, x, y);
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
    private string _label = "";
    private string _value = "";
    private bool _highlighted;

    public void Set(string label, string value, bool highlighted)
    {
        if (label == _label && value == _value && highlighted == _highlighted)
            return;
        _label = label;
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
    private bool _valueClickable;

    public Button Button { get; } = new();

    public event Action? ValueClicked;

    public ConnectionTile(string label)
    {
        _label = label;
        TrackerTheme.StyleButton(Button);
        Controls.Add(Button);
    }

    private Rectangle ValueBounds => new(S(10), S(21), Button.Left - S(14), S(19));

    public void Set(string value, Color valueColor, bool hasError, string buttonText, bool buttonEnabled, bool valueClickable = false)
    {
        if (Button.Text != buttonText)
        {
            Button.Text = buttonText;
            PerformLayout(); // the button is sized to its text
        }
        Button.Enabled = buttonEnabled;
        if (_valueClickable != valueClickable)
        {
            _valueClickable = valueClickable;
            UpdateCursor(PointToClient(Cursor.Position));
        }
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

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        UpdateCursor(e.Location);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        UpdateCursor(new Point(-1, -1));
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (_valueClickable && e.Button == MouseButtons.Left && ValueBounds.Contains(e.Location))
            ValueClicked?.Invoke();
    }

    private void UpdateCursor(Point location) =>
        Cursor = _valueClickable && ValueBounds.Contains(location) ? Cursors.Hand : Cursors.Default;

    protected override void OnPaint(PaintEventArgs e)
    {
        FillTile(e.Graphics, TrackerTheme.Background, _hasError ? TrackerTheme.Error : TrackerTheme.Line);
        Rectangle valueBounds = ValueBounds;
        TextRenderer.DrawText(e.Graphics, _label, TrackerTheme.CaptionFont, new Rectangle(S(10), S(6), valueBounds.Width, S(14)),
            TrackerTheme.TextMuted, SingleLine);
        TextRenderer.DrawText(e.Graphics, _value, TrackerTheme.BodyFont, valueBounds,
            _valueColor, SingleLine | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>A section's title, with an optional legend on the right.</summary>
internal sealed class SectionHeader : TrackerControl
{
    private readonly string _title;
    private (string Text, Color? Pip, bool PipFilled)[] _legend;

    public SectionHeader(string title, params (string Text, Color? Pip, bool PipFilled)[] legend)
    {
        _title = title;
        _legend = legend;
    }

    public void SetLegend((string Text, Color? Pip, bool PipFilled)[] legend)
    {
        if (ReferenceEquals(legend, _legend))
            return;
        _legend = legend;
        Invalidate();
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

/// <summary>A Progressive Clue that's been found.</summary>
/// <param name="Number">Which one it was, counting from 1.</param>
/// <param name="Discovery">The part of the address it revealed, like "Floor 23". Null for a red herring.</param>
internal readonly record struct ClueEntry(int Number, string Text, string? Discovery);

/// <summary>How many Progressive Clues the Parrot Hunt has turned up, as a count and a row of dots.</summary>
internal sealed class ClueBar : TrackerControl
{
    private int _found;
    private int _total;
    private ClueEntry[] _clues = Array.Empty<ClueEntry>();
    private int _hoveredIndex = -1;
    private ClueEntry? _hoveredClue;

    /// <summary>The found clue under the mouse changed: its entry (null for none) and its dot's bounds in the bar.</summary>
    public event Action<ClueEntry?, Rectangle>? ClueHoverChanged;

    /// <param name="clues">The found clues in order, where they're known.</param>
    public void Set(int found, int total, ClueEntry[] clues)
    {
        if (found == _found && total == _total && clues.SequenceEqual(_clues))
            return;
        _found = found;
        _total = total;
        _clues = clues;
        Invalidate();
        if (_hoveredIndex >= 0)
            UpdateHover(PointToClient(Cursor.Position));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        UpdateHover(e.Location);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        UpdateHover(new Point(-1, -1));
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible)
            UpdateHover(new Point(-1, -1));
    }

    private void UpdateHover(Point mouse)
    {
        int index = DotAt(mouse);
        ClueEntry? clue = index >= 0 && index < _clues.Length ? _clues[index] : null;
        if (index == _hoveredIndex && clue == _hoveredClue)
            return;
        _hoveredIndex = index;
        _hoveredClue = clue;
        ClueHoverChanged?.Invoke(clue, index >= 0 ? DotBounds(index) : Rectangle.Empty);
    }

    /// <summary>The dot at a point, counting the gaps around a dot as part of it.</summary>
    private int DotAt(Point point)
    {
        for (int i = 0; i < _total; i++)
        {
            if (Rectangle.Inflate(DotBounds(i), S(3), S(3)).Contains(point))
                return i;
        }
        return -1;
    }

    private Rectangle DotBounds(int index)
    {
        int pip = S(14), pitch = pip + S(6);
        return new Rectangle(Width - S(12) - (_total - index) * pitch + S(6), (Height - pip) / 2, pip, pip);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        FillTile(e.Graphics, TrackerTheme.Background, TrackerTheme.Line);
        TextRenderer.DrawText(e.Graphics, "CLUES", TrackerTheme.CaptionFont, new Rectangle(S(12), S(10), Width / 2, S(14)), TrackerTheme.TextMuted, SingleLine);
        TextRenderer.DrawText(e.Graphics, $"{_found} / {_total}", TrackerTheme.ClueCountFont, new Rectangle(S(12), S(26), Width / 2, Height - S(30)), TrackerTheme.Text, SingleLine);

        for (int i = 0; i < _total; i++)
        {
            Rectangle dot = DotBounds(i);
            bool useless = i < _clues.Length && _clues[i].Discovery is null;
            DrawPip(e.Graphics, dot.X, dot.Y, dot.Width, i < _found && !useless ? TrackerTheme.Accent : TrackerTheme.PipLocked, i < _found);
        }
    }
}

/// <summary>What the You Are Here panel shows.</summary>
/// <param name="Checks">The room's uncompleted checks, named without the room, in the order to draw them.</param>
/// <param name="Message">Stands in for the list while <paramref name="Checks"/> is empty.</param>
internal readonly record struct HereState(string RoomName, CheckPip[] Checks, string Message);

/// <summary>The room the player's in and the checks still to get there, row by row across two columns.</summary>
internal sealed class HerePanel : TrackerControl
{
    private HereState _state = new("", Array.Empty<CheckPip>(), "");
    private int _hoveredIndex = -1;
    private CheckPip? _hoveredCheck;

    /// <summary>The locked check under the mouse changed: the panel, its check (null for none) and its line's bounds in the panel.</summary>
    public event Action<HerePanel, CheckPip?, Rectangle>? CheckHoverChanged;

    public HereState State
    {
        set
        {
            if (value with { Checks = _state.Checks } == _state && value.Checks.SequenceEqual(_state.Checks))
                return;
            _state = value;
            Invalidate();
            if (_hoveredIndex >= 0)
                UpdateHover(PointToClient(Cursor.Position));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        UpdateHover(e.Location);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        UpdateHover(new Point(-1, -1));
    }

    private void UpdateHover(Point mouse)
    {
        int index = LockedCheckAt(mouse);
        CheckPip? check = index >= 0 ? _state.Checks[index] : null;
        if (index == _hoveredIndex && check == _hoveredCheck)
            return;
        _hoveredIndex = index;
        _hoveredCheck = check;
        CheckHoverChanged?.Invoke(this, check, index >= 0 ? LineBounds(index) : Rectangle.Empty);
    }

    /// <summary>The check at a point, among those with known needs to show.</summary>
    private int LockedCheckAt(Point point)
    {
        for (int i = 0; i < _state.Checks.Length; i++)
        {
            CheckPip check = _state.Checks[i];
            if (check.Status == CheckStatus.Locked && check.Requires.Length > 0 && LineBounds(i).Contains(point))
                return i;
        }
        return -1;
    }

    private Rectangle LineBounds(int index)
    {
        int left = S(8), columnWidth = (Width - left * 2) / 2, rowHeight = S(18);
        return new Rectangle(left + index % 2 * columnWidth, S(28) + index / 2 * rowHeight, columnWidth, rowHeight);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        FillTile(e.Graphics, TrackerTheme.Background, TrackerTheme.LineStrong);

        int left = S(8), pip = S(8), textOffset = pip + S(5);
        TextRenderer.DrawText(e.Graphics, _state.RoomName, TrackerTheme.BodyFont, new Rectangle(left, S(6), Width - left * 2, S(19)),
            TrackerTheme.Text, SingleLine | TextFormatFlags.EndEllipsis);

        if (_state.Checks.Length == 0)
        {
            Rectangle message = LineBounds(0);
            message.Width = Width - left * 2;
            TextRenderer.DrawText(e.Graphics, _state.Message, TrackerTheme.TileNameFont, message,
                TrackerTheme.TextMuted, SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            return;
        }

        for (int i = 0; i < _state.Checks.Length; i++)
        {
            Rectangle line = LineBounds(i);
            bool locked = _state.Checks[i].Status == CheckStatus.Locked;
            DrawPip(e.Graphics, line.X, line.Y + (line.Height - pip) / 2, pip, locked ? TrackerTheme.PipLocked : TrackerTheme.Accent, filled: false);
            TextRenderer.DrawText(e.Graphics, _state.Checks[i].Name, TrackerTheme.TileNameFont, new Rectangle(line.X + textOffset, line.Y, line.Width - textOffset - S(4), line.Height),
                locked ? TrackerTheme.TextMuted : TrackerTheme.Text, SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
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
