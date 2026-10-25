namespace StarshipTitanicAp;

/// <summary>Feathers is outside the generic per-item state machine. Its pickup check is sent from CarryParrot's
/// escape-from-inventory transition (ReconcileTrackedItems' isCarryParrot branch); AP-grant handling for the real
/// object is in TryHideOrDeliverFeather.</summary>
internal static class FeathersItem
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "Feathers",
        PickupLocationName = "Parrot Lobby - Feather",
        DefaultParent = new DefaultParent("CarryParrot", "CCarryParrot"),
    };
}
