namespace StarshipTitanicAp;

internal static class LemonItem
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "Lemon",
        ApItemName = "Lemon",
        PickupLocationName = "Arboretum - Lemon",
        HomeRnvs = new[] { new RoomNodeView(48, 2, 2) },
        DefaultParent = new DefaultParent(null, "CViewItem"),
        RestoreFieldOverride = new RestoreFieldOverride(35, 162, 115, 242, 8, -1, KeepVisible: false),
        SkipFirstChildReorderOnRestore = true,
        RequiresCanTakeOverride = true,
    };
}
