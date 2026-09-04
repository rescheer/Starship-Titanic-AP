namespace StarshipTitanicAp;

internal static class CentralCoreItem
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "CentralCore",
        ApItemName = "Titania's Core",
        PickupLocationName = "Parrot Lobby - Titania's Core",
        HomeRnvs = new[] { new RoomNodeView(9, 1, 2) },
        DefaultParent = new DefaultParent("PerchCoreHolder", "CParrotPerchHolder"),
    };
}
