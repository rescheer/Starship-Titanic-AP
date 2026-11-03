namespace StarshipTitanicAp;

/// <summary>One deck row's state: whether its label is lit, and each of its rooms in <see cref="RoomTiles"/> order.</summary>
internal sealed record DeckState(bool IsReached, RoomTileState[] Rooms);

/// <summary>One Titania part's state, in <see cref="TitaniaParts"/> order.</summary>
internal enum PartState { NotReceived, InMail, InHand }

/// <summary>The goal rectangles: Titania's parts or, under Parrot Hunt, the parrot's address.</summary>
internal abstract record GoalState
{
    /// <param name="Title">The section's label, which names the goal once it's known.</param>
    /// <param name="InHandCount">Null while it can't be known.</param>
    /// <param name="UsesMail">False under Direct To Inventory, which has no use for the "in the mail" legend entry.</param>
    /// <param name="FromServer">Whether <see cref="PartState.InHand"/> only means the AP server says the part was received, while there's no game to ask.</param>
    /// <param name="PartHints">Each part's hints, for those that have any or can have any.</param>
    public sealed record Titania(string Title, PartState[] Parts, int? InHandCount, bool UsesMail, bool FromServer, HintTarget?[] PartHints) : GoalState;

    /// <param name="Elevator">Each part of the address is null until revealed.</param>
    /// <param name="Clues">The clues found so far, in order. Empty while not connected, since their text depends on the seed.</param>
    /// <param name="ClueHint">The hints for the clues still to come, if there are any or there can be.</param>
    public sealed record Parrot(int? Elevator, int? Floor, int? Room, int CluesFound, int CluesTotal, ClueEntry[] Clues, HintTarget? ClueHint) : GoalState;
}

/// <summary>Everything the main window shows, besides the log and Recent lines.</summary>
internal sealed record TrackerState(
    string GameValue, bool GameHasError, string GameButton,
    string ApValue, Color ApValueColor, bool ApValueClickable, bool ApHasError, string ApButton, bool ApButtonEnabled,
    string HintsValue, int HintCost, int HintPoints,
    string InLogicValue, bool InLogicAlert,
    string GoModeValue, bool GoModeHighlighted, string[] GoModeRequires,
    string MailLabel, string MailValue, bool MailHighlighted,
    bool SaveSeedMismatch,
    DeckState[] Decks,
    HereState Here,
    GoalState Goal);

/// <summary>The main window's contents. Lays everything out by hand, so it all scales with the monitor's DPI. Knows
/// nothing about the session: <see cref="MainForm"/> works out a <see cref="TrackerState"/> and passes it in.</summary>
internal sealed class TrackerView : TrackerControl
{
    private const int CommandBarHeight = 62;
    private const int BannerHeight = 52;
    private const int BannerTextHeight = 28;
    private const int StatusTileWidth = 136;
    private const int TrackerHeight = 330;
    private const int GoalWidth = 420;
    private const int ChatRowHeight = 33;
    private const int MinRecentHeight = 24; // anything less isn't worth showing

    private const string InMailMark = "✉";
    private const string InHandMark = "✔";
    private const string UnknownMark = "?";

    public event Action? AttachButtonClicked;
    public event Action? ApButtonClicked;
    public event Action? ApValueClicked;
    public event Action<string>? ChatSubmitted;
    public event Action? ForceSeedClicked;

    /// <summary>The player confirmed these hints. An option's <see cref="HintOption.Copies"/> is how many requests to send for it.</summary>
    public event Action<HintOption[]>? HintRequested;

    private readonly ConnectionTile _gameTile = new("STARSHIP TITANIC");
    private readonly ConnectionTile _apTile = new("ARCHIPELAGO");
    private readonly StatusTile _hintsTile = new();
    private readonly StatusTile _inLogicTile = new();
    private readonly StatusTile _goModeTile = new();
    private string[] _goModeRequires = Array.Empty<string>();
    private RequirementLine[] _goModeLines = Array.Empty<RequirementLine>();
    private readonly StatusTile _mailTile = new();

