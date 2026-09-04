namespace StarshipTitanicAp;

internal static class Eye2Item
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "Eye2",
        ApItemName = "Titania's Eye (Elevator)",
        PickupLocationName = "Broken Elevator - Titania's Eye (Elevator)",
        HomeRnvs = new[] { new RoomNodeView(21, 1, 4) },
        DefaultParent = new DefaultParent(null, "CViewItem"),
        // All four lifts share (21, 1, 4) for their "get the Eye"/head-socket viewport, and their room-flags are
        // also indistinguishable (all report 0x96E45) - there's currently no known signal to scope restoration to
        // just the broken lift (elevator 4). Confirmed live that this is harmless: restoring Eye2 while riding one
        // of the three working lifts doesn't produce any visible or functional side effect there, so this
        // restores unconditionally at (21, 1, 4) rather than gating on something that doesn't actually work.
        // Titania's Eye/Elevator: CGetLiftEye2::MouseDragStartMsg forwards straight to the item without ever
        // consulting _canTake (see GetLiftEye2GateHook's doc comment), so the flag is never set true by any
        // natural engine path - once this app delivers it via mail instead, it needs this override or it sits
        // undeliverable in the tray.
        RequiresCanTakeOverride = true,
    };
}
