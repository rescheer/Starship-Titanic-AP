namespace StarshipTitanicAp;

internal static class BeerGlassItem
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "BeerGlass",
        ApItemName = "Bar Glass",
        ServerGrantedApItemName = "Bar Glass",
        DefaultParent = new DefaultParent(null, "CViewItem"),
    };
}
