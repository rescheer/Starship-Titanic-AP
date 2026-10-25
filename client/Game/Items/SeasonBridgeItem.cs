namespace StarshipTitanicAp;

internal static class SeasonBridgeItem
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "SeasonBridge",
        ApItemName = "Green Fuse",
        PickupLocationName = "1st Class Restaurant - Green Fuse",
        HomeRnvs = new[] { new RoomNodeView(49, 8, 1) },
        DefaultParent = new DefaultParent(null, "CViewItem"),
        IsFuseBoxItem = true,
        RestoreFieldOverride = new RestoreFieldOverride(222, 319, 310, 407, null, null, KeepVisible: true),
    };
}
