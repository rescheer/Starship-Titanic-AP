namespace StarshipTitanicAp;

/// <summary>Classifies engine item names for the per-item state machine, backed by the per-item definitions
/// in Game/Items/.</summary>
public static class ItemTracking
{
    public static bool IsOneDirectionalItem(string itemName) =>
        Items.TryGet(itemName, out ItemDefinition item) && item.IsOneDirectional;

    /// <summary>CarryParrot never enters the state machine at all - only its natural pickup is detected.</summary>
    public const string CarryParrotName = "CarryParrot";

    /// <summary>True for an item that goes through the full state machine.</summary>
    public static bool IsFullStateMachineItem(string itemName) =>
        !string.Equals(itemName, CarryParrotName, StringComparison.OrdinalIgnoreCase)
        && !IsOneDirectionalItem(itemName)
        && LocationChecks.TryGetApItemName(itemName, out _);

    /// <summary>Engine names for the items granted directly by the multiworld server rather than via a natural-pickup check.</summary>
    public static readonly IReadOnlyDictionary<string, string> ServerGrantedItemNames = Items.All
        .Where(d => d.ServerGrantedApItemName is not null)
        .ToDictionary(d => d.Name, d => d.ServerGrantedApItemName!, StringComparer.OrdinalIgnoreCase);

    public static bool IsFuseBoxItem(string itemName) =>
        Items.TryGet(itemName, out ItemDefinition item) && item.IsFuseBoxItem;

    public static bool IsRestorationExcluded(string itemName) =>
        Items.TryGet(itemName, out ItemDefinition item) && item.IsRestorationExcluded;

    public static bool RequiresCanTakeOverride(string itemName) =>
        Items.TryGet(itemName, out ItemDefinition item) && item.RequiresCanTakeOverride;

    public static bool RequiresCanTakeRestoreToggle(string itemName) =>
        Items.TryGet(itemName, out ItemDefinition item) && item.RequiresCanTakeRestoreToggle;

    public static bool SkipsFirstChildReorderOnRestore(string itemName) =>
        Items.TryGet(itemName, out ItemDefinition item) && item.SkipFirstChildReorderOnRestore;
}
