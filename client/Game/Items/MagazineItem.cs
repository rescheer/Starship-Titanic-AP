namespace StarshipTitanicAp;

internal static class MagazineItem
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "Magazine",
        ApItemName = "Magazine",
        PickupLocationName = "SGT Class Lobby - Magazine",
        HomeRnvs = new[] { new RoomNodeView(27, 4, 1) },
        // Restored on arrival at the SGT TV [27,4,1], but its actual fresh-save parent is the hidden CViewItem
        // at [27,1,2].
        HomeParentSearchRnvOverride = new[] { new RoomNodeView(27, 1, 2) },
        DefaultParent = new DefaultParent(null, "CViewItem"),
    };
}
