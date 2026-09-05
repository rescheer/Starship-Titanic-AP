namespace StarshipTitanicAp;

internal static class NapkinItem
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "Napkin",
        ApItemName = "Napkin",
        PickupLocationName = "1st Class Restaurant - Napkin",
        HomeRnvs = new[] { new RoomNodeView(49, 8, 1) },
        DefaultParent = new DefaultParent(null, "CViewItem"),
        // Same generic-restoration issue as BrokenLiftbotHead/Ear2/MaitreDLeftArm despite sharing a view with
        // baked room art: the napkin itself is still the visual for its own sprite, so the generic restore
        // flow's _visible=false left it present but not shown. Confirmed live that KeepVisible alone fixes it.
        // No captured bounds/cursorId override yet; if artifacts show up later, capture live values via
        // ItemFieldsForm and fill them in here.
        RestoreFieldOverride = new RestoreFieldOverride(null, null, null, null, null, null, KeepVisible: true),
    };
}
