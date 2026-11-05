namespace StarshipTitanicAp;

/// <summary>Which physical "stage" a tracked item is currently in, per this app's own bookkeeping.</summary>
public enum ItemStage : byte
{
    None = 0,
    Hidden = 1,
    Mail = 2,
    Inventory = 3,
    Restored = 4,
}

/// <summary>Where a Restored item should go back to if the player leaves its home RNV without picking it up.</summary>
public enum ItemPulledFrom : byte
{
    None = 0,
    Hidden = 1,
    Mail = 2,
    Inventory = 3,
    /// <summary>A fuse or Titania part restored while installed in a Fuse Box socket or one of Titania's slots. It goes
    /// back there whether or not it's picked up, since the socket still shows it installed.</summary>
    Installed = 4,
}

/// <summary>The full persisted state for one tracked item, packed into a single 4-byte int.</summary>
/// <param name="ToolPlaced">True while <see cref="ItemStage.Mail"/> holds an item this app mailed (an AP grant, or a
/// Debug form move), as opposed to the game's own script placing it there (a genuine natural delivery). Recorded at
/// the moment the item enters Mail, and read through GameActions.IsItemToolPlaced. It lives here rather than in a
/// double field because the save format only round-trips small integers through those.</param>
/// <param name="ReshowSibling">True while <see cref="ItemStage.Restored"/> holds an item whose HideSiblingOnRestore
/// sibling has to be shown again once the item leaves its home parent (see ItemSync.ReshowHiddenSibling). Recorded at
/// the moment the item is restored.</param>
/// <param name="InstalledVisible">While <see cref="ItemStage.Restored"/> holds an item pulled from
/// <see cref="ItemPulledFrom.Installed"/>: its _visible as the install left it, put back when it returns.</param>
/// <param name="InstalledFlag">Likewise, the flag the install set on the part itself (see
/// GameActions.ReadItemInstalledFlag), cleared while it's restored.</param>
public readonly record struct ItemPersistedState(ItemStage Stage, bool CheckFired, ItemPulledFrom PulledFrom, bool ToolPlaced = false, bool ReshowSibling = false,
    bool InstalledVisible = false, bool InstalledFlag = false)
{
    public static readonly ItemPersistedState None = new(ItemStage.None, false, ItemPulledFrom.None);

    /// <summary>Whether the item has reached the inventory. Stays true once it has, including while the item is pulled
    /// back to its home spot for a natural pickup, or has since been used somewhere (an item at Stage.Inventory isn't
    /// rewritten when it leaves).</summary>
    public bool HasBeenInInventory => Stage == ItemStage.Inventory
        || (Stage == ItemStage.Restored && PulledFrom is ItemPulledFrom.Inventory or ItemPulledFrom.Installed);

    public static ItemPersistedState Decode(int raw) => new(
        (ItemStage)(raw & 0xFF),
        ((raw >> 8) & 1) != 0,
        (ItemPulledFrom)((raw >> 16) & 0xFF),
        ((raw >> 9) & 1) != 0,
        ((raw >> 10) & 1) != 0,
        ((raw >> 11) & 1) != 0,
        ((raw >> 12) & 1) != 0);

    public int Encode() =>
        (int)Stage | (CheckFired ? 1 << 8 : 0) | ((int)PulledFrom << 16) | (ToolPlaced ? 1 << 9 : 0) | (ReshowSibling ? 1 << 10 : 0)
        | (InstalledVisible ? 1 << 11 : 0) | (InstalledFlag ? 1 << 12 : 0);
}
