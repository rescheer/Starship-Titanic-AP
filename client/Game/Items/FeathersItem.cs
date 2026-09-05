namespace StarshipTitanicAp;

/// <summary>Feathers never enters the generic per-item state machine (see CarryParrotItem) - its pickup check is
/// fired directly off CarryParrot's own escape-from-inventory transition (see ReconcileTrackedItems' isCarryParrot
/// branch), not off this object's own movement, so it doesn't double up with the check the generic loop would
/// otherwise fire when the real object lands in inventory. Every other aspect of AP-grant handling for the real
/// object is still the normal full-state-machine treatment (proactive mail delivery on grant, hide-then-deliver
/// for an ungranted natural pickup) - see TryHideOrDeliverFeather, which reimplements just that part by hand.</summary>
internal static class FeathersItem
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "Feathers",
        PickupLocationName = "Parrot Lobby - Feather",
        DefaultParent = new DefaultParent("CarryParrot", "CCarryParrot"),
    };
}
