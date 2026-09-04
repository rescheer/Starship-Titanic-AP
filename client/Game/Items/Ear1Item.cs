namespace StarshipTitanicAp;

internal static class Ear1Item
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "Ear1",
        ApItemName = "Titania's Ear (Pistachio Bowl)",
        PickupLocationName = "2nd Class Stateroom - Titania's Ear (Pistachio Bowl)",
        HomeRnvs = new[] { new RoomNodeView(6, 4, 3) },
        DefaultParent = new DefaultParent("SW", "CViewItem"),
        // Unlike Ear2/Eye2, this item's own _canTake is vanilla-true the whole time - what actually blocks
        // picking it up before the nut puzzle is solved is the separate CBowlUnlocker sibling rendering on top
        // of it (CBowlUnlocker::MovieEndMsg sets its own _visible false once unlocked), so no canTake override
        // is needed here; just restore its exact captured child state.
        RestoreFieldOverride = new RestoreFieldOverride(0, 0, 76, 76, null, 0, KeepVisible: false),
    };
}
