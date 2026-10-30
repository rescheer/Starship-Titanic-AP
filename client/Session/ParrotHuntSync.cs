namespace StarshipTitanicAp;

/// <summary>Runs the Parrot Hunt goal in game: takes the parrot out of play, keeps it parked in the hidden room except
/// while the player is in the target stateroom (where it waits in the mail for that room's Succ-U-Bus), announces
/// clues as Progressive Clues arrive, and reports the goal once the parrot is released there.
/// Progress is persisted on CarryParrot itself: Stage None until it's been taken out of play, then Hidden while parked
/// or Mail while in transit to the target, and CheckFired once it's been released.</summary>
internal sealed class ParrotHuntSync
{
    private const string ParrotLobbyRoomName = "ParrotLobby";
    private const string PerchedParrotName = "PerchedParrot";
    private const string ParrotLobbyControllerName = "ParrotLobbyController";
    private const string HiddenRoomName = "HiddenRoom";

    /// <summary>How far up from CarryParrot's parent to look for the stateroom it was released into. A release moves it
    /// under the tray's view first, then under the room, so a poll can land in between.</summary>
    private const int MaxReleaseAncestorDepth = 4;

    private readonly SessionContext _ctx;

    private long? _carryParrotAddr;

    /// <summary>Whether the goal has been sent since the last resync.</summary>
    private bool _goalSent;

    private bool _takeOutFailureReported;

    public ParrotHuntSync(SessionContext ctx)
    {
        _ctx = ctx;
    }

    /// <summary>Forgets the cached CarryParrot address. Call when the game tree is rebuilt.</summary>
    public void Reset()
    {
        _carryParrotAddr = null;
        _takeOutFailureReported = false;
        ResyncFromItems();
    }

    /// <summary>Resends the goal on the next sync if it's been reached, e.g. after reconnecting.</summary>
    public void ResyncFromItems() => _goalSent = false;

    public void Sync()
    {
        if (ParrotHuntTracker.TryRead(_ctx.Ap.KnownSlotData) is not { } info)
            return;
        if (_ctx.Project is not { } project || _ctx.InventoryRoom is not { } petControlAddr || _ctx.MailManRoom is not { } mailManAddr)
            return;
        if (ResolveCarryParrot(project) is not { } parrot)
            return;

        AnnounceClues(info, parrot, petControlAddr);

        ItemPersistedState persisted = GameActions.ReadItemPersistedState(_ctx.Mem, parrot);
        if (persisted.CheckFired)
        {
            SendGoal();
            return;
        }

        if (persisted.Stage == ItemStage.None)
        {
            // Throttled: each attempt walks the Parrot Lobby
            if (_ctx.TickCount % ClientSession.InventoryIntervalTicks == 0)
                TakeParrotOutOfPlay(project, parrot);
            return;
        }

        if (_ctx.Mem.ReadInt64(parrot + GameOffsets.Parent) is not { } parent)
            return;
        bool inMail = parent == mailManAddr;
        bool parked = !inMail && GameState.TryReadName(_ctx.Mem, parent) == HiddenRoomName;
        bool inTarget = IsPlayerInTargetRoom(info, petControlAddr);

        // Out of the mail since it was put there: taken from the Succ-U-Bus tray, which releases it into the room.
        if (persisted.Stage == ItemStage.Mail && !inMail && !parked)
        {
            if (inTarget && IsUnderRoom(parent, info.TargetRoomName))
            {
                GameActions.WriteItemPersistedState(_ctx.Mem, parrot, new ItemPersistedState(ItemStage.None, true, ItemPulledFrom.None));
                _ctx.Report.AppendServerLog("CLIENT: The parrot has been released - goal complete!", bold: true);
                _ctx.ShowPetMessage("AP: The parrot has been released - goal complete!");
                SendGoal();
                return;
            }

            // Parking should keep any other Succ-U-Bus from handing it over, but if one did, it goes back in the mail.
            _ctx.Report.AppendLog($"Parrot Hunt: CarryParrot left the mail outside the target stateroom (parent 0x{parent:X}) - parking it again");
        }

        // A release elsewhere leaves it escaped; it has to be mailed again to be released from the target's tray.
        if (GameActions.ReadParrotState(_ctx.Mem) is { } parrotState && parrotState != GameOffsets.ParrotStateMailed)
            GameActions.SetParrotState(_ctx.Mem, GameOffsets.ParrotStateMailed);

        if (inTarget)
        {
            if (!inMail)
                SendToTarget(info, parrot, mailManAddr, petControlAddr);
        }
        else if (!parked)
        {
            Park(parrot, wasInMail: inMail);
        }
    }

