namespace StarshipTitanicAp;

internal sealed partial class ClientSession
{
    /// <summary>Forces the next mail poll to raise <see cref="MailChanged"/> even if the mail contents look unchanged.</summary>
    internal void InvalidateMail() => _lastMailItems = null;

    private void UpdateMail()
    {
        long? project = _currentProject;
        if (project is null)
            return;

        // Only a non-null result replaces the cached address.
        long? mailManRoom = GameState.FindMailManRoom(_mem, project.Value);
        if (mailManRoom is not null)
            _currentMailManRoom = mailManRoom;
        ReportAddress("Mail Inventory (CMailMan)", _currentMailManRoom);

        if (_currentMailManRoom is null)
        {
            MailChanged?.Invoke(null);
            return;
        }

        List<GameState.MailItem> items = GameState.ReadMailItems(_mem, _currentMailManRoom.Value);

        bool changed = _lastMailItems is null
            || items.Count != _lastMailItems.Count
            || !items.Select(i => (i.Name, i.IsPendingMail, i.DestRoomFlags, i.RoomFlags, i.ToolPlaced))
                     .SequenceEqual(_lastMailItems.Select(i => (i.Name, i.IsPendingMail, i.DestRoomFlags, i.RoomFlags, i.ToolPlaced)));

        if (!changed)
            return;

        _lastMailItems = items;
        MailChanged?.Invoke(items);
    }

    /// <summary>Called on every real room change; retargets any tool-placed mail item to this room's station.</summary>
    private void DeliverQueuedMailAtStation(string roomName)
    {
        if (_currentMailManRoom is null)
            return;
        uint? liveRoomFlags = _currentInventoryRoom is long petControl ? GameState.ReadCurrentRoomFlags(_mem, petControl) : null;
        if (!ChevronCodes.TryGetCode(roomName, liveRoomFlags, out uint code))
            return;

        List<GameState.MailItem> items = GameState.ReadMailItems(_mem, _currentMailManRoom.Value);
        int delivered = 0;
        foreach (GameState.MailItem item in items)
        {
            if (!item.ToolPlaced)
                continue;

            if (GameActions.SetItemMailDestination(_mem, item.Address, code))
                delivered++;
        }

        if (delivered > 0)
        {
            _lastMailItems = null;
            ReportAction(true, $"Updated {delivered} tool-placed item(s) to {roomName} station");
        }
    }
}
