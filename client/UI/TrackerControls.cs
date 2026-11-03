using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;

namespace StarshipTitanicAp;

/// <summary>Base for the main window's custom-painted rectangles.</summary>
internal abstract class TrackerControl : Control
{
    protected const TextFormatFlags SingleLine = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
    protected const int Inset = 7;

    /// <summary>The player clicked something with hints to request.</summary>
    public event Action<HintOption[]>? HintClicked;

    /// <summary>The mouse moved onto or off a part of the control that's hinted or hintable: the control, the part
    /// (null for none) and its bounds in the control. Dots and clues have their own, as they show more.</summary>
    public event Action<TrackerControl, HintTarget?, Rectangle>? HintHoverChanged;

    protected TrackerControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        BackColor = TrackerTheme.Background;
        ForeColor = TrackerTheme.Text;
    }

    /// <summary>Scales a design-time pixel size for the monitor the window is on.</summary>
    protected int S(int pixels) => LogicalToDeviceUnits(pixels);

    protected void RaiseHintClicked(HintOption[] options)
    {
        if (options.Length > 0)
            HintClicked?.Invoke(options);
    }

    protected void RaiseHintHover(HintTarget? target, Rectangle bounds) => HintHoverChanged?.Invoke(this, target, bounds);

    protected void ShowHandCursor(bool show) => Cursor = show ? Cursors.Hand : Cursors.Default;

    /// <summary>Shows the control beside <paramref name="anchor"/> (in the parent's coordinates), inside the parent.</summary>
    protected void ShowBesideAnchor(Rectangle anchor, int width, int height)
    {
        int margin = S(4);
        Size parent = Parent!.ClientSize;
        width = Math.Min(width, parent.Width - margin * 2);

        int x = Math.Clamp(anchor.Left - S(Inset), margin, parent.Width - width - margin);
        int y = anchor.Bottom + margin;
        if (y + height > parent.Height - margin)
            y = Math.Max(margin, anchor.Top - margin - height);

        SetBounds(x, y, width, height);
        Visible = true;
        Invalidate();
    }

    private static int SpaceWidth(Font font) =>
        TextRenderer.MeasureText("a a", font, Size.Empty, SingleLine).Width - TextRenderer.MeasureText("aa", font, Size.Empty, SingleLine).Width;

    /// <summary>A part's width, counting the spaces around its text, which measuring would leave out.</summary>
    private static int PartWidth(string text, Font font)
    {
        string trimmed = text.Trim();
        int width = trimmed.Length > 0 ? TextRenderer.MeasureText(trimmed, font, Size.Empty, SingleLine).Width : 0;
        return width + (text.Length - trimmed.Length) * SpaceWidth(font);
    }

    protected static int MeasureParts((string Text, Color Color)[] parts, Font font) => parts.Sum(part => PartWidth(part.Text, font));

    /// <summary>Draws differently colored pieces in a row, cutting off what doesn't fit with an ellipsis.</summary>
    /// <param name="maxFirstPartWidth">Keeps a long first part from pushing the others out of sight.</param>
    protected static void DrawParts(Graphics g, (string Text, Color Color)[] parts, Font font, Rectangle bounds, int maxFirstPartWidth = int.MaxValue)
    {
        int x = bounds.Left;
        for (int i = 0; i < parts.Length && x < bounds.Right; i++)
        {
            (string text, Color color) = parts[i];
            int width = Math.Min(PartWidth(text, font), bounds.Right - x);
            if (i == 0)
                width = Math.Min(width, maxFirstPartWidth);

            string trimmed = text.TrimStart();
            int leading = (text.Length - trimmed.Length) * SpaceWidth(font);
            trimmed = trimmed.TrimEnd();
            if (trimmed.Length > 0)
                TextRenderer.DrawText(g, trimmed, font, new Rectangle(x + leading, bounds.Top, width - leading, bounds.Height), color, SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            x += width;
        }
    }

    protected void FillTile(Graphics g, Color fill, Color border)
    {
        using (var brush = new SolidBrush(fill))
            g.FillRectangle(brush, ClientRectangle);
        using var pen = new Pen(border);
        g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    /// <summary>Draws a dot: either filled or a ring.</summary>
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

/// <summary>Something a click can ask the server for.</summary>
/// <param name="Label">How to name it to the player.</param>
/// <param name="Target">The item or location name to send.</param>
/// <param name="IsLocation">Whether it's a location's contents, rather than an item.</param>
/// <param name="Required">For an item, how many copies are needed in total.</param>
/// <param name="Copies">How many requests it would take to reveal everything needed.</param>
internal readonly record struct HintOption(string Label, string Target, bool IsLocation, int Required, int Copies);

/// <summary>Something that isn't a check but can have hints: an Access Card, a Titania part or the clues.</summary>
/// <param name="Hints">Where its unfound hints say it is, one line each.</param>
/// <param name="Options">What's left to request. Compared by reference, as is <paramref name="Hints"/>, so reuse the
/// same instance while it hasn't changed.</param>
internal sealed record HintTarget(string Title, (string Text, Color Color)[][] Hints, HintOption[] Options);

/// <summary>An item a locked check is missing, and where its unfound hints say it is.</summary>
internal readonly record struct RequirementLine(string Text, (string Text, Color Color)[][] Hints);

/// <summary>A hint on a check itself.</summary>
/// <param name="Caption">What the hover calls it, like "HINTED: PRIORITY".</param>
/// <param name="DotColor">What color the hint turns the check's dot, if any.</param>
/// <param name="Parts">The item it's for, as "Slot - Item".</param>
internal sealed record CheckHint(string Caption, Color? DotColor, (string Text, Color Color)[] Parts);

/// <summary>One of a room's checks. The arrays are compared by reference, so reuse the same ones while they haven't changed.</summary>
/// <param name="Name">What to call it, which can leave out its room.</param>
/// <param name="Location">Its full location name.</param>
/// <param name="Requires">What's still missing for a locked check, where that's known.</param>
/// <param name="HintOptions">What's left to request by clicking it.</param>
/// <param name="Hint">The hint on the check itself. Null once it's checked.</param>
internal readonly record struct CheckPip(string Name, string Location, CheckStatus Status, RequirementLine[] Requires, HintOption[] HintOptions, CheckHint? Hint)
{
    public Color DotColor => Status switch
    {
        CheckStatus.Checked => TrackerTheme.PipLocked,
        _ when Hint?.DotColor is { } hinted => hinted,
        CheckStatus.Locked => TrackerTheme.PipLocked,
        _ => TrackerTheme.Accent,
    };

    public bool DotFilled => Status != CheckStatus.Locked;
}

/// <summary>What a room tile shows. Without <paramref name="Checks"/> it's just a name and tag.</summary>
/// <param name="Checks">The room's checks, in the order to draw them.</param>
/// <param name="IsLocked">Whether something's missing to enter the room, which dims the tile.</param>
/// <param name="IsHere">Whether the player's in this room, which highlights the tile and adds a map pin.</param>
/// <param name="NeedsCard">Whether the room's Access Card is still missing, which adds a lock unless there's a map pin.</param>
/// <param name="CardHint">The Access Card's hints, for the bottom of the tile, if it has any or can have any.</param>
internal readonly record struct RoomTileState(string Name, CheckPip[] Checks, string Tag, bool IsLocked, bool IsHere, bool NeedsCard, HintTarget? CardHint);

/// <summary>One room: its name, a dot per check, and a line on where it stands.</summary>
internal sealed class RoomTile : TrackerControl
{
    private const int CornerIconSize = 12;
    private const int CornerIconInset = 5;

    private RoomTileState _state = new("", Array.Empty<CheckPip>(), "", false, false, false, null);
    private int _hoveredIndex = -1;
    private CheckPip? _hoveredPip;
    private HintTarget? _hoveredCard;
    private bool _mouseInside;

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
            if (_mouseInside)
                UpdateHover(PointToClient(Cursor.Position));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _mouseInside = true;
        UpdateHover(e.Location);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _mouseInside = false;
        UpdateHover(new Point(-1, -1));
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left)
            return;

        int index = PipAt(e.Location);
        if (index >= 0)
            RaiseHintClicked(_state.Checks[index].HintOptions);
        else if (CardAt(e.Location) is { } card)
            RaiseHintClicked(card.Options);
    }

    private void UpdateHover(Point mouse)
    {
        int index = PipAt(mouse);
        CheckPip? pip = index >= 0 ? _state.Checks[index] : null;
        HintTarget? card = index >= 0 ? null : CardAt(mouse);
        ShowHandCursor((pip?.HintOptions.Length ?? card?.Options.Length ?? 0) > 0);

        if (index != _hoveredIndex || pip != _hoveredPip)
        {
            _hoveredIndex = index;
            _hoveredPip = pip;
            PipHoverChanged?.Invoke(this, pip, index >= 0 ? PipBounds(index) : Rectangle.Empty);
        }
        if (card != _hoveredCard)
        {
            _hoveredCard = card;
            RaiseHintHover(card, BottomBounds);
        }
    }

    /// <summary>The bottom of the tile, where its tag and corner icon are.</summary>
    private Rectangle BottomBounds => new(0, Height - S(19), Width, S(19));

    private HintTarget? CardAt(Point point) => BottomBounds.Contains(point) ? _state.CardHint : null;

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
        int left = S(6), pip = S(12), pitch = pip + S(3);
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
            DrawPip(e.Graphics, pip.X, pip.Y, pip.Width, _state.Checks[i].DotColor, _state.Checks[i].DotFilled);
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
    protected const int CaptionGap = 6;
    protected const string ClickToHint = "Click to hint";
    private const int WmNcHitTest = 0x84;
    private static readonly IntPtr HtTransparent = new(-1);

    protected HoverPopup()
    {
        Visible = false;
    }

    /// <summary>Shows the popup beside <paramref name="anchor"/> (in the parent's coordinates), inside the parent, sized to its content.</summary>
    protected void ShowBeside(Rectangle anchor, int contentWidth, int contentHeight) =>
        ShowBesideAnchor(anchor, contentWidth + S(Inset) * 2, contentHeight + S(Inset) * 2);

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

    /// <summary>Draws a line of colored pieces and returns where the next one goes.</summary>
    protected int DrawPartsLine(Graphics g, (string Text, Color Color)[] parts, Font font, int x, int y)
    {
        int height = Measure("Ag", font).Height;
        DrawParts(g, parts, font, new Rectangle(x, y, Width - S(Inset) - x, height));
        return y + height;
    }

    /// <summary>The size of a list of hints, followed by the line saying more can be requested if there's anything to request.</summary>
    protected Size MeasureHints((string Text, Color Color)[][] hints, HintOption[] options, int gap)
    {
        int width = 0, height = 0;
        foreach ((string Text, Color Color)[] hint in hints)
        {
            width = Math.Max(width, MeasureParts(hint, TrackerTheme.TileNameFont));
            height += Measure("Ag", TrackerTheme.TileNameFont).Height;
        }
        if (options.Length > 0)
        {
            Size line = Measure(ClickToHint, TrackerTheme.CaptionFont);
            width = Math.Max(width, line.Width);
            height += gap + line.Height;
        }
        return new Size(width, height);
    }

    /// <summary>Draws what <see cref="MeasureHints"/> measured.</summary>
    protected void DrawHints(Graphics g, (string Text, Color Color)[][] hints, HintOption[] options, int gap, int x, int y)
    {
        foreach ((string Text, Color Color)[] hint in hints)
            y = DrawPartsLine(g, hint, TrackerTheme.TileNameFont, x, y);
        if (options.Length > 0)
            DrawLine(g, ClickToHint, TrackerTheme.CaptionFont, TrackerTheme.Accent, x, y + gap);
    }
}

