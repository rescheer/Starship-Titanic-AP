namespace StarshipTitanicAp;

/// <summary>Asks before spending hint points, by the mouse. Knows only the list of things it's offering, not what was
/// clicked to open it. Unlike a <see cref="HoverPopup"/> it takes clicks, and it closes on a click anywhere else.</summary>
internal sealed class HintConfirmBox : TrackerControl, IMessageFilter
{
    private const int MaxTextWidth = 260;
    private const int LineGap = 6;
    private const int RowGap = 4;
    private const int ColumnGap = 14;
    private const int ButtonHeight = 24;
    private const int ButtonGap = 6;
    private const int TextToButtonGap = 8;
    private const TextFormatFlags Wrapped = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak;

    private const int WmKeyDown = 0x100;
    private const int WmLButtonDown = 0x201;
    private const int WmRButtonDown = 0x204;
    private const int WmMButtonDown = 0x207;
    private const int WmNcLButtonDown = 0xA1;
    private const int WmNcRButtonDown = 0xA4;
    private const int VkEscape = 0x1B;

    private readonly record struct TextBlock(string Text, Rectangle Bounds, Color Color);

    private readonly List<TextBlock> _text = new();
    private readonly List<Button> _buttons = new();
    private HintOption[] _options = Array.Empty<HintOption>();
    private Point _anchor;
    private int _cost;
    private int _points;

    /// <summary>The player chose these. An option's <see cref="HintOption.Copies"/> is how many requests to send for it.</summary>
    public event Action<HintOption[]>? Requested;

    public HintOption[] Options => _options;

    /// <summary>Opens the box at <paramref name="anchor"/> (in the parent's coordinates).</summary>
    public void Open(HintOption[] options, int cost, int points, Point anchor)
    {
        if (!Visible)
            Application.AddMessageFilter(this);
        _anchor = anchor;
        LayOut(options, cost, points);
    }

    /// <summary>Brings an open box up to date, closing it if there's nothing left to offer.</summary>
    public void UpdateOptions(HintOption[] options, int cost, int points)
    {
        if (!Visible || (cost == _cost && points == _points && options.SequenceEqual(_options)))
            return;

        if (options.Length == 0)
            Close();
        else
            LayOut(options, cost, points);
    }

    public void Close()
    {
        if (!Visible)
            return;
        Application.RemoveMessageFilter(this);
        Visible = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Application.RemoveMessageFilter(this);
        base.Dispose(disposing);
    }

    private bool CanAfford(int hints) => _cost == 0 || _points >= _cost * hints;

    private static string Points(int points) => points == 1 ? "1 point" : $"{points} points";

    private void LayOut(HintOption[] options, int cost, int points)
    {
        _options = options;
        _cost = cost;
        _points = points;

        _text.Clear();
        foreach (Button old in _buttons)
        {
            Controls.Remove(old);
            old.Dispose();
        }
        _buttons.Clear();

        Size size = options.Length == 1 ? LayOutSingle(options[0]) : LayOutList();
        ShowBesideAnchor(new Rectangle(_anchor, Size.Empty), size.Width, size.Height);
    }

    private int TextHeight(string text, int width) =>
        TextRenderer.MeasureText(text, TrackerTheme.TileNameFont, new Size(width, int.MaxValue), Wrapped).Height;

    private Button AddButton(string text, bool enabled, Action onClick)
    {
        var button = new Button { Text = text, Enabled = enabled, Height = S(ButtonHeight), TabStop = false };
        button.Width = TextRenderer.MeasureText(text, TrackerTheme.TileNameFont).Width + S(16);
        TrackerTheme.StyleButton(button);
        button.Click += (_, _) => onClick();
        Controls.Add(button);
        _buttons.Add(button);
        return button;
    }

    /// <summary>Closes first, so that what the request sets off can't rebuild the box out from under the button being clicked.</summary>
    private void Choose(params HintOption[] options)
    {
        Close();
        Requested?.Invoke(options);
    }

    /// <summary>Adds the cost line and the buttons along the bottom, right-aligned to <paramref name="width"/>. Returns the height used.</summary>
    private int LayOutFooter(string? costLine, Button? main, int width, int top)
    {
        int y = top;
        if (costLine is not null)
        {
            int height = TextHeight(costLine, width);
            _text.Add(new TextBlock(costLine, new Rectangle(S(Inset), y, width, height), TrackerTheme.TextMuted));
            y += height + S(LineGap);
        }

        Button cancel = AddButton("Cancel", true, Close);
        int right = S(Inset) + width;
        cancel.Location = new Point(right - cancel.Width, y);
        if (main is not null)
            main.Location = new Point(cancel.Left - S(ButtonGap) - main.Width, y);
        return y + S(ButtonHeight) - top;
    }

