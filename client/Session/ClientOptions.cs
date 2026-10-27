namespace StarshipTitanicAp;

/// <summary>Runtime toggles for <see cref="ClientSession"/>. Only the debug UI changes these.</summary>
internal sealed class ClientOptions
{
    /// <summary>Pause AP syncing until the attached save is verified against the connected AP seed.</summary>
    public bool EnforceSaveSeedGuard { get; set; } = true;

    /// <summary>Hide naturally picked-up items until their AP item has been received.</summary>
    public bool TakeUngrantedItems { get; set; } = true;

    /// <summary>Let the DeskBot's initial None -> Third upgrade through the class lock (no AP item for it yet).</summary>
    public bool AllowInitialUpgrade { get; set; } = true;

    /// <summary>Comma-separated room names or IDs to lock on top of those missing their Access Card.</summary>
    public string ExtraLockedRooms { get; set; } = "";
}