    private long? ResolveCarryParrot(long project)
    {
        if (_carryParrotAddr is not null)
            return _carryParrotAddr;
        // Walks the whole tree, so only once a second until found
        if (_ctx.TickCount % ClientSession.InventoryIntervalTicks != 0)
            return null;

        _carryParrotAddr = GameState.FindAllCarryItems(_ctx.Mem, project)
            .Where(item => string.Equals(item.Name, ItemTracking.CarryParrotName, StringComparison.OrdinalIgnoreCase))
            .Select(item => (long?)item.Address)
            .FirstOrDefault();
        return _carryParrotAddr;
    }

    /// <summary>Whether the player is standing in the target stateroom. Every stateroom of a class is the same room
    /// object, so it's told apart by the PET's room flags, compared the way the stateroom's Succ-U-Bus compares them.</summary>
    private bool IsPlayerInTargetRoom(ParrotHuntInfo info, long petControlAddr) =>
        string.Equals(_ctx.CurrentRoomName, info.TargetRoomName, StringComparison.OrdinalIgnoreCase)
        && GameState.ReadCurrentRoomFlags(_ctx.Mem, petControlAddr) is { } roomFlags
        && RoomFlags.CompareLocation(roomFlags, info.TargetRoomFlags);

    /// <summary>Whether <paramref name="nodeAddr"/> is the named room or sits somewhere under it.</summary>
    private bool IsUnderRoom(long nodeAddr, string roomName)
    {
        long current = nodeAddr;
        for (int depth = 0; depth <= MaxReleaseAncestorDepth && GameState.LooksLikeHeapPointer(current); depth++)
        {
            if (string.Equals(GameState.TryReadName(_ctx.Mem, current), roomName, StringComparison.OrdinalIgnoreCase))
                return true;
            if (_ctx.Mem.ReadInt64(current + GameOffsets.Parent) is not { } parent)
                return false;
            current = parent;
        }
        return false;
    }

    /// <summary>Does what vanilla does when the parrot is mailed away from the Parrot Lobby, then parks it in the
    /// hidden room.</summary>
    private void TakeParrotOutOfPlay(long project, long parrot)
    {
        Dictionary<string, long>? found = GameState.FindRoomByName(_ctx.Mem, project, ParrotLobbyRoomName) is { } lobby
            ? GameState.FindDescendantsByName(_ctx.Mem, lobby, new[] { PerchedParrotName, ParrotLobbyControllerName })
            : null;
        if (found is null
            || !found.TryGetValue(PerchedParrotName, out long perchedParrot)
            || !found.TryGetValue(ParrotLobbyControllerName, out long controller))
        {
            if (!_takeOutFailureReported)
            {
                _takeOutFailureReported = true;
                _ctx.Report.ReportAction(false, "Parrot Hunt: couldn't find the Parrot Lobby's parrot objects - will keep retrying");
            }
            return;
        }

        bool ok = GameActions.SetParrotState(_ctx.Mem, GameOffsets.ParrotStateMailed)
            && GameActions.SetItemVisible(_ctx.Mem, perchedParrot, false)
            && GameActions.SendParrotLobbyAction(_ctx.Mem, controller, "LoseParrot")
            && GameActions.MoveItemToHiddenRoom(_ctx.Mem, parrot);
        if (ok)
        {
            GameActions.WriteItemPersistedState(_ctx.Mem, parrot, new ItemPersistedState(ItemStage.Hidden, false, ItemPulledFrom.None));
            _ctx.Report.NotifyItemsChanged();
        }
        _ctx.Report.ReportAction(ok, ok
            ? "Parrot Hunt: the parrot has been mailed away from the Parrot Lobby"
            : "Parrot Hunt: failed to take the parrot out of the Parrot Lobby - will retry");
    }

