namespace StarshipTitanicAp;

internal static class LongStickItem
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "LongStick",
        ApItemName = "Long Stick",
        PickupLocationName = "SGT Class Lobby - Long Stick",
        HomeRnvs = new[] { new RoomNodeView(24, 3, 1) },
        DefaultParent = new DefaultParent(null, "CViewItem"),
    };
}
