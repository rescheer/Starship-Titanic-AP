using Archipelago.MultiClient.Net.Enums;

namespace StarshipTitanicAp;

/// <summary>Turns a hint into the colored parts the window shows, so they read the same everywhere.</summary>
internal static class HintText
{
    /// <summary>Where a hinted item is: the slot and location, and the entrance if the hint has one.</summary>
    public static (string Text, Color Color)[] Where(HintInfo hint)
    {
        var parts = new List<(string Text, Color Color)>
        {
            (hint.FindingSlot, TrackerTheme.SlotColor(hint.FinderIsThisSlot)),
            ("  ", TrackerTheme.Text),
            (hint.LocationName, TrackerTheme.LocationText),
        };
        if (hint.Entrance.Length > 0)
        {
            parts.Add((" at ", TrackerTheme.TextMuted));
            parts.Add((hint.Entrance, TrackerTheme.EntranceText));
        }
        return parts.ToArray();
    }

    /// <summary>What's in a hinted check: "Slot - Item".</summary>
    public static (string Text, Color Color)[] What(HintInfo hint) => new[]
    {
        (hint.ReceivingSlot, TrackerTheme.SlotColor(hint.ReceiverIsThisSlot)),
        (" - ", TrackerTheme.TextMuted),
        (hint.ItemName, TrackerTheme.ItemColor(hint.Flags)),
    };

    /// <summary>How a hint on a check looks there. An unspecified hint goes by its item's type, so that finding filler
    /// or a trap doesn't make a check look wanted.</summary>
    public static CheckHint ForCheck(HintInfo hint)
    {
        (string caption, Color? dot) = hint.Status switch
        {
            HintStatus.Priority => ("HINTED: PRIORITY", TrackerTheme.HintedDot),
            HintStatus.Avoid => ("HINTED: AVOID", TrackerTheme.AvoidDot),
            HintStatus.NoPriority => ("HINTED: NO PRIORITY", (Color?)null),
            HintStatus.Unspecified when hint.Flags.HasFlag(ItemFlags.Trap) => ("HINTED", TrackerTheme.AvoidDot),
            HintStatus.Unspecified when hint.Flags.HasFlag(ItemFlags.Advancement) || hint.Flags.HasFlag(ItemFlags.NeverExclude) => ("HINTED", TrackerTheme.HintedDot),
            _ => ("HINTED", null),
        };
        return new CheckHint(caption, dot, What(hint));
    }
}