    /// <summary>Puts CarryParrot in the mail, in transit to the target stateroom, for its Succ-U-Bus to receive. Never
    /// marked tool-placed, or DeliverQueuedMailAtStation would readdress it to wherever the player is.</summary>
    private void SendToTarget(ParrotHuntInfo info, long parrot, long mailManAddr, long petControlAddr)
    {
        // Addressed before it's moved, so a Receive pressed in between can't find it with stale flags
        bool ok = GameActions.SetItemMailDestination(_ctx.Mem, parrot, info.TargetRoomFlags)
            && _ctx.GameManager is { } gameManager
            && GameActions.MoveItemSmart(_ctx.Mem, parrot, mailManAddr, petControlAddr, gameManager);
        if (ok)
        {
            GameActions.WriteItemPersistedState(_ctx.Mem, parrot, new ItemPersistedState(ItemStage.Mail, false, ItemPulledFrom.None));
            _ctx.Report.NotifyMailChanged();
            _ctx.Report.NotifyItemsChanged();
        }
        _ctx.Report.ReportAction(ok, ok
            ? "Parrot Hunt: in the target stateroom - the parrot is waiting at its Succ-U-Bus"
            : "Parrot Hunt: in the target stateroom, but failed to put the parrot in the mail");
    }

    /// <summary>Moves CarryParrot back to the hidden room, out of reach of every Succ-U-Bus. One left in the target's
    /// tray goes back in transit when the player returns, so it has to be received again.</summary>
    private void Park(long parrot, bool wasInMail)
    {
        bool ok = GameActions.MoveItemToHiddenRoom(_ctx.Mem, parrot);
        if (ok)
        {
            GameActions.WriteItemPersistedState(_ctx.Mem, parrot, new ItemPersistedState(ItemStage.Hidden, false, ItemPulledFrom.None));
            if (wasInMail)
                _ctx.Report.NotifyMailChanged();
            _ctx.Report.NotifyItemsChanged();
        }
        _ctx.Report.ReportAction(ok, ok ? "Parrot Hunt: parked the parrot in the hidden room" : "Parrot Hunt: failed to park the parrot");
    }

    /// <summary>Shows a PET message for the Progressive Clues not announced yet: the real clue at each of the seed's
    /// thresholds, or a red herring when none of them hit one. The count announced is saved on CarryParrot, so
    /// reconnecting (which re-receives every item) or reloading doesn't repeat them. Waits while disconnected, since
    /// the lines chosen depend on the seed (see <see cref="ParrotHuntClueText"/>). Swaps the PET to the conversation
    /// tab first, since a message shown on any other tab can be overwritten before it's read.</summary>
    private void AnnounceClues(ParrotHuntInfo info, long parrot, long petControlAddr)
    {
        int clues = info.CountClues(_ctx.Ap.GetReceivedItemNames());
        int announced = GameActions.ReadCluesAnnounced(_ctx.Mem, parrot);
        if (clues <= announced || _ctx.Ap.SeedName is not { } seedName || _ctx.Ap.SlotName is not { } slotName)
            return;
        if (!GameActions.WriteCluesAnnounced(_ctx.Mem, parrot, clues))
            return;

        var text = new ParrotHuntClueText(info, seedName, slotName);
        List<string> revealed = Enumerable.Range(announced + 1, clues - announced)
            .Select(info.PartRevealedAt)
            .OfType<string>()
            .ToList();
        string message = revealed.Count > 0
            ? string.Join(" ", revealed.Select(text.Clue))
            : text.RedHerring(clues);

        _ctx.Report.AppendServerLog($"CLIENT: {message}");
        _ctx.Report.AppendServerLog($"CLIENT: Parrot Hunt clues: {info.DescribeProgress(clues)}");
        if (GameState.GetCurrentPetArea(_ctx.Mem, petControlAddr) != GameOffsets.PetAreaConversation)
            GameActions.SetPetAreaConversation(_ctx.Mem, petControlAddr);
        _ctx.ShowPetMessage($"AP: {message}");
    }

    private void SendGoal()
    {
        if (_goalSent)
            return;

        _goalSent = true;
        bool handedOff = _ctx.Ap.SendLocationCheck(LocationChecks.GoalLocationName);
        _ctx.Report.ReportAction(handedOff, handedOff
            ? "Goal complete: parrot released"
            : "Goal complete: parrot released (queued offline)");
    }
}
