namespace StarshipTitanicAp;

internal static class Eye1Item
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "Eye1",
        PickupLocationName = "1st Class Stateroom - Titania's Eye (Light)",
        DefaultParent = new DefaultParent(null, "CViewItem"),
    };
}
