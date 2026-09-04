namespace StarshipTitanicAp;

internal static class NapkinItem
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "Napkin",
        ApItemName = "Napkin",
        PickupLocationName = "1st Class Restaurant - Napkin",
        HomeRnvs = new[] { new RoomNodeView(49, 8, 1) },
        DefaultParent = new DefaultParent(null, "CViewItem"),
    };
}
