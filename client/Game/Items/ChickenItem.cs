namespace StarshipTitanicAp;

internal static class ChickenItem
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "Chicken",
        DefaultParent = new DefaultParent(null, "CViewItem"),
    };
}
