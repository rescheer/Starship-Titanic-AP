namespace StarshipTitanicAp;

internal static class PerchItem
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "Perch",
        ApItemName = "Perch",
        PickupLocationName = "Parrot Lobby - Perch",
        HomeRnvs = new[] { new RoomNodeView(9, 4, 4) },
        DefaultParent = new DefaultParent("PerchHolder", "CDropTarget"),
    };
}
