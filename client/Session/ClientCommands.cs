namespace StarshipTitanicAp;

/// <summary>"/" commands the client handles itself, typed in game (via the PET, see <see cref="TextCommandHook"/>) or
/// in the client's chat box. Callers decide what happens to anything else: the chat box sends it to the AP server
/// unchanged, while in game it's rejected (<see cref="ReportUnknown"/>) and server commands go through /ap.</summary>
internal sealed class ClientCommands
{
    private readonly SessionContext _ctx;
    private readonly SaveSeedGuard _saveSeedGuard;
    private readonly ItemSync _itemSync;

    /// <summary>A client command: its handler, which receives the rest of the line as its arguments, plus the
    /// usage line and description /help shows for it.</summary>
    private sealed record Command(string Usage, string Description, Action<string> Handler);

    /// <summary>Command name (without the "/") to command, in the order /help lists them.</summary>
    private readonly Dictionary<string, Command> _commands;

    public ClientCommands(SessionContext ctx, SaveSeedGuard saveSeedGuard, ItemSync itemSync)
    {
        _ctx = ctx;
        _saveSeedGuard = saveSeedGuard;
        _itemSync = itemSync;
        _commands = new(StringComparer.OrdinalIgnoreCase)
        {
            ["help"] = new("/help", "list the client's commands", _ => PrintHelp()),
            ["ap"] = new("/ap <text>", "send a message or !command to the AP server", SendToServer),
            ["hint"] = new("/hint [item]", "same as /ap !hint [item]",
                args => SendToServer(args.Length == 0 ? "!hint" : $"!hint {args}")),
            ["return"] = new("/return <item>", "move a received item back into your inventory", ReturnItem),
            ["getmail"] = new("/getmail", "move all AP items waiting in the mail into your inventory", _ => GetMail()),
            ["loc"] = new("/loc", "show your current room/node/view", _ => PrintLocation()),
            ["force_seed"] = new("/force_seed", "bypass the save/seed guard by tagging this save with the current seed",
                _ => ForceSeed()),
        };
    }

    /// <summary>The client command that <paramref name="text"/> invokes, ready to run, or null if it isn't one.</summary>
    public Action? Find(string text)
    {
        text = text.Trim();
        if (!text.StartsWith('/'))
            return null;

        string[] parts = text[1..].Split(' ', 2, StringSplitOptions.TrimEntries);
        if (!_commands.TryGetValue(parts[0], out Command? command))
            return null;

        string args = parts.Length > 1 ? parts[1] : "";
        return () => command.Handler(args);
    }

    /// <summary>Tells the player that <paramref name="text"/>, typed in game, isn't a client command, and how to send
    /// it to the AP server instead.</summary>
    public void ReportUnknown(string text)
    {
        text = text.Trim();
        string message = $"Unknown command \"{text}\" - to send it to the AP server, use /ap {text}";
        _ctx.Report.ReportAction(false, message);
        _ctx.Report.AppendServerLog($"CLIENT: {message}");
        _ctx.ShowPetMessage($"AP: {message}");
    }

    /// <summary>/help: lists every client command with its description in the server log, and just their names in
    /// the PET, which has no room for the descriptions.</summary>
    private void PrintHelp()
    {
        _ctx.Report.AppendServerLog("CLIENT: Client commands (anything else typed here goes to the AP server):");
        foreach (Command command in _commands.Values)
            _ctx.Report.AppendServerLog($"CLIENT:   {command.Usage} - {command.Description}");
        _ctx.ShowPetMessage($"AP: Commands: {string.Join(", ", _commands.Keys.Select(name => "/" + name))}"
            + " - see the client's server log for details.");
    }

    /// <summary>/ap &lt;text&gt;: sends the rest of the line to the AP server as-is, e.g. "/ap !hint X" sends "!hint X".</summary>
    private void SendToServer(string text)
    {
        if (text.Length == 0)
        {
            _ctx.Report.AppendServerLog("CLIENT: Usage: /ap <message or !command>");
            _ctx.ShowPetMessage("AP: Usage: /ap <message or !command>");
            return;
        }

        bool sent = _ctx.Ap.SendCommand(text);
        _ctx.Report.ReportAction(sent, $"/ap: send \"{text}\" to the AP server");
        if (!sent)
        {
            _ctx.Report.AppendServerLog("CLIENT: Not connected - message not sent");
            _ctx.ShowPetMessage("AP: Not connected to an AP server - message not sent.");
        }
    }