/// <summary>Names an Access Card, Titania part or the clues, with where they're hinted to be and whether more can be requested.</summary>
internal sealed class HintPopup : HoverPopup
{
    private HintTarget _target = new("", Array.Empty<(string Text, Color Color)[]>(), Array.Empty<HintOption>());

    /// <summary>Shows a target beside <paramref name="anchor"/> (in the parent's coordinates), inside the parent.</summary>
    public void ShowFor(HintTarget target, Rectangle anchor)
    {
        _target = target;
        Size title = Measure(target.Title, TrackerTheme.TileNameFont);
        Size hints = MeasureHints(target.Hints, target.Options, S(CaptionGap));
        ShowBeside(anchor, Math.Max(title.Width, hints.Width), title.Height + hints.Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        FillTile(e.Graphics, TrackerTheme.Surface, _target.Options.Length > 0 ? TrackerTheme.Accent : TrackerTheme.PipLocked);

        int x = S(Inset), y = S(Inset);
        y = DrawLine(e.Graphics, _target.Title, TrackerTheme.TileNameFont, TrackerTheme.Text, x, y);
        DrawHints(e.Graphics, _target.Hints, _target.Options, S(CaptionGap), x, y);
    }
}

/// <summary>Names the check under the mouse and where it stands, any hint on it and, if it's locked, what it still
/// requires and where any of that is hinted to be.</summary>
internal sealed class CheckPopup : HoverPopup
{
    private const int MaxLinesPerColumn = 20; // short enough to fit above or below anything at the minimum window size
    private const int ColumnGap = 14;
    private const int HintIndent = 10;
    private const string RequiresCaption = "REQUIRES";

