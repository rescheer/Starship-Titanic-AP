namespace StarshipTitanicAp;

internal static class Ear1Item
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "Ear1",
        ApItemName = "Titania's Ear (Pistachio Bowl)",
        PickupLocationName = "2nd Class Stateroom - Titania's Ear (Pistachio Bowl)",
        HomeRnvs = new[] { new RoomNodeView(6, 4, 3) },
        DefaultParent = new DefaultParent("SW", "CViewItem"),
    };
}
