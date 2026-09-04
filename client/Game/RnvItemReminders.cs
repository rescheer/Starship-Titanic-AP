namespace StarshipTitanicAp;

/// <summary>Points of interest that should nudge the player toward a still-ungranted AP item when they arrive.</summary>
public static class RnvItemReminders
{
    private readonly record struct Reminder(string ApItemName, string Message);

    // exact (Room, Node, View) -> AP item that must have been received, else Message is shown
    private static readonly Dictionary<RoomNodeView, Reminder> Reminders = new()
    {
        [new RoomNodeView(49, 4, 1)] = new Reminder("Restaurant Table Reservation", "AP Item needed: 1st Class Restaurant - Table Access"),
    };

    /// <summary>Looks up the reminder registered for an exact (Room, Node, View), if any.</summary>
    public static bool TryGetReminder(RoomNodeView rnv, out string apItemName, out string message)
    {
        if (Reminders.TryGetValue(rnv, out Reminder reminder))
        {
            apItemName = reminder.ApItemName;
            message = reminder.Message;
            return true;
        }

        apItemName = "";
        message = "";
        return false;
    }
}
