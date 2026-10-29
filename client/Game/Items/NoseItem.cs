namespace StarshipTitanicAp;

internal static class NoseItem
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "Nose",
        ApItemName = "Titania's Nose",
        PickupLocationName = "Parrot Lobby - Titania's Nose",
        HomeRnvs = new[] { new RoomNodeView(9, 2, 3) },
        DefaultParent = new DefaultParent("NoseHolder", "CNoseHolder"),
        HomeParentIsDropTargetDisplay = true,
        // Vanilla's pickup goes through CNoseHolder (CDropTarget::MouseDragStartMsg's CPassOnDragStartMsg), which
        // never consults the Nose's own _canTake, so the authored false stays set.
        RequiresCanTakeOverride = true,
    };
}
