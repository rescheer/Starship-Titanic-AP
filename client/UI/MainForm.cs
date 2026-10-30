using Archipelago.MultiClient.Net.Colors;
using Archipelago.MultiClient.Net.MessageLog.Parts;

namespace StarshipTitanicAp;

/// <summary>Main application window: the player-facing view of a <see cref="ClientSession"/>.</summary>
public sealed class MainForm : Form
{
    private readonly ClientSession _session;
    private readonly TrackerView _view = new() { Dock = DockStyle.Fill };

    private int _mailCount;

    // Each room's location names in this slot, and the seed's logic, rebuilt on each connection state change
    private readonly Dictionary<RoomDefinition, string[]> _roomLocationNames = new();
    private SlotLogic? _logic;
    private SlotLogic.Evaluation? _evaluation;
    private int _evaluatedItemCount = -1;

    public MainForm()
    {
        _session = new ClientSession();
        Text = AppInfo.TitleBarText;
        ClientSize = new Size(LogicalToDeviceUnits(1200), LogicalToDeviceUnits(750));
        MinimumSize = new Size(LogicalToDeviceUnits(1120), LogicalToDeviceUnits(640));
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = TrackerTheme.Background;
        Controls.Add(_view);

        RefreshView();

        _session.ServerLog += AppendServerLog;
        _session.ServerLogMessage += parts => _view.AppendLog(LogParts(parts), bold: false);
        _session.ActivePlayerMessage += parts => _view.AddRecent(LogParts(parts), DateTime.Now);
        _session.AttachChanged += RefreshView;
        _session.ApStateChanged += (state, _) =>
        {
            if (state == ApConnectionState.Connecting)
                _view.ClearRecent();
            RebuildSlotData();
            RefreshView();
        };
        _session.ItemReceived += RefreshView;
        _session.CheckedLocationsChanged += RefreshView;
        _session.PendingChecksChanged += RefreshView; // every tick while playing, and picks up the Titania repair check
        _session.TitaniaPartsChanged += RefreshView;
        _session.PassengerClassChanged += _ => RefreshView();
        _session.MailChanged += items =>
        {
            _mailCount = items?.Count(i => i.ToolPlaced) ?? 0;
            RefreshView();
        };
        _session.RoomNodeViewChanged += (_, _) => RefreshView();
        _session.Ticked += RefreshView;

        _view.AttachButtonClicked += DoToggleAttach;
        _view.ApButtonClicked += DoToggleApConnection;
        _view.ChatSubmitted += DoSendChatMessage;

        _session.Start();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        TrackerTheme.UseDarkTitleBar(this);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _session.Dispose();
        base.OnFormClosed(e);
    }

    private void DoToggleAttach()
    {
        if (!_session.Memory.IsAttached)
        {
            _session.Attach();
            return;
        }

        _session.Detach();
        AppendServerLog("CLIENT: Detached - room and item locks are off until you attach again", bold: false);
    }

    private void DoToggleApConnection()
    {
        if (_session.Ap.State is ApConnectionState.Connected or ApConnectionState.Connecting)
        {
            _session.Ap.Disconnect();
            return;
        }

        ConnectionSettings.Data connection = _session.Connection;
        using var dlg = new ConnectDialog(connection.Server, connection.Slot, connection.Password);
        if (dlg.ShowDialog(this) == DialogResult.OK)
            _session.Connect(dlg.Server, dlg.Slot, dlg.Password);
    }

    private void DoSendChatMessage(string text)
    {
        bool ok = _session.SubmitChat(text);
        if (ok)
            _view.ClearChatInput();
        else
            AppendServerLog("CLIENT: Not connected - message not sent", bold: false);
    }

    private void AppendServerLog(string line, bool bold) => _view.AppendLog(new[] { (line, TrackerTheme.Text) }, bold);

    private static IEnumerable<(string Text, Color Color)> LogParts(MessagePart[] parts) =>
        parts.Select(part => (part.Text, LogColor(part)));

    /// <summary>A message part's color, in the standard AP client's colors (Archipelago's data/client.kv). The library's
    /// own palette differs from them, its green most of all. Plain text stays the normal text color.</summary>
    private static Color LogColor(MessagePart part) => part.IsBackgroundColor ? TrackerTheme.Text : part.PaletteColor switch
    {
        PaletteColor.Black => ColorTranslator.FromHtml("#000000"),
        PaletteColor.Red => ColorTranslator.FromHtml("#EE0000"),
        PaletteColor.Green => ColorTranslator.FromHtml("#00FF7F"),
        PaletteColor.Yellow => ColorTranslator.FromHtml("#FAFAD2"),
        PaletteColor.Blue => ColorTranslator.FromHtml("#6495ED"),
        PaletteColor.Magenta => ColorTranslator.FromHtml("#EE00EE"),
        PaletteColor.Cyan => ColorTranslator.FromHtml("#00EEEE"),
        PaletteColor.SlateBlue => ColorTranslator.FromHtml("#6D8BE8"),
        PaletteColor.Plum => ColorTranslator.FromHtml("#AF99EF"),
        PaletteColor.Salmon => ColorTranslator.FromHtml("#FA8072"),
        _ => TrackerTheme.Text, // White or unspecified - plain text
    };

