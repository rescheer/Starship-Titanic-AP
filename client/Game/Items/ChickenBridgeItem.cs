namespace StarshipTitanicAp;

internal static class ChickenBridgeItem
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "ChickenBridge",
        ServerGrantedApItemName = "Yellow Fuse",
        DefaultParent = new DefaultParent(null, "CViewItem"),
        IsFuseBoxItem = true,
    };
}
