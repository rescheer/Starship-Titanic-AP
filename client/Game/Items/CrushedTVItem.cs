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
        // Shows the same symptom as BrokenLiftbotHead: uninteractable after restoration because the generic
        // flow's forced _visible false isn't actually redundant with baked room art here either. Values
        // captured live: L183 T218 R303 B338, cursorId 8 (matches BrokenLiftbotHead's non-default cursor).
        RestoreFieldOverride = new RestoreFieldOverride(183, 218, 303, 338, 8, -1, KeepVisible: true),
    };
}
