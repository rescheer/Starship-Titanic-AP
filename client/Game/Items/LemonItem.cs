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
        // Lemon's visibility/position are driven by the vanilla drop puzzle itself (CLemonDispensor::FrameMsg ->
        // CFruit::LemonFallsFromTreeMsg sets _visible true and dragMove()s it, then CFruit::FrameMsg animates
        // _bounds.top down until the fall completes) - not by our restore flow, hence KeepVisible: false so we
        // don't fight that. The real bug wasn't any of this item's own fields (bounds/visible/surface all
        // matched vanilla exactly post-fall, confirmed live) - it was the generic restore flow's unconditional
        // MoveToFirstChild reorder. Lemon's authored sibling position is right after SeasonBackground in the
        // Arboretum's CViewItem; forcing it to be the first child instead moved it in front of that background,
        // which then painted over it every frame - fully rendered per its own state, just occluded. See
        // SkipFirstChildReorderOnRestore.
        RestoreFieldOverride = new RestoreFieldOverride(35, 162, 115, 242, 8, -1, KeepVisible: false),
        SkipFirstChildReorderOnRestore = true,
    };
}
