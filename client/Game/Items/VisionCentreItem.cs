namespace StarshipTitanicAp;

internal static class VisionCentreItem
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "VisionCentre",
        ApItemName = "Titania's Vision Center",
        PickupLocationName = "Bar - Titania's Vision Center",
        HomeRnvs = new[] { new RoomNodeView(31, 3, 2) },
        DefaultParent = new DefaultParent(null, "CViewItem"),
        RequiresCanTakeRestoreToggle = true,
        RestoreFieldOverride = new RestoreFieldOverride(272, 207, 352, 287, null, null, null),
    };
}
