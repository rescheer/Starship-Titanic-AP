namespace StarshipTitanicAp;

internal static class HoseItem
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "Hose",
        ApItemName = "Hose",
        PickupLocationName = "Arboretum - Hose",
        // Arboretum / Frozen Arboretum
        HomeRnvs = new[] { new RoomNodeView(48, 3, 3), new RoomNodeView(52, 3, 3) },
        DefaultParent = new DefaultParent(null, "CViewItem"),
    };
}
