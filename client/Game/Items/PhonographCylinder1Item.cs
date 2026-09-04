namespace StarshipTitanicAp;

internal static class PhonographCylinder1Item
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "Phonograph Cylinder 1",
        DefaultParent = new DefaultParent("Music Room Cylinder Holder", "CRestaurantCylinderHolder"),
    };
}
