namespace StarshipTitanicAp;

internal static class BigHammerItem
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "BigHammer",
        ApItemName = "Hammer",
        PickupLocationName = "Promenade Deck - Hammer",
        HomeRnvs = new[] { new RoomNodeView(16, 2, 1) },
        DefaultParent = new DefaultParent(null, "CViewItem"),
    };
}
