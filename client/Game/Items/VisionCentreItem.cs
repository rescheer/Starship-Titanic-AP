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
        // CVisionCentre::MouseDragStartMsg claims every drag that reaches it, visible or not, and the Bar view hands
        // drags to its children in tree order. As first child it would beat PickUpGlass to the glass; vanilla has it
        // after PickUpGlass and PickUpVisCentre, which appending it last keeps.
        SkipFirstChildReorderOnRestore = true,
        RestoreFieldOverride = new RestoreFieldOverride(272, 207, 352, 287, null, null, null),
    };
}
