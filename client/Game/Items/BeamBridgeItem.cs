namespace StarshipTitanicAp;

internal static class BeamBridgeItem
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "BeamBridge",
        ServerGrantedApItemName = "Red Fuse",
        DefaultParent = new DefaultParent(null, "CViewItem"),
        IsFuseBoxItem = true,
    };
}
