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
        // Same generic-restoration issue as BrokenLiftbotHead/Ear2/MaitreDLeftArm despite sharing a view with
        // baked room art: the fuse itself is still the visual for its own sprite, so the generic restore flow's
        // _visible=false left it present but not shown. Confirmed live that KeepVisible alone fixes it. No
        // captured bounds/cursorId override yet; if artifacts show up later, capture live values via
        // ItemFieldsForm and fill them in here.
        RestoreFieldOverride = new RestoreFieldOverride(222, 319, 310, 407, null, null, KeepVisible: true),
    };
}
