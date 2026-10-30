namespace StarshipTitanicAp;

/// <summary>One deck row's state: whether its label is lit, and each of its rooms in <see cref="RoomTiles"/> order.</summary>
internal sealed record DeckState(bool IsReached, RoomTileState[] Rooms);

/// <summary>One Titania part's state, in <see cref="TitaniaParts"/> order.</summary>
internal enum PartState { NotReceived, InMail, InHand }

/// <summary>The goal rectangles: Titania's parts or, under Parrot Hunt, the parrot's address.</summary>
internal abstract record GoalState
{
    /// <param name="InHandCount">Null while it can't be known.</param>
    /// <param name="UsesMail">False under Direct To Inventory, which has no use for the "in the mail" legend entry.</param>
    public sealed record Titania(PartState[] Parts, int? InHandCount, bool UsesMail) : GoalState;

    /// <param name="Elevator">Each part of the address is null until revealed.</param>
    public sealed record Parrot(int? Elevator, int? Floor, int? Room, int CluesFound, int CluesTotal) : GoalState;
}

/// <summary>Everything the main window shows, besides the log and Recent lines.</summary>
internal sealed record TrackerState(
    string GameValue, bool GameHasError, string GameButton,
    string ApValue, Color ApValueColor, bool ApHasError, string ApButton, bool ApButtonEnabled,
    string MailLabel, string MailValue, bool MailHighlighted,
    string ClassValue,
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
    private const int TrackerHeight = 330;
    private const int GoalWidth = 420;
    private const int ChatRowHeight = 33;
    private const int HerePanelHeight = 106; // the room's name and four rows of checks, enough for the Restaurant's seven

    private const string InMailMark = "✉";
    private const string InHandMark = "✔";
    private const string UnknownMark = "?";

    public event Action? AttachButtonClicked;
    public event Action? ApButtonClicked;
    public event Action<string>? ChatSubmitted;
    public event Action? ForceSeedClicked;

    private readonly ConnectionTile _gameTile = new("STARSHIP TITANIC");
    private readonly ConnectionTile _apTile = new("ARCHIPELAGO");
    private readonly StatusTile _mailTile = new();
    private readonly StatusTile _classTile = new();

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

    private readonly SectionHeader _roomsHeader = new("ROOMS",
        ("Checked", TrackerTheme.Accent, true),
        ("In logic", TrackerTheme.Accent, false),
        ("Locked", TrackerTheme.PipLocked, false));
    private readonly Label[] _deckLabels;
    private readonly RoomTile[][] _roomTiles;
    private readonly CheckPopup _checkPopup = new();
    private Control? _popupSource;

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

    private readonly SectionHeader _titaniaHeader = new("TITANIA", TitaniaMailLegend);
    private readonly GoalTile[] _partTiles; // the parts total first, then each part
    private readonly SectionHeader _parrotHeader = new("THE PARROT");
    private readonly GoalTile[] _parrotTiles = { new(), new(), new() };
    private readonly ClueBar _clueBar = new();

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
        ("In logic", TrackerTheme.Accent, false),
        ("Locked", TrackerTheme.PipLocked, false));
    private readonly HerePanel _here = new();
    private readonly SectionHeader _recentHeader = new("RECENT");
    private readonly RecentPanel _recent = new();

    private bool _showParrot;

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

        Controls.Add(_checkPopup); // added first, so it's on top of everything else
        Controls.AddRange(new Control[] { _gameTile, _apTile, _mailTile, _classTile, _saveSeedBanner, _forceSeedLink, _roomsHeader, _titaniaHeader, _parrotHeader, _clueBar, _log, _chatInput, _chatSend, _hereHeader, _here, _recentHeader, _recent });
        Controls.AddRange(_deckLabels);
        Controls.AddRange(_roomTiles.SelectMany(row => row).ToArray<Control>());
        Controls.AddRange(_partTiles.ToArray<Control>());
        Controls.AddRange(_parrotTiles.ToArray<Control>());

        foreach (RoomTile tile in _roomTiles.SelectMany(row => row))
            tile.PipHoverChanged += OnCheckHoverChanged;
        _here.CheckHoverChanged += OnCheckHoverChanged;
        _gameTile.Button.Click += (_, _) => AttachButtonClicked?.Invoke();
        _apTile.Button.Click += (_, _) => ApButtonClicked?.Invoke();
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
    }

    public void ClearChatInput() => _chatInput.Clear();

    public void Apply(TrackerState state)
    {
        _gameTile.Set(state.GameValue, state.GameHasError ? TrackerTheme.Error : TrackerTheme.Text, state.GameHasError, state.GameButton, buttonEnabled: true);
        _apTile.Set(state.ApValue, state.ApValueColor, state.ApHasError, state.ApButton, state.ApButtonEnabled);
        _mailTile.Set(state.MailLabel, state.MailValue, state.MailHighlighted);
        _classTile.Set("PASSENGER CLASS", state.ClassValue, highlighted: false);

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

    private void ApplyGoal(GoalState goal)
    {
        switch (goal)
        {
            case GoalState.Titania titania:
                _titaniaHeader.SetLegend(titania.UsesMail ? TitaniaMailLegend : TitaniaDirectLegend);
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
                }
                break;

            case GoalState.Parrot parrot:
                SetParrotTile(0, "ELEVATOR", parrot.Elevator);
                SetParrotTile(1, "FLOOR", parrot.Floor);
                SetParrotTile(2, "ROOM", parrot.Room);
                _clueBar.Set(parrot.CluesFound, parrot.CluesTotal);
                break;
        }
    }

    private void SetParrotTile(int index, string label, int? value) =>
        _parrotTiles[index].Set(label, value?.ToString() ?? UnknownMark, value is null ? GoalTileStyle.Unknown : GoalTileStyle.Done, TrackerTheme.ParrotValueFont);

    /// <summary>Appends one line built from differently colored pieces, trimming the oldest lines past the cap.</summary>
    public void AppendLog(IEnumerable<(string Text, Color Color)> parts, bool bold)
    {
        _log.SelectionStart = _log.TextLength;
        _log.SelectionLength = 0;
        _log.SelectionFont = bold ? TrackerTheme.LogBoldFont : TrackerTheme.LogFont;
        foreach ((string text, Color color) in parts)
        {
            _log.SelectionColor = color;
            _log.AppendText(text);
        }
        _log.SelectionColor = TrackerTheme.Text;
        _log.AppendText(Environment.NewLine);

        // Cut the oldest lines in place rather than reassigning Lines, which would drop every line's colors
        const int maxLines = 2000;
        if (_log.Lines.Length > maxLines)
        {
            _log.ReadOnly = false; // a read-only RichTextBox ignores SelectedText edits
            _log.Select(0, _log.GetFirstCharIndexFromLine(maxLines / 2));
            _log.SelectedText = "";
            _log.ReadOnly = true;
            _log.SelectionStart = _log.TextLength;
        }

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
        _gameTile.SetBounds(margin, tileTop, S(250), tileHeight);
        _apTile.SetBounds(_gameTile.Right + gap, tileTop, S(370), tileHeight);
        _classTile.SetBounds(width - margin - S(136), tileTop, S(136), tileHeight);
        _mailTile.SetBounds(_classTile.Left - gap - S(136), tileTop, S(136), tileHeight);

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
        _here.SetBounds(recentLeft, _hereHeader.Bottom + S(6), recentWidth, S(HerePanelHeight));
        _recentHeader.SetBounds(recentLeft, _here.Bottom + S(8), recentWidth, S(17));
        _recent.SetBounds(recentLeft, _recentHeader.Bottom + S(6), recentWidth, height - _recentHeader.Bottom - S(14));

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
