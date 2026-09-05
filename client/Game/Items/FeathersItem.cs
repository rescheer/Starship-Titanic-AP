namespace StarshipTitanicAp;

internal static class FeathersItem
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "Feathers",
        ApItemName = "Feather",
        PickupLocationName = "Parrot Lobby - Feather",
        DefaultParent = new DefaultParent("CarryParrot", "CCarryParrot"),
    };
}
