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
        RestoreFieldOverride = new RestoreFieldOverride(36, 240, 266, 440, null, null, null),
        RestoreLoadsEnterFrame = true,
        // Hidden until the gondolier puzzle's CVisibleMsg, which loads _visibleFrame (left at 2, the glyph).
        RestoreResetsVisibleFrame = true,
    };
}
