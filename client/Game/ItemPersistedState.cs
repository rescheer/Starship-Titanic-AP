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
}

/// <summary>The full persisted state for one tracked item, packed into a single 4-byte int.</summary>
/// <param name="ToolDelivered">True while <see cref="ItemStage.Mail"/> holds an item this app proactively mailed
/// (an AP grant landing before the player found it naturally), as opposed to the game's own script placing it
/// there (a genuine natural delivery). Only meaningful for the Magazine (see MagazineItem.cs); recorded at the
/// moment the item enters Mail.</param>
public readonly record struct ItemPersistedState(ItemStage Stage, bool CheckFired, ItemPulledFrom PulledFrom, bool ToolDelivered = false)
{
    public static readonly ItemPersistedState None = new(ItemStage.None, false, ItemPulledFrom.None);

    public static ItemPersistedState Decode(int raw) => new(
        (ItemStage)(raw & 0xFF),
        ((raw >> 8) & 1) != 0,
        (ItemPulledFrom)((raw >> 16) & 0xFF),
        ((raw >> 9) & 1) != 0);

    public int Encode() =>
        (int)Stage | (CheckFired ? 1 << 8 : 0) | ((int)PulledFrom << 16) | (ToolDelivered ? 1 << 9 : 0);
}