    private readonly record struct Cell(int Column, int Row, (string Text, Color Color)[] Parts, int Indent);

    private readonly List<Cell> _cells = new();
    private CheckPip _check;
    private int _columnWidth;
    private int _lineHeight;
    private int _rows;

    /// <summary>Shows a check beside <paramref name="anchor"/> (in the parent's coordinates), inside the parent.</summary>
    public void ShowFor(CheckPip check, Rectangle anchor)
    {
        _check = check;

        Size nameSize = Measure(check.Name, TrackerTheme.TileNameFont);
        int width = nameSize.Width, height = nameSize.Height;
        void AddLine(int lineWidth, int lineHeight, int gap)
        {
            width = Math.Max(width, lineWidth);
            height += gap + lineHeight;
        }

        if (StatusCaption(check) is { } status)
        {
            Size size = Measure(status, TrackerTheme.CaptionFont);
            AddLine(size.Width, size.Height, S(CaptionGap));
        }
        if (check.Hint is { } hint)
        {
            Size caption = Measure(hint.Caption, TrackerTheme.CaptionFont);
            AddLine(caption.Width, caption.Height, S(CaptionGap));
            AddLine(MeasureParts(hint.Parts, TrackerTheme.TileNameFont), Measure("Ag", TrackerTheme.TileNameFont).Height, 0);
        }
        if (check.Requires.Length > 0)
        {
            Size caption = Measure(RequiresCaption, TrackerTheme.CaptionFont);
            AddLine(caption.Width, caption.Height, S(CaptionGap));
            int columns = LayOutRequires(check.Requires);
            AddLine(columns * _columnWidth + (columns - 1) * S(ColumnGap), _rows * _lineHeight, 0);
        }
        if (check.HintOptions.Length > 0)
        {
            Size line = Measure(ClickToHint, TrackerTheme.CaptionFont);
            AddLine(line.Width, line.Height, S(CaptionGap));
        }
        ShowBeside(anchor, width, height);
    }

