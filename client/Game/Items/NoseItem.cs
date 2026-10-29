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
        HomeParentIsDropTargetDisplay = true,
        // Authored _bounds are 120x120 (never redrawn while hidden in the NoseHolder); the real surface is 80x80.
        InventoryBoundsSize = (80, 80),
    };
}
