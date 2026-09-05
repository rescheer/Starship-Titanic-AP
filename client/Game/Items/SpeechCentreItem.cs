namespace StarshipTitanicAp;

internal static class SpeechCentreItem
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "SpeechCentre",
        ApItemName = "Titania's Speech Center",
        PickupLocationName = "Arboretum - Titania's Speech Center",
        HomeRnvs = new[] { new RoomNodeView(48, 2, 2) },
        DefaultParent = new DefaultParent(null, "CViewItem"),
        SkipFirstChildReorderOnRestore = true,
    };
}