    /// <summary>Flows the requirements down columns, keeping each with its hints. Returns how many columns that takes.</summary>
    private int LayOutRequires(RequirementLine[] requires)
    {
        _cells.Clear();
        _columnWidth = 0;
        _rows = 0;
        _lineHeight = Measure("Ag", TrackerTheme.TileNameFont).Height;

        int column = 0, row = 0;
        void AddCell((string Text, Color Color)[] parts, int indent)
        {
            _cells.Add(new Cell(column, row, parts, indent));
            _columnWidth = Math.Max(_columnWidth, indent + MeasureParts(parts, TrackerTheme.TileNameFont));
            row++;
            _rows = Math.Max(_rows, row);
        }

        foreach (RequirementLine requirement in requires)
        {
            if (row > 0 && row + 1 + requirement.Hints.Length > MaxLinesPerColumn)
            {
                column++;
                row = 0;
            }
            AddCell(new[] { (requirement.Text, TrackerTheme.Text) }, 0);
            foreach ((string Text, Color Color)[] hint in requirement.Hints)
                AddCell(hint, S(HintIndent));
        }
        return column + 1;
    }

    private static string? StatusCaption(CheckPip check) => check.Status switch
    {
        CheckStatus.Checked => "COMPLETED",
        CheckStatus.InLogic => "IN LOGIC",
        _ => null,
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        FillTile(g, TrackerTheme.Surface, _check.DotColor);

        int x = S(Inset), y = S(Inset), gap = S(CaptionGap);
        y = DrawLine(g, _check.Name, TrackerTheme.TileNameFont, TrackerTheme.Text, x, y);
        if (StatusCaption(_check) is { } status)
            y = DrawLine(g, status, TrackerTheme.CaptionFont, TrackerTheme.TextMuted, x, y + gap);
        if (_check.Hint is { } hint)
        {
            y = DrawLine(g, hint.Caption, TrackerTheme.CaptionFont, hint.DotColor ?? TrackerTheme.TextMuted, x, y + gap);
            y = DrawPartsLine(g, hint.Parts, TrackerTheme.TileNameFont, x, y);
        }
        if (_check.Requires.Length > 0)
        {
            y = DrawLine(g, RequiresCaption, TrackerTheme.CaptionFont, TrackerTheme.TextMuted, x, y + gap);
            foreach (Cell cell in _cells)
            {
                int left = x + cell.Column * (_columnWidth + S(ColumnGap));
                DrawParts(g, cell.Parts, TrackerTheme.TileNameFont, new Rectangle(left + cell.Indent, y + cell.Row * _lineHeight, _columnWidth - cell.Indent, _lineHeight));
            }
            y += _rows * _lineHeight;
        }
        if (_check.HintOptions.Length > 0)
            DrawLine(g, ClickToHint, TrackerTheme.CaptionFont, TrackerTheme.Accent, x, y + gap);
    }
}

/// <summary>Shows what the clue under the mouse said and whether it turned up anything, then where the clues still to
/// come are hinted to be and whether more can be requested.</summary>
internal sealed class CluePopup : HoverPopup
{
    private const int MaxTextWidth = 300;
    private const TextFormatFlags Wrapped = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak;

