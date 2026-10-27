namespace StarshipTitanicAp;

internal sealed partial class ClientSession
{
    /// <summary>Sends the AP location check for a room's "Arrive for the First Time" location.</summary>
    private void TrySendRoomVisitCheck(string roomName)
    {
        if (!LocationChecks.TryGetLocationName(roomName, out string locationName))
            return;

        if (!_sentRoomVisitChecks.Add(roomName))
            return;

        bool handedOff = _apConnection.SendLocationCheck(locationName);
        ReportAction(handedOff, handedOff
            ? $"Location check: {roomName} -> {locationName}"
            : $"Location check queued (offline): {roomName} -> {locationName}");
        NotifyPendingChecksChanged();
    }

    /// <summary>Sends the AP location check for visiting an exact (Room, Node, View) point of interest.</summary>
    private void TrySendPointOfInterestCheck(RoomNodeView rnv)
    {
        if (!LocationChecks.TryGetPointOfInterestLocationName(rnv, out string locationName))
            return;

        if (!_sentPointOfInterestChecks.Add(rnv))
            return;

        bool handedOff = _apConnection.SendLocationCheck(locationName);
        ReportAction(handedOff, handedOff
            ? $"Location check: Room {rnv.Room} Node {rnv.Node} View {rnv.View} -> {locationName}"
            : $"Location check queued (offline): Room {rnv.Room} Node {rnv.Node} View {rnv.View} -> {locationName}");
        NotifyPendingChecksChanged();
    }

    /// <summary>Displays how many AP items are waiting in the mail system when the player arrives at an
    /// already-checked Succ-U-Bus terminal.</summary>
    private void TryShowWaitingMailReminder(RoomNodeView rnv)
    {
        if (!LocationChecks.TryGetPointOfInterestLocationName(rnv, out string locationName))
            return;
        if (!LocationChecks.SuccUBusStationLocationNames.Contains(locationName))
            return;
        if (!_apConnection.IsLocationCheckedOrQueued(locationName))
            return;
        if (_currentMailManRoom is null || _currentInventoryRoom is null)
            return;

        int count = GameState.ReadMailItems(_mem, _currentMailManRoom.Value).Count(i => i.ToolPlaced);
        if (count == 0)
            return;

        GameActions.DisplayMessageSmart(_mem, _currentInventoryRoom.Value,
            $"You have {count} AP item{(count == 1 ? "" : "s")} waiting to be delivered.");
    }

    /// <summary>Displays a reminder message if the player arrives at a point of interest without the AP item it requires.</summary>
    private void TryShowItemNeededReminder(RoomNodeView rnv)
    {
        if (!RnvItemReminders.TryGetReminder(rnv, out string apItemName, out string message))
            return;
        if (_currentInventoryRoom is null)
            return;

        string[] receivedItems = _apConnection.GetReceivedItemNames();
        if (receivedItems.Contains(apItemName, StringComparer.OrdinalIgnoreCase))
            return;

        GameActions.DisplayMessageSmart(_mem, _currentInventoryRoom.Value, message);
    }

    /// <summary>Sends the AP location check for physically finding a tracked item, if one is mapped.</summary>
    private void SendItemPickupCheck(string itemName)
    {
        if (!LocationChecks.TryGetItemPickupLocationName(itemName, out string locationName))
            return;

        bool handedOff = _apConnection.SendLocationCheck(locationName);
        ReportAction(handedOff, handedOff
            ? $"Location check: {itemName} -> {locationName}"
            : $"Location check queued (offline): {itemName} -> {locationName}");
        NotifyPendingChecksChanged();
    }
}