    /// <summary>Sorts the slot's location names under their rooms by name prefix, and reads the seed's logic. Empty
    /// while not connected.</summary>
    private void RebuildSlotData()
    {
        _logic = SlotLogic.TryRead(_session.Ap.SlotData);
        _evaluatedItemCount = -1;
        _roomLocationNames.Clear();

        string[] allLocations = _session.Ap.GetAllLocationNames();
        foreach (RoomDefinition room in RoomTiles.Decks.SelectMany(deck => deck.Rooms))
        {
            _roomLocationNames[room] = allLocations
                .Where(location => room.LocationPrefixes.Any(prefix => location.StartsWith(prefix + " - ", StringComparison.Ordinal)))
                .ToArray();
        }
    }

    /// <summary>Works out what the window should show and passes it to the view, which skips anything unchanged - this
    /// runs on every tick while playing.</summary>
    private void RefreshView()
    {
        bool attached = _session.Memory.IsAttached;
        bool connected = _session.Ap.State == ApConnectionState.Connected;
        string[] receivedItems = _session.Ap.GetReceivedItemNames();
        int? passengerClass = CurrentPassengerClass(connected, receivedItems);
        bool titaniaRepaired = connected && _session.Ap.IsLocationCheckedOrQueued(ClassSync.TitaniaRepairedLocation);

        if (connected && _logic is not null && _evaluatedItemCount != receivedItems.Length)
        {
            _evaluation = _logic.Evaluate(receivedItems);
            _evaluatedItemCount = receivedItems.Length;
        }

        (string apValue, Color apColor, bool apError) = _session.Ap.State switch
        {
            ApConnectionState.Connected => ($"✔ {_session.Ap.SlotName} @ {_session.Connection.Server}", TrackerTheme.Text, false),
            ApConnectionState.Connecting => ("Connecting...", TrackerTheme.Accent, false),
            _ => ("✖ Not connected", TrackerTheme.Error, true),
        };
        bool directToInventory = ItemSync.IsDirectToInventory(_session.Ap.KnownSlotData);
        int mailCount = attached ? _mailCount : 0;

        _view.Apply(new TrackerState(
            attached ? $"✔ Attached (PID {_session.Memory.ProcessId})" : "✖ Not attached",
            !attached,
            attached ? "Detach" : "Attach",
            apValue,
            apColor,
            apError,
            _session.Ap.State is ApConnectionState.Connected or ApConnectionState.Connecting ? "Disconnect" : "Connect to AP...",
            _session.Ap.State != ApConnectionState.Connecting,
            directToInventory ? "Direct" : $"{mailCount} waiting",
            !directToInventory && mailCount > 0,
            PassengerClassLabel(passengerClass),
            _session.Options.EnforceSaveSeedGuard && _session.SaveSeedGuardState == SaveSeedGuardState.Blocked,
            RoomTiles.Decks.Select(deck => BuildDeck(deck, connected, receivedItems, passengerClass, titaniaRepaired)).ToArray(),
            BuildGoal(attached, connected, receivedItems)));
    }

    /// <summary>The player's class: what AP says while connected (the game's own value can be spoofed for a room), or
    /// else what the game says, while attached. Null if neither knows.</summary>
    private int? CurrentPassengerClass(bool connected, string[] receivedItems)
    {
        if (connected && _session.Ap.SlotData is { } slotData)
        {
            int? fromAp = ClassUpgradeTracker.IsVanilla(slotData) ? ClassFromUpgradeChecks() : ClassUpgradeTracker.ComputeClass(receivedItems, slotData);
            return fromAp ?? (int)PassengerClass.Third;
        }
        return _session.GamePassengerClass is (int)PassengerClass.First or (int)PassengerClass.Second or (int)PassengerClass.Third
            ? _session.GamePassengerClass
            : null;
    }

    /// <summary>The class reached through the DeskBot's own upgrades, for seeds with vanilla class upgrades.</summary>
    private int? ClassFromUpgradeChecks()
    {
        foreach (PassengerClass passengerClass in new[] { PassengerClass.First, PassengerClass.Second })
        {
            if (LocationChecks.TryGetClassUpgradeLocationName((int)passengerClass, out string locationName)
                && _session.Ap.IsLocationCheckedOrQueued(locationName))
                return (int)passengerClass;
        }
        return null;
    }

    private static string PassengerClassLabel(int? passengerClass) => passengerClass switch
    {
        (int)PassengerClass.First => "1st Class",
        (int)PassengerClass.Second => "2nd Class",
        (int)PassengerClass.Third => "SGT Class",
        _ => "-",
    };

