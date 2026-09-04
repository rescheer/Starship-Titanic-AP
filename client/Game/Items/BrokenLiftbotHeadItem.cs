namespace StarshipTitanicAp;

internal static class BrokenLiftbotHeadItem
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "BrokenLiftbotHead",
        ApItemName = "LiftBot Head",
        PickupLocationName = "Bottom of the Well - LiftBot Head",
        HomeRnvs = new[] { new RoomNodeView(38, 8, 1) },
        DefaultParent = new DefaultParent(null, "CViewItem"),
        RestoreFieldOverride = new RestoreFieldOverride(245, 258, 321, 334, 8, -1, KeepVisible: true),
    };
}