    /// <summary>/return &lt;item&gt;: moves an item the AP server has already granted back into the player's inventory.
    /// The item is its AP name, or any unique part of it (e.g. "/return pistachio").</summary>
    private void ReturnItem(string query)
    {
        if (query.Length == 0)
        {
            ReportReturn(false, "Usage: /return <item name>");
            return;
        }
        if (_ctx.Options.EnforceSaveSeedGuard && _saveSeedGuard.State != SaveSeedGuardState.Ok)
        {
            ReportReturn(false, "/return failed - AP actions are blocked for this save (see the save/seed guard).");
            return;
        }

        List<string> matches = ItemSync.MatchReturnableItems(query);
        if (matches.Count != 1)
        {
            ReportReturn(false, matches.Count == 0
                ? $"/return failed - no returnable item matches \"{query}\"."
                : $"/return failed - \"{query}\" matches more than one item: {string.Join(", ", matches)}.");
            return;
        }

        string itemName = matches[0];
        ReturnItemResult result = _itemSync.ReturnToInventory(itemName);
        ReportReturn(result == ReturnItemResult.Returned, result switch
        {
            ReturnItemResult.NotInGame => "/return failed: not in game yet.",
            ReturnItemResult.UnknownItem => $"/return failed: {itemName} can't be returned.",
            ReturnItemResult.NotGranted => $"/return failed: {itemName} hasn't been received from the AP server yet.",
            ReturnItemResult.NotFound => $"/return failed: couldn't find {itemName} in the game.",
            ReturnItemResult.AlreadyInInventory => $"/return: {itemName} is already in your inventory.",
            ReturnItemResult.InstalledInFuseBox => $"/return failed: {itemName} is installed in the Fuse Box.",
            ReturnItemResult.Restored => $"/return failed: {itemName} is standing in for a check right now.",
            ReturnItemResult.MoveFailed => $"/return failed: couldn't move {itemName} to your inventory.",
            _ => $"/return: {itemName} returned to your inventory.",
        });
    }

    /// <summary>/getMail: moves every item the client delivered to the mail system into the player's inventory, so
    /// they don't have to be fetched from a SuccUBus.</summary>
    private void GetMail()
    {
        if (_ctx.Options.EnforceSaveSeedGuard && _saveSeedGuard.State != SaveSeedGuardState.Ok)
        {
            ReportReturn(false, "/getMail failed - AP actions are blocked for this save (see the save/seed guard).");
            return;
        }

        if (_itemSync.CollectToolPlacedMail() is not (var moved, var failed))
        {
            ReportReturn(false, "/getMail failed: not in game yet.");
            return;
        }

        if (moved.Count == 0 && failed.Count == 0)
            ReportReturn(true, "/getMail: no AP items are waiting in the mail.");
        else if (failed.Count == 0)
            ReportReturn(true, $"/getMail: moved to your inventory: {string.Join(", ", moved)}.");
        else
            ReportReturn(false, moved.Count == 0
                ? $"/getMail failed: couldn't move {string.Join(", ", failed)} to your inventory."
                : $"/getMail: moved {string.Join(", ", moved)} to your inventory, but couldn't move {string.Join(", ", failed)}.");
    }

    /// <summary>/loc: prints the player's current room/node/view, in the [room, node, view] form the docs use.</summary>
    private void PrintLocation()
    {
        RoomNodeView? rnv = _ctx.GameManager is long gm ? GameState.ReadRoomNodeView(_ctx.Mem, gm) : null;
        if (rnv is not { } r)
        {
            ReportReturn(false, "/loc failed: not in game yet.");
            return;
        }

        string room = _ctx.CurrentRoomName is { } name ? $" ({name})" : "";
        ReportReturn(true, $"/loc: RNV [{r.Room}, {r.Node}, {r.View}]{room}");
    }

    private void ReportReturn(bool success, string message)
    {
        _ctx.Report.ReportAction(success, message);
        _ctx.Report.AppendServerLog($"CLIENT: {message}");
        _ctx.ShowPetMessage($"AP: {message}");
    }

    /// <summary>/force_seed: overrides the save/AP-seed guard by tagging the attached save with the connected seed.</summary>
    private void ForceSeed()
    {
        ForceTagResult result = _saveSeedGuard.ForceTag();
        (string log, string pet) = result switch
        {
            ForceTagResult.NotAttached or ForceTagResult.GameNotResolved => (
                "/force_seed: not attached / game not resolved yet",
                "AP: /force_seed failed - client isn't attached to the game yet."),
            ForceTagResult.NotConnected => (
                "/force_seed: not connected to an AP server - no seed to tag with",
                "AP: /force_seed failed - not connected to an AP server."),
            ForceTagResult.ProjectNotResolved => (
                "/force_seed: project not resolved yet",
                "AP: /force_seed failed - game state not resolved yet, try again in a moment."),
            ForceTagResult.BeamBridgeNotFound => (
                "/force_seed: could not locate BeamBridge to tag",
                "AP: /force_seed failed - could not locate the BeamBridge item to tag."),
            ForceTagResult.WriteFailed => (
                "/force_seed: failed to write the guard tag",
                "AP: /force_seed failed - see client log."),
            _ => (
                "/force_seed: save/AP-seed guard bypassed - this save is now tagged with the current seed/slot",
                "AP: Save/seed guard bypassed - AP actions are re-enabled for this save."),
        };

        _ctx.Report.ReportAction(result == ForceTagResult.Tagged, log);
        if (result is ForceTagResult.Tagged or ForceTagResult.WriteFailed)
            _ctx.Report.AppendServerLog($"CLIENT: {log}", bold: true);
        _ctx.ShowPetMessage(pet);
    }
}