    private DeckState BuildDeck(DeckDefinition deck, bool connected, string[] receivedItems, int? passengerClass, bool titaniaRepaired)
    {
        bool isReached = deck.Label switch
        {
            "BRIDGE" => titaniaRepaired,
            "1ST" => passengerClass is (int)PassengerClass.First,
            "2ND" => passengerClass is (int)PassengerClass.First or (int)PassengerClass.Second,
            _ => passengerClass is not null,
        };
        return new DeckState(isReached, deck.Rooms.Select(room => BuildRoom(room, connected, receivedItems, passengerClass, titaniaRepaired)).ToArray());
    }

    /// <summary>A room's dots and tag. Just its name while not connected, since nothing about the seed is known.</summary>
    private RoomTileState BuildRoom(RoomDefinition room, bool connected, string[] receivedItems, int? passengerClass, bool titaniaRepaired)
    {
        bool isHere = room.LocationPrefixes.Contains(CurrentRoomLocationPrefix(), StringComparer.Ordinal);
        if (!connected || !_roomLocationNames.TryGetValue(room, out string[]? locations))
            return new RoomTileState(room.Name, Array.Empty<CheckPip>(), "", false, isHere);

        CheckPip[] checks = locations.Select(BuildCheck).OrderBy(check => check.Status).ToArray();
        int total = checks.Length;
        int checkedCount = checks.Count(check => check.Status == CheckStatus.Checked);
        int inLogic = checks.Count(check => check.Status == CheckStatus.InLogic);

        HashSet<string> requiredCards = RoomKeys.GetRequiredCards(_session.Ap.SlotData);
        bool hasCard = room.Card is null || !requiredCards.Contains(room.Card) || receivedItems.Contains(room.Card, StringComparer.OrdinalIgnoreCase);
        bool classMet = room.Requirement switch
        {
            RoomRequirement.SecondClass => passengerClass is (int)PassengerClass.First or (int)PassengerClass.Second,
            RoomRequirement.FirstClass => passengerClass is (int)PassengerClass.First,
            _ => true,
        };
        string classNeeded = room.Requirement == RoomRequirement.FirstClass ? "1st Class" : "2nd Class";

        string? lockReason = true switch
        {
            _ when room.Requirement == RoomRequirement.TitaniaRepaired && !titaniaRepaired => "Needs Titania",
            _ when !hasCard && !classMet => $"Card + {classNeeded}",
            _ when !hasCard => "Needs card",
            _ when !classMet => $"Needs {classNeeded}",
            _ => null,
        };

        string tag = true switch
        {
            _ when total == 0 => "No checks",
            _ when checkedCount == total => "Complete",
            _ when lockReason is not null => lockReason,
            _ when _logic is null => $"{checkedCount}/{total}", // an older seed, without logic to go by
            _ => $"{inLogic} in logic",
        };
        return new RoomTileState(room.Name, checks, tag, lockReason is not null, isHere);
    }

    /// <summary>A check's dot. Every unchecked check is locked on an older seed, without logic to go by.</summary>
    private CheckPip BuildCheck(string location)
    {
        if (_session.Ap.IsLocationChecked(location))
            return new CheckPip(location, CheckStatus.Checked, Array.Empty<string>());
        if (_logic is null || _evaluation is null)
            return new CheckPip(location, CheckStatus.Locked, Array.Empty<string>());
        if (_evaluation.Reachable.Contains(location))
            return new CheckPip(location, CheckStatus.InLogic, Array.Empty<string>());
        return new CheckPip(location, CheckStatus.Locked, _evaluation.MissingItems.GetValueOrDefault(location, Array.Empty<string>()));
    }

    /// <summary>The location-name prefix of the room the player's in, or "" while that's not known.</summary>
    private string CurrentRoomLocationPrefix() =>
        _session.CurrentRoomName is { } roomName && _session.Memory.IsAttached ? LocationChecks.GetReadableRoomName(roomName) : "";

    private GoalState BuildGoal(bool attached, bool connected, string[] receivedItems)
    {
        if (ParrotHuntTracker.TryRead(_session.Ap.KnownSlotData) is { } info)
        {
            int clues = info.CountClues(receivedItems);
            return new GoalState.Parrot(
                info.IsRevealed("elevator", clues) ? info.Elevator : null,
                info.IsRevealed("floor", clues) ? info.Floor : null,
                info.IsRevealed("room", clues) ? info.Room : null,
                clues,
                info.TotalClues);
        }

        // In hand can only be read from the game, so until attached a received part stays "in the mail", or under
        // Direct To Inventory, which has no mail to wait in, not received
        bool usesMail = !ItemSync.IsDirectToInventory(_session.Ap.KnownSlotData);
        PartState[] parts = TitaniaParts.All.Select(part => true switch
        {
            _ when !connected || !receivedItems.Contains(part.ApItemName, StringComparer.OrdinalIgnoreCase) => PartState.NotReceived,
            _ when _session.TitaniaPartsInHand.Contains(part.ApItemName) => PartState.InHand,
            _ => usesMail ? PartState.InMail : PartState.NotReceived,
        }).ToArray();
        return new GoalState.Titania(
            parts,
            connected && attached ? parts.Count(p => p == PartState.InHand) : null,
            usesMail);
    }
}