    private readonly Label _saveSeedBanner = new()
    {
        Text = "Save seed does not match server seed! AP sync paused",
        TextAlign = ContentAlignment.BottomCenter,
        Font = new Font(TrackerTheme.BodyFont, FontStyle.Bold),
        BackColor = TrackerTheme.Error,
        ForeColor = TrackerTheme.Background,
        Visible = false,
    };
    private readonly LinkLabel _forceSeedLink = new()
    {
        Text = "Use this save with this seed anyway",
        TextAlign = ContentAlignment.TopCenter,
        Font = TrackerTheme.BodyFont,
        BackColor = TrackerTheme.Error,
        LinkColor = TrackerTheme.Background,
        ActiveLinkColor = TrackerTheme.Background,
        VisitedLinkColor = TrackerTheme.Background,
        LinkBehavior = LinkBehavior.AlwaysUnderline,
        Visible = false,
    };

    private readonly SectionHeader _roomsHeader = new("ROOMS");
    private readonly SectionHeader _roomsLegend = new("", // beside the Bridge, the one deck with space to spare
        ("Checked", TrackerTheme.PipLocked, true),
        ("In logic", TrackerTheme.Accent, true),
        ("Locked", TrackerTheme.PipLocked, false))
    {
        ExtraLegendLines = new[]
        {
            new (string, Color?, bool)[]
            {
                ("Hinted in logic", TrackerTheme.HintedDot, true),
                ("Hinted locked", TrackerTheme.HintedDot, false),
            },
            new (string, Color?, bool)[]
            {
                ("Avoid in logic", TrackerTheme.AvoidDot, true),
                ("Avoid locked", TrackerTheme.AvoidDot, false),
            },
        },
    };
    private readonly Label[] _deckLabels;
    private readonly RoomTile[][] _roomTiles;
    private readonly CheckPopup _checkPopup = new();
    private Control? _popupSource;
    private readonly HintPopup _hintPopup = new();
    private Control? _hintPopupSource;
    private readonly HintConfirmBox _hintBox = new() { Visible = false };
    private int _hintCost;
    private int _hintPoints;

    private static readonly (string Text, Color? Pip, bool PipFilled)[] TitaniaMailLegend =
    {
        ($"{InHandMark} In hand", null, false),
        ($"{InMailMark} In the mail", null, false),
        ($"{UnknownMark} Not received", null, false),
    };
    private static readonly (string Text, Color? Pip, bool PipFilled)[] TitaniaDirectLegend =
    {
        ($"{InHandMark} In hand", null, false),
        ($"{UnknownMark} Not received", null, false),
    };
    private static readonly (string Text, Color? Pip, bool PipFilled)[] TitaniaServerLegend =
    {
        ($"{InHandMark} Received", null, false),
        ($"{UnknownMark} Not received", null, false),
    };

    private readonly SectionHeader _titaniaHeader = new("TITANIA", TitaniaMailLegend);
    private readonly GoalTile[] _partTiles; // the parts total first, then each part
    private readonly SectionHeader _parrotHeader = new("GOAL: PARROT HUNT");
    private readonly GoalTile[] _parrotTiles = { new(), new(), new() };
    private readonly ClueBar _clueBar = new();
    private readonly CluePopup _cluePopup = new();

    private readonly RichTextBox _log = new()
    {
        ReadOnly = true,
        BorderStyle = BorderStyle.None,
        BackColor = TrackerTheme.LogBackground,
        ForeColor = TrackerTheme.Text,
        Font = TrackerTheme.LogFont,
        ScrollBars = RichTextBoxScrollBars.Vertical,
        WordWrap = true,
    };
    private readonly TextBox _chatInput = new() { Font = TrackerTheme.LogFont, PlaceholderText = "Message or /command" };
    private readonly Button _chatSend = new() { Text = "Send" };
    private readonly SectionHeader _hereHeader = new("YOU ARE HERE",
        ("In logic", TrackerTheme.Accent, true),
        ("Locked", TrackerTheme.PipLocked, false));
    private readonly HerePanel _here = new();
    private readonly SectionHeader _recentHeader = new("RECENT");
    private readonly RecentPanel _recent = new();

