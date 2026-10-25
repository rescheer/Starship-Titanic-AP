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
        RequiresCanTakeOverride = true,
    };
}
