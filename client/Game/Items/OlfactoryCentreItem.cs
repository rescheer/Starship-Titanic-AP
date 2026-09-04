namespace StarshipTitanicAp;

internal static class OlfactoryCentreItem
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "OlfactoryCentre",
        ApItemName = "Titania's Olfactory Center",
        PickupLocationName = "Bilge Room - Titania's Olfactory Center",
        HomeRnvs = new[] { new RoomNodeView(47, 1, 2) },
        DefaultParent = new DefaultParent("Brobostigon Search Point", "CSearchPoint"),
    };
}
