namespace StarshipTitanicAp;

internal static class CrushedTVItem
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "CrushedTV",
        ApItemName = "Crushed TV",
        PickupLocationName = "Bottom of the Well - Crushed Television",
        HomeRnvs = new[] { new RoomNodeView(38, 7, 1) },
        DefaultParent = new DefaultParent(null, "CViewItem"),
        RestoreFieldOverride = new RestoreFieldOverride(183, 218, 303, 338, 8, -1, KeepVisible: true),
    };
}