    private bool _showParrot;
    private bool _scrollLogWhenSized;
    private int _hereNeededHeight;

    public TrackerView()
    {
        TabStop = false;

        _deckLabels = RoomTiles.Decks
            .Select(deck => new Label { Text = deck.Label, Font = TrackerTheme.CaptionFont, ForeColor = TrackerTheme.TextMuted, BackColor = TrackerTheme.Background, TextAlign = ContentAlignment.TopLeft })
            .ToArray();
        _roomTiles = RoomTiles.Decks
            .Select(deck => deck.Rooms.Select(_ => new RoomTile()).ToArray())
            .ToArray();
        _partTiles = Enumerable.Range(0, TitaniaParts.All.Length + 1).Select(_ => new GoalTile()).ToArray();

        TrackerTheme.StyleTextBox(_chatInput);
        TrackerTheme.StyleButton(_chatSend);

        Controls.Add(_hintBox); // the box and popups are added first, so they're on top of everything else
        Controls.Add(_checkPopup);
        Controls.Add(_cluePopup);
        Controls.Add(_hintPopup);
        Controls.AddRange(new Control[] { _gameTile, _apTile, _hintsTile, _inLogicTile, _goModeTile, _mailTile, _saveSeedBanner, _forceSeedLink, _roomsHeader, _roomsLegend, _titaniaHeader, _parrotHeader, _clueBar, _log, _chatInput, _chatSend, _hereHeader, _here, _recentHeader, _recent });
        Controls.AddRange(_deckLabels);
        Controls.AddRange(_roomTiles.SelectMany(row => row).ToArray<Control>());
        Controls.AddRange(_partTiles.ToArray<Control>());
        Controls.AddRange(_parrotTiles.ToArray<Control>());

        foreach (RoomTile tile in _roomTiles.SelectMany(row => row))
            tile.PipHoverChanged += OnCheckHoverChanged;
        _here.CheckHoverChanged += OnCheckHoverChanged;
        _goModeTile.HoverChanged += UpdateGoModePopup;
        _clueBar.ClueHoverChanged += OnClueHoverChanged;
        foreach (TrackerControl hintable in _roomTiles.SelectMany(row => row).Concat<TrackerControl>(_partTiles).Append(_here).Append(_clueBar))
        {
            hintable.HintClicked += OpenHintBox;
            hintable.HintHoverChanged += OnHintHoverChanged;
        }
        _hintBox.Requested += options => HintRequested?.Invoke(options);
        _gameTile.Button.Click += (_, _) => AttachButtonClicked?.Invoke();
        _apTile.Button.Click += (_, _) => ApButtonClicked?.Invoke();
        _apTile.ValueClicked += () => ApValueClicked?.Invoke();
        _forceSeedLink.LinkClicked += (_, _) => ForceSeedClicked?.Invoke();
        _chatSend.Click += (_, _) => SubmitChat();
        _chatInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
                return;
            e.SuppressKeyPress = true;
            SubmitChat();
        };
        _log.HandleCreated += (_, _) => TrackerTheme.UseDarkScrollBars(_log);
        _log.SizeChanged += (_, _) =>
        {
            if (!_scrollLogWhenSized || !LogHasSize)
                return;
            _scrollLogWhenSized = false;
            ScrollLogToEnd();
        };
    }

    public void ClearChatInput() => _chatInput.Clear();

    public void Apply(TrackerState state)
    {
        _gameTile.Set(state.GameValue, state.GameHasError ? TrackerTheme.Error : TrackerTheme.Text, state.GameHasError, state.GameButton, buttonEnabled: true);
        _apTile.Set(state.ApValue, state.ApValueColor, state.ApHasError, state.ApButton, state.ApButtonEnabled, state.ApValueClickable);
        _hintsTile.Set("HINTS AVAILABLE", state.HintsValue, highlighted: false);
        _hintCost = state.HintCost;
        _hintPoints = state.HintPoints;
        _inLogicTile.Set("IN-LOGIC CHECKS", state.InLogicValue, highlighted: false, state.InLogicAlert);
        _goModeTile.Set("GO MODE", state.GoModeValue, state.GoModeHighlighted);
        if (!ReferenceEquals(_goModeRequires, state.GoModeRequires))
        {
            _goModeRequires = state.GoModeRequires;
            _goModeLines = state.GoModeRequires.Select(item => new RequirementLine(item, Array.Empty<(string Text, Color Color)[]>())).ToArray();
            UpdateGoModePopup();
        }
        _mailTile.Set(state.MailLabel, state.MailValue, state.MailHighlighted);

        for (int deck = 0; deck < _roomTiles.Length; deck++)
        {
            Color labelColor = state.Decks[deck].IsReached ? TrackerTheme.Accent : TrackerTheme.TextMuted;
            if (_deckLabels[deck].ForeColor != labelColor)
                _deckLabels[deck].ForeColor = labelColor;

            for (int room = 0; room < _roomTiles[deck].Length; room++)
            {
                _roomTiles[deck][room].State = state.Decks[deck].Rooms[room];
            }
        }

        _here.State = state.Here;

        bool layoutChanged = false;
        if (_hereNeededHeight != _here.NeededHeight)
        {
            _hereNeededHeight = _here.NeededHeight;
            layoutChanged = true;
        }
        if (_saveSeedBanner.Visible != state.SaveSeedMismatch)
        {
            _saveSeedBanner.Visible = state.SaveSeedMismatch;
            _forceSeedLink.Visible = state.SaveSeedMismatch;
            layoutChanged = true;
        }
        bool showParrot = state.Goal is GoalState.Parrot;
        if (_showParrot != showParrot)
        {
            _showParrot = showParrot;
            layoutChanged = true;
        }
        ApplyGoal(state.Goal);
        if (layoutChanged)
            PerformLayout();
    }

    /// <summary>Moving between controls can bring the new one's hover before the old one's leave, so a control can only
    /// hide the popup while it's the one showing it.</summary>
    private void OnCheckHoverChanged(Control source, CheckPip? check, Rectangle bounds)
    {
        if (check is { } shown)
        {
            if (_hintBox.Visible)
                return;
            _popupSource = source;
            bounds.Offset(source.Location);
            _checkPopup.ShowFor(shown, bounds);
        }
        else if (_popupSource == source)
        {
            _popupSource = null;
            _checkPopup.Visible = false;
        }
    }

    /// <summary>Lists what's still missing for the goal while the mouse is over the tile.</summary>
    private void UpdateGoModePopup()
    {
        CheckPip? check = _goModeTile.IsHovered && _goModeLines.Length > 0
            ? new CheckPip("Go mode", "Go mode", CheckStatus.Locked, _goModeLines, Array.Empty<HintOption>(), null)
            : null;
        OnCheckHoverChanged(_goModeTile, check, new Rectangle(Point.Empty, _goModeTile.Size));
    }

    private void OnClueHoverChanged(ClueEntry? clue, Rectangle bounds)
    {
        if (clue is { } shown)
        {
            if (_hintBox.Visible)
                return;
            bounds.Offset(_clueBar.Location);
            _cluePopup.ShowFor(shown, _clueBar.Hint, bounds);
        }
        else
        {
            _cluePopup.Visible = false;
        }
    }

    /// <summary>The same rule as <see cref="OnCheckHoverChanged"/>: only the control showing the popup can hide it.</summary>
    private void OnHintHoverChanged(TrackerControl source, HintTarget? target, Rectangle bounds)
    {
        if (target is not null)
        {
            if (_hintBox.Visible)
                return;
            _hintPopupSource = source;
            bounds.Offset(source.Location);
            _hintPopup.ShowFor(target, bounds);
        }
        else if (_hintPopupSource == source)
        {
            _hintPopupSource = null;
            _hintPopup.Visible = false;
        }
    }

    private void OpenHintBox(HintOption[] options)
    {
        _popupSource = null;
        _hintPopupSource = null;
        _checkPopup.Visible = false;
        _cluePopup.Visible = false;
        _hintPopup.Visible = false;
        _hintBox.Open(options, _hintCost, _hintPoints, PointToClient(Cursor.Position));
    }

    public void CloseHintBox() => _hintBox.Close();

    /// <summary>Keeps an open box in step with what can still be requested. <paramref name="resolve"/> gives an option
    /// as it now stands, or null if it's no longer on offer.</summary>
    public void RefreshHintBox(Func<HintOption, HintOption?> resolve)
    {
        if (_hintBox.Visible)
            _hintBox.UpdateOptions(_hintBox.Options.Select(resolve).OfType<HintOption>().ToArray(), _hintCost, _hintPoints);
    }

    private void ApplyGoal(GoalState goal)
    {
        switch (goal)
        {
            case GoalState.Titania titania:
                _titaniaHeader.SetTitle(titania.Title);
                _titaniaHeader.SetLegend(titania.FromServer ? TitaniaServerLegend : titania.UsesMail ? TitaniaMailLegend : TitaniaDirectLegend);
                string total = $"{(titania.InHandCount?.ToString() ?? "-")}/{TitaniaParts.All.Length}";
                _partTiles[0].Set("PARTS", total, GoalTileStyle.Total, TrackerTheme.PartCountFont);
                for (int i = 0; i < TitaniaParts.All.Length; i++)
                {
                    (string mark, GoalTileStyle style) = titania.Parts[i] switch
                    {
                        PartState.InHand => (InHandMark, GoalTileStyle.Done),
                        PartState.InMail => (InMailMark, GoalTileStyle.Pending),
                        _ => (UnknownMark, GoalTileStyle.Unknown),
                    };
                    _partTiles[i + 1].Set(TitaniaParts.All[i].Label.ToUpperInvariant(), mark, style, TrackerTheme.MarkFont);
                    _partTiles[i + 1].SetHint(titania.PartHints[i]);
                }
                break;

            case GoalState.Parrot parrot:
                SetParrotTile(0, "ELEVATOR", parrot.Elevator);
                SetParrotTile(1, "FLOOR", parrot.Floor);
                SetParrotTile(2, "ROOM", parrot.Room);
                _clueBar.Set(parrot.CluesFound, parrot.CluesTotal, parrot.Clues, parrot.ClueHint);
                break;
        }
    }

    private void SetParrotTile(int index, string label, int? value) =>
        _parrotTiles[index].Set(label, value?.ToString() ?? UnknownMark, value is null ? GoalTileStyle.Unknown : GoalTileStyle.Done, TrackerTheme.ParrotValueFont);

    /// <summary>Appends one line built from differently colored pieces, trimming the oldest lines past the cap.</summary>
    public void AppendLog(IEnumerable<(string Text, Color Color)> parts, bool bold)
    {
        Font font = bold ? TrackerTheme.LogBoldFont : TrackerTheme.LogFont;
        foreach ((string text, Color color) in parts)
            AppendLogText(text, color, font);
        AppendLogText(Environment.NewLine, TrackerTheme.Text, font);

        // Cut the oldest lines in place rather than reassigning Lines, which would drop every line's colors
        const int maxLines = 2000;
        if (_log.Lines.Length > maxLines)
        {
            _log.ReadOnly = false; // a read-only RichTextBox ignores SelectedText edits
            _log.Select(0, _log.GetFirstCharIndexFromLine(maxLines / 2));
            _log.SelectedText = "";
            _log.ReadOnly = true;
        }

        // Scrolling without a size would leave only the last line showing, so that waits for the window to come back
        if (LogHasSize)
            ScrollLogToEnd();
        else
            _scrollLogWhenSized = true;
    }

    /// <summary>While the window's minimized the log has no size, and AppendText then puts the selection back where it
    /// was, so each piece has to move it to the end again to get its own format.</summary>
    private void AppendLogText(string text, Color color, Font font)
    {
        _log.Select(_log.TextLength, 0);
        _log.SelectionFont = font;
        _log.SelectionColor = color;
        _log.AppendText(text);
    }

    private bool LogHasSize => _log.Width > 0 && _log.Height > 0;

    private void ScrollLogToEnd()
    {
        _log.Select(_log.TextLength, 0);
        _log.ScrollToCaret();
    }

    public void AddRecent(IEnumerable<(string Text, Color Color)> parts, DateTime received) => _recent.Add(parts, received);

    public void ClearRecent() => _recent.Clear();

    private void SubmitChat()
    {
        string text = _chatInput.Text.Trim();
        if (text.Length > 0)
            ChatSubmitted?.Invoke(text);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);

        int width = ClientSize.Width, height = ClientSize.Height;
        int margin = S(12), gap = S(6);

        // Command bar: the connections on the left, the flavor on the right
        int barHeight = S(CommandBarHeight), tileTop = S(8), tileHeight = S(46);
        int statusWidth = S(StatusTileWidth);
        _gameTile.SetBounds(margin, tileTop, S(250), tileHeight);
        _mailTile.SetBounds(width - margin - statusWidth, tileTop, statusWidth, tileHeight);
        _goModeTile.SetBounds(_mailTile.Left - gap - statusWidth, tileTop, statusWidth, tileHeight);
        _inLogicTile.SetBounds(_goModeTile.Left - gap - statusWidth, tileTop, statusWidth, tileHeight);
        _hintsTile.SetBounds(_inLogicTile.Left - gap - statusWidth, tileTop, statusWidth, tileHeight);
        _apTile.SetBounds(_gameTile.Right + gap, tileTop, _hintsTile.Left - gap - (_gameTile.Right + gap), tileHeight);

        int top = barHeight;
        if (_saveSeedBanner.Visible)
        {
            _saveSeedBanner.SetBounds(0, top, width, S(BannerTextHeight));
            _forceSeedLink.SetBounds(0, _saveSeedBanner.Bottom, width, S(BannerHeight) - S(BannerTextHeight));
            top += S(BannerHeight);
        }

        LayoutRooms(top, width - S(GoalWidth), margin, gap);
        LayoutGoal(top, width - S(GoalWidth) + margin, width - margin, gap);

        // Log and chat on the left, You Are Here over Recent on the right. The log's padding is painted around it (see OnPaint)
        int bottomTop = top + S(TrackerHeight);
        int logWidth = width - S(GoalWidth);
        int chatTop = height - S(ChatRowHeight);
        _log.SetBounds(S(10), bottomTop + S(6), logWidth - S(20), chatTop - bottomTop - S(12));
        int sendWidth = S(64), rowHeight = S(24), rowTop = chatTop + (S(ChatRowHeight) - rowHeight) / 2;
        _chatSend.SetBounds(logWidth - S(4) - sendWidth, rowTop, sendWidth, rowHeight);
        _chatInput.SetBounds(S(4), rowTop + (rowHeight - _chatInput.Height) / 2, _chatSend.Left - S(8), _chatInput.Height);

        int recentLeft = logWidth + margin, recentWidth = S(GoalWidth) - margin * 2;
        _hereHeader.SetBounds(recentLeft, bottomTop + S(8), recentWidth, S(17));

        // You Are Here takes what it needs for its hints. Recent gives up its height first, then the panel clips its last rows
        int hereTop = _hereHeader.Bottom + S(6);
        int hereHeight = Math.Min(Math.Max(S(HerePanel.DefaultHeight), _here.NeededHeight), height - S(8) - hereTop);
        _here.SetBounds(recentLeft, hereTop, recentWidth, Math.Max(0, hereHeight));
        _recentHeader.SetBounds(recentLeft, _here.Bottom + S(8), recentWidth, S(17));
        int recentHeight = height - _recentHeader.Bottom - S(14);
        _recent.SetBounds(recentLeft, _recentHeader.Bottom + S(6), recentWidth, Math.Max(0, recentHeight));
        _recentHeader.Visible = _recent.Visible = recentHeight >= S(MinRecentHeight);

        Invalidate();
    }

    private void LayoutRooms(int top, int areaWidth, int margin, int gap)
    {
        _roomsHeader.SetBounds(margin, top + S(8), areaWidth - margin * 2, S(17));

        int labelWidth = S(50);
        int tilesLeft = margin + labelWidth + S(8);
        int tileWidth = (areaWidth - margin - tilesLeft - gap * 5) / 6;
        int tileHeight = S(52);
        int y = _roomsHeader.Bottom + gap;
        for (int deck = 0; deck < _roomTiles.Length; deck++)
        {
            _deckLabels[deck].SetBounds(margin, y + S(5), labelWidth, S(16));
            for (int room = 0; room < _roomTiles[deck].Length; room++)
                _roomTiles[deck][room].SetBounds(tilesLeft + room * (tileWidth + gap), y, tileWidth, tileHeight);
            y += tileHeight + gap;
        }

        RoomTile bridge = _roomTiles[0][^1];
        _roomsLegend.SetBounds(bridge.Right + gap, bridge.Top, areaWidth - margin - bridge.Right - gap, tileHeight);
    }

    private void LayoutGoal(int top, int left, int right, int gap)
    {
        int headerTop = top + S(8), headerHeight = S(17);
        int gridTop = headerTop + headerHeight + gap;
        int width = right - left;

        _titaniaHeader.Visible = !_showParrot;
        _titaniaHeader.SetBounds(left, headerTop, width, headerHeight);
        int cellWidth = (width - gap * 3) / 4, cellHeight = S(86);
        for (int i = 0; i < _partTiles.Length; i++)
        {
            _partTiles[i].Visible = !_showParrot;
            _partTiles[i].SetBounds(left + i % 4 * (cellWidth + gap), gridTop + i / 4 * (cellHeight + gap), cellWidth, cellHeight);
        }

        _parrotHeader.Visible = _showParrot;
        _parrotHeader.SetBounds(left, headerTop, width, headerHeight);
        int parrotWidth = (width - gap * 2) / 3, parrotHeight = S(150);
        for (int i = 0; i < _parrotTiles.Length; i++)
        {
            _parrotTiles[i].Visible = _showParrot;
            _parrotTiles[i].SetBounds(left + i * (parrotWidth + gap), gridTop, parrotWidth, parrotHeight);
        }
        _clueBar.Visible = _showParrot;
        _clueBar.SetBounds(left, gridTop + parrotHeight + gap, width, S(90));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        int width = ClientSize.Width, height = ClientSize.Height;
        int top = S(CommandBarHeight) + (_saveSeedBanner.Visible ? S(BannerHeight) : 0);
        int bottomTop = top + S(TrackerHeight);
        int goalLeft = width - S(GoalWidth);
        int chatTop = height - S(ChatRowHeight);

        using (var logBrush = new SolidBrush(TrackerTheme.LogBackground))
            g.FillRectangle(logBrush, 0, bottomTop, goalLeft, chatTop - bottomTop);

        using var line = new Pen(TrackerTheme.Line);
        g.DrawLine(line, 0, S(CommandBarHeight) - 1, width, S(CommandBarHeight) - 1);
        g.DrawLine(line, 0, bottomTop - 1, width, bottomTop - 1);
        g.DrawLine(line, 0, chatTop, goalLeft, chatTop);
        g.DrawLine(line, goalLeft, top, goalLeft, height);
    }
}
