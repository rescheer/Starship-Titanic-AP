namespace StarshipTitanicAp;

internal static class MouthItem
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "Mouth",
        ApItemName = "Titania's Mouth",
        PickupLocationName = "Arboretum - Titania's Mouth",
        HomeRnvs = new[] { new RoomNodeView(52, 5, 2) },
        DefaultParent = new DefaultParent(null, "CViewItem"),
    };
}
