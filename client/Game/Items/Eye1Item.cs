namespace StarshipTitanicAp;

internal static class Eye1Item
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "Eye1",
        ApItemName = "Titania's Eye (Light)",
        PickupLocationName = "1st Class Stateroom - Titania's Eye (Light)",
        HomeRnvs = new[] { new RoomNodeView(7, 6, 4) },
        DefaultParent = new DefaultParent(null, "CViewItem"),
    };
}
