namespace StarshipTitanicAp;

internal static class NoseItem
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "Nose",
        ApItemName = "Titania's Nose",
        PickupLocationName = "Parrot Lobby - Titania's Nose",
        HomeRnvs = new[] { new RoomNodeView(9, 2, 3) },
        DefaultParent = new DefaultParent("NoseHolder", "CNoseHolder"),
    };
}
