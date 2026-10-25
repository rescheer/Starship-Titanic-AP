namespace StarshipTitanicAp;

internal static class MaitreDLeftArmItem
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "MaitreD Left Arm",
        ApItemName = "Maitre'D Bot's Left Arm",
        PickupLocationName = "1st Class Restaurant - Maitre'D Bot's Left Arm",
        HomeRnvs = new[] { new RoomNodeView(49, 8, 1) },
        DefaultParent = new DefaultParent(null, "CViewItem"),
        RestoreFieldOverride = new RestoreFieldOverride(274, 68, 374, 332, null, null, KeepVisible: true),
        SkipFirstChildReorderOnRestore = true,
    };
}
