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
        // Same generic-restoration issue as BrokenLiftbotHead/Ear2: the arm itself is the visual on this
        // CViewItem (no separate baked room art depicting it), so the generic restoration flow forcing
        // _visible false leaves it present but unclickable. No captured bounds/cursorId override yet beyond
        // KeepVisible; if that alone isn't enough, capture live values via ItemFieldsForm and fill them in here.
        RestoreFieldOverride = new RestoreFieldOverride(null, null, null, null, null, null, KeepVisible: true),
        SkipFirstChildReorderOnRestore = true,
    };
}
