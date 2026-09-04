namespace StarshipTitanicAp;

internal static class FanBridgeItem
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "FanBridge",
        ApItemName = "Blue Fuse",
        PickupLocationName = "Bilge Room - Blue Fuse",
        HomeRnvs = new[] { new RoomNodeView(47, 1, 2) },
        DefaultParent = new DefaultParent("Brobostigon Search Point", "CSearchPoint"),
        IsFuseBoxItem = true,
    };
}