    private ClueEntry _clue;
    private HintTarget? _hint;
    private Size _textSize;

    /// <summary>Shows a clue beside <paramref name="anchor"/> (in the parent's coordinates), inside the parent.</summary>
    public void ShowFor(ClueEntry clue, HintTarget? hint, Rectangle anchor)
    {
        _clue = clue;
        _hint = hint;

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
        if (hint is not null)
        {
            Size hints = MeasureHints(hint.Hints, hint.Options, S(CaptionGap));
            width = Math.Max(width, hints.Width);
            height += S(CaptionGap) + hints.Height;
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
            y = DrawLine(e.Graphics, discovery, TrackerTheme.TileNameFont, TrackerTheme.Accent, x, y);
        if (_hint is not null)
            DrawHints(e.Graphics, _hint.Hints, _hint.Options, S(CaptionGap), x, y + S(CaptionGap));
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
    private HintTarget? _hint;
    private bool _hovered;

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

    /// <summary>The tile's item, if it has hints or can have them.</summary>
    public void SetHint(HintTarget? hint)
    {
        if (hint == _hint)
            return;
        _hint = hint;
        if (_hovered)
            UpdateHover();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        UpdateHover();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = false;
        UpdateHover();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left && _hint is not null)
            RaiseHintClicked(_hint.Options);
    }

    private void UpdateHover()
    {
        ShowHandCursor(_hovered && _hint?.Options.Length > 0);
        RaiseHintHover(_hovered ? _hint : null, ClientRectangle);
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

/// <summary>A small labeled value in the command bar, highlighted when it needs attention, or in red when it's a problem.</summary>
internal sealed class StatusTile : TrackerControl
{
    private string _label = "";
    private string _value = "";
    private bool _highlighted;
    private bool _alert;

    /// <summary>Whether the mouse is over the tile.</summary>
    public bool IsHovered { get; private set; }

    /// <summary>The mouse moved onto or off the tile.</summary>
    public event Action? HoverChanged;

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        IsHovered = true;
        HoverChanged?.Invoke();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        IsHovered = false;
        HoverChanged?.Invoke();
    }

    public void Set(string label, string value, bool highlighted, bool alert = false)
    {
        if (label == _label && value == _value && highlighted == _highlighted && alert == _alert)
            return;
        _label = label;
        _value = value;
        _highlighted = highlighted;
        _alert = alert;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        (Color fill, Color border, Color valueColor) = true switch
        {
            _ when _alert => (TrackerTheme.ErrorFill, TrackerTheme.Error, TrackerTheme.Error),
            _ when _highlighted => (TrackerTheme.AccentFill, TrackerTheme.Accent, TrackerTheme.Accent),
            _ => (TrackerTheme.Background, TrackerTheme.Line, TrackerTheme.Text),
        };
        FillTile(e.Graphics, fill, border);
        TextRenderer.DrawText(e.Graphics, _label, TrackerTheme.CaptionFont, new Rectangle(0, S(6), Width, S(14)),
            TrackerTheme.TextMuted, SingleLine | TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(e.Graphics, _value, TrackerTheme.BodyFont, new Rectangle(0, S(21), Width, S(19)),
            valueColor, SingleLine | TextFormatFlags.HorizontalCenter);
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
    private string _title;
    private const int FirstLineHeight = 17;

    private (string Text, Color? Pip, bool PipFilled)[] _legend;

    /// <summary>More lines of legend under the first, sharing the height below it, for a header tall enough to hold them.</summary>
    public (string Text, Color? Pip, bool PipFilled)[][] ExtraLegendLines { get; init; } = Array.Empty<(string, Color?, bool)[]>();

    public SectionHeader(string title, params (string Text, Color? Pip, bool PipFilled)[] legend)
    {
        _title = title;
        _legend = legend;
    }

    public void SetTitle(string title)
    {
        if (title == _title)
            return;
        _title = title;
        Invalidate();
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
        int firstLine = ExtraLegendLines.Length > 0 ? S(FirstLineHeight) : Height;
        TextRenderer.DrawText(e.Graphics, _title, TrackerTheme.CaptionFont, new Rectangle(0, 0, Width, firstLine), TrackerTheme.TextMuted, SingleLine | TextFormatFlags.VerticalCenter);

        DrawLegend(e.Graphics, _legend, 0, firstLine);
        for (int line = 0; line < ExtraLegendLines.Length; line++)
        {
            int lineHeight = (Height - firstLine) / ExtraLegendLines.Length;
            DrawLegend(e.Graphics, ExtraLegendLines[line], firstLine + line * lineHeight, lineHeight);
        }
    }

    private void DrawLegend(Graphics g, (string Text, Color? Pip, bool PipFilled)[] legend, int top, int height)
    {
        int x = Width;
        int pip = S(8);
        for (int i = legend.Length - 1; i >= 0; i--)
        {
            (string text, Color? pipColor, bool filled) = legend[i];
            int textWidth = TextRenderer.MeasureText(g, text, TrackerTheme.CaptionFont, Size.Empty, SingleLine).Width;
            x -= textWidth;
            TextRenderer.DrawText(g, text, TrackerTheme.CaptionFont, new Rectangle(x, top, textWidth, height), TrackerTheme.TextMuted, SingleLine | TextFormatFlags.VerticalCenter);
            if (pipColor is { } color)
            {
                x -= pip + S(4);
                DrawPip(g, x, top + (height - pip) / 2, pip, color, filled);
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
    private HintTarget? _hoveredBarHint;
    private HintTarget? _shownClueHint;
    private bool _mouseInside;

    /// <summary>The found clue under the mouse changed: its entry (null for none) and its dot's bounds in the bar.</summary>
    public event Action<ClueEntry?, Rectangle>? ClueHoverChanged;

    /// <summary>The hints for the clues still to come, if there are any or there can be.</summary>
    public HintTarget? Hint { get; private set; }

    /// <param name="clues">The found clues in order, where they're known.</param>
    public void Set(int found, int total, ClueEntry[] clues, HintTarget? hint)
    {
        if (found == _found && total == _total && hint == Hint && clues.SequenceEqual(_clues))
            return;
        _found = found;
        _total = total;
        _clues = clues;
        Hint = hint;
        Invalidate();
        if (_mouseInside)
            UpdateHover(PointToClient(Cursor.Position));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _mouseInside = true;
        UpdateHover(e.Location);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _mouseInside = false;
        UpdateHover(new Point(-1, -1));
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left && Hint is not null)
            RaiseHintClicked(Hint.Options);
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
        bool inside = ClientRectangle.Contains(mouse);
        HintTarget? barHint = inside && clue is null ? Hint : null;
        ShowHandCursor(inside && Hint?.Options.Length > 0);

        if (index != _hoveredIndex || clue != _hoveredClue || (clue is not null && Hint != _shownClueHint))
        {
            _hoveredIndex = index;
            _hoveredClue = clue;
            _shownClueHint = Hint;
            ClueHoverChanged?.Invoke(clue, index >= 0 ? DotBounds(index) : Rectangle.Empty);
        }
        if (barHint != _hoveredBarHint)
        {
            _hoveredBarHint = barHint;
            RaiseHintHover(barHint, ClientRectangle);
        }
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
/// <param name="RoomFlags">The PET's room flags where the player's standing, drawn as a chevron behind the panel.</param>
internal readonly record struct HereState(string RoomName, CheckPip[] Checks, string Message, uint? RoomFlags);

/// <summary>The room the player's in and the checks still to get there, row by row across two columns.</summary>
internal sealed class HerePanel : TrackerControl
{
    /// <summary>The room's name and four rows of checks, enough for the Restaurant's seven. Grows for hints.</summary>
    public const int DefaultHeight = 106;

    private HereState _state = new("", Array.Empty<CheckPip>(), "", null);
    private const int RoomNameHeight = 28;
    private const int CheckLineHeight = 18;
    private const int HintLineHeight = 14;
    private const int BottomPadding = 6;

    private int _hoveredIndex = -1;
    private CheckPip? _hoveredCheck;
    private bool _mouseInside;

    /// <summary>The check under the mouse changed: the panel, its check (null for none) and its line's bounds in the panel.</summary>
    public event Action<HerePanel, CheckPip?, Rectangle>? CheckHoverChanged;

    /// <summary>The height that shows every check and its hint.</summary>
    public int NeededHeight
    {
        get
        {
            int rows = (_state.Checks.Length + 1) / 2;
            return S(RoomNameHeight + BottomPadding) + Enumerable.Range(0, rows).Sum(RowHeight);
        }
    }

    public HereState State
    {
        set
        {
            if (value with { Checks = _state.Checks } == _state && value.Checks.SequenceEqual(_state.Checks))
                return;
            _state = value;
            Invalidate();
            if (_mouseInside)
                UpdateHover(PointToClient(Cursor.Position));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _mouseInside = true;
        UpdateHover(e.Location);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _mouseInside = false;
        UpdateHover(new Point(-1, -1));
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        int index = CheckAt(e.Location);
        if (e.Button == MouseButtons.Left && index >= 0)
            RaiseHintClicked(_state.Checks[index].HintOptions);
    }

    private void UpdateHover(Point mouse)
    {
        int index = CheckAt(mouse);
        CheckPip? check = index >= 0 ? _state.Checks[index] : null;
        ShowHandCursor(check?.HintOptions.Length > 0);
        if (index == _hoveredIndex && check == _hoveredCheck)
            return;
        _hoveredIndex = index;
        _hoveredCheck = check;
        CheckHoverChanged?.Invoke(this, check, index >= 0 ? LineBounds(index) : Rectangle.Empty);
    }

    private int CheckAt(Point point)
    {
        for (int i = 0; i < _state.Checks.Length; i++)
        {
            if (LineBounds(i).Contains(point))
                return i;
        }
        return -1;
    }

    /// <summary>A row is as tall as its tallest check, so the two columns stay lined up.</summary>
    private int RowHeight(int row)
    {
        CheckPip[] checks = _state.Checks;
        bool hasHint = checks[row * 2].Hint is not null || (row * 2 + 1 < checks.Length && checks[row * 2 + 1].Hint is not null);
        return S(hasHint ? CheckLineHeight + HintLineHeight : CheckLineHeight);
    }

    /// <summary>A check's line and the hint under it. With no checks, where the panel's message goes.</summary>
    private Rectangle LineBounds(int index)
    {
        int left = S(8), columnWidth = (Width - left * 2) / 2;
        int top = S(RoomNameHeight);
        for (int row = 0; row < index / 2; row++)
            top += RowHeight(row);
        int height = _state.Checks.Length > 0 ? RowHeight(index / 2) : S(CheckLineHeight);
        return new Rectangle(left + index % 2 * columnWidth, top, columnWidth, height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        FillTile(e.Graphics, TrackerTheme.Background, TrackerTheme.LineStrong);
        if (_state.RoomFlags is { } roomFlags and not 0)
            DrawChevron(e.Graphics, roomFlags);

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
            CheckPip check = _state.Checks[i];
            Rectangle line = LineBounds(i);
            int nameHeight = S(CheckLineHeight), textWidth = line.Width - textOffset - S(4);
            bool locked = check.Status == CheckStatus.Locked;
            DrawPip(e.Graphics, line.X, line.Y + (nameHeight - pip) / 2, pip, check.DotColor, check.DotFilled);
            TextRenderer.DrawText(e.Graphics, check.Name, TrackerTheme.TileNameFont, new Rectangle(line.X + textOffset, line.Y, textWidth, nameHeight),
                locked ? TrackerTheme.TextMuted : TrackerTheme.Text, SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (check.Hint is { } hint)
                DrawParts(e.Graphics, hint.Parts, TrackerTheme.CaptionFont, new Rectangle(line.X + textOffset, line.Y + nameHeight, textWidth, S(HintLineHeight)), textWidth / 2);
        }
    }

    /// <summary>The room's chevron, faint against the panel's right side and as tall as its default height. Follows
    /// CPetRoomsGlyph: the low 20 bits of the room flags, most significant first, as five rows of four segments that
    /// zigzag up and down, lit for a 1.</summary>
    private void DrawChevron(Graphics g, uint roomFlags)
    {
        // Measured in tenths of a row: each segment is 13 wide, rises 5 and is 3 thick, so the chevron is 52 by 48
        int padding = S(6);
        float unit = (S(DefaultHeight) - padding * 2) / 48f;
        float left = Width - padding - 52 * unit, thickness = 3 * unit;
        using var lit = new SolidBrush(Color.FromArgb(48, TrackerTheme.Accent));
        using var unlit = new SolidBrush(Color.FromArgb(14, TrackerTheme.Accent));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        for (int row = 0; row < 5; row++)
        {
            uint bits = (roomFlags >> (16 - row * 4)) & 0xF;
            float top = padding + row * 10 * unit, bottom = top + 5 * unit;
            for (int column = 0; column < 4; column++)
            {
                float x0 = left + column * 13 * unit, x1 = x0 + 13 * unit;
                (float y0, float y1) = column % 2 == 0 ? (bottom, top) : (top, bottom);
                PointF[] segment = { new(x0, y0), new(x1, y1), new(x1, y1 + thickness), new(x0, y0 + thickness) };
                g.FillPolygon((bits & (8u >> column)) != 0 ? lit : unlit, segment);
            }
        }
        g.SmoothingMode = SmoothingMode.Default;
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
