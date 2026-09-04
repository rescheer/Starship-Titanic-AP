namespace StarshipTitanicAp;

internal static class PhotographItem
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "Photograph",
        DefaultParent = new DefaultParent(null, "CViewItem"),
    };
}