    private Size LayOutSingle(HintOption option)
    {
        int width = S(MaxTextWidth), y = S(Inset);
        string prompt = $"Request hint for {option.Label}?";
        int promptHeight = TextHeight(prompt, width);
        _text.Add(new TextBlock(prompt, new Rectangle(S(Inset), y, width, promptHeight), TrackerTheme.Text));
        y += promptHeight + S(LineGap);

        string? costLine = _cost > 0 ? $"Costs {Points(_cost)}. You have {_points}." : null;
        Button request = AddButton("Request", CanAfford(1), () => Choose(option with { Copies = 1 }));
        y += LayOutFooter(costLine, request, width, y);
        return new Size(width + S(Inset) * 2, y + S(Inset));
    }

    /// <summary>A row per option under the contents row, if there is one, flowing into more columns if they wouldn't
    /// fit the window, then the cost and buttons underneath.</summary>
    private Size LayOutList()
    {
        HintOption[] contents = _options.Where(option => option.IsLocation).ToArray();
        HintOption[] items = _options.Where(option => !option.IsLocation).ToArray();
        int allHints = items.Sum(option => option.Copies);

        string? costLine = true switch
        {
            _ when _cost == 0 => null,
            _ when items.Length > 1 => $"Costs {Points(_cost)} each, {_cost * allHints} for all. You have {_points}.",
            _ => $"Costs {Points(_cost)} each. You have {_points}.",
        };

        int columnWidth = S(MaxTextWidth);
        int hintButtonWidth = TextRenderer.MeasureText("Hint", TrackerTheme.TileNameFont).Width + S(16);
        int labelWidth = columnWidth - hintButtonWidth - S(TextToButtonGap);

        int footerHeight = (costLine is null ? 0 : TextHeight(costLine, columnWidth) + S(LineGap)) + S(ButtonHeight);
        int maxBottom = Parent!.ClientSize.Height - S(4) * 2 - S(Inset) - footerHeight - S(LineGap);

        int column = 0, y = S(Inset), bottom = y;
        int Left() => S(Inset) + column * (columnWidth + S(ColumnGap));
        void NextColumnIfNeeded(int height)
        {
            if (y + height > maxBottom && y > S(Inset))
            {
                column++;
                y = S(Inset);
            }
        }
        void AddRow(HintOption option, string label)
        {
            int height = Math.Max(TextHeight(label, labelWidth), S(ButtonHeight));
            NextColumnIfNeeded(height);
            _text.Add(new TextBlock(label, new Rectangle(Left(), y + Math.Max(0, (S(ButtonHeight) - TextHeight(label, labelWidth)) / 2), labelWidth, height), TrackerTheme.Text));
            Button hint = AddButton("Hint", CanAfford(1), () => Choose(option with { Copies = 1 }));
            hint.Location = new Point(Left() + columnWidth - hint.Width, y);
            y += height + S(RowGap);
            bottom = Math.Max(bottom, y);
        }

        foreach (HintOption option in contents)
            AddRow(option, char.ToUpperInvariant(option.Label[0]) + option.Label[1..]);

        if (items.Length > 0)
        {
            const string heading = "Request hint for:";
            int headingHeight = TextHeight(heading, columnWidth);
            NextColumnIfNeeded(headingHeight + S(ButtonHeight));
            _text.Add(new TextBlock(heading, new Rectangle(Left(), y, columnWidth, headingHeight), TrackerTheme.TextMuted));
            y += headingHeight + S(RowGap);
            bottom = Math.Max(bottom, y);
        }
        foreach (HintOption option in items)
            AddRow(option, option.Label);

        int width = (column + 1) * columnWidth + column * S(ColumnGap);
        Button? main = items.Length > 1 ? AddButton("Hint all", CanAfford(allHints), () => Choose(items)) : null;

        int footerTop = bottom + S(LineGap) - S(RowGap);
        int footer = LayOutFooter(costLine, main, width, footerTop);
        return new Size(width + S(Inset) * 2, footerTop + footer + S(Inset));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        FillTile(e.Graphics, TrackerTheme.Surface, TrackerTheme.Accent);
        foreach (TextBlock block in _text)
            TextRenderer.DrawText(e.Graphics, block.Text, TrackerTheme.TileNameFont, block.Bounds, block.Color, Wrapped);
    }

    /// <summary>Sees every click in the window, including on the native log and chat box, which the view can't. Lets
    /// them carry on to what was clicked, so that clicking another check goes straight to its box.</summary>
    bool IMessageFilter.PreFilterMessage(ref Message m)
    {
        switch (m.Msg)
        {
            case WmKeyDown when (int)m.WParam == VkEscape:
                Close();
                return true;

            case WmLButtonDown or WmRButtonDown or WmMButtonDown or WmNcLButtonDown or WmNcRButtonDown:
                Control? target = Control.FromChildHandle(m.HWnd);
                if (target is null || (target != this && !Contains(target)))
                    Close();
                return false;

            default:
                return false;
        }
    }
}
