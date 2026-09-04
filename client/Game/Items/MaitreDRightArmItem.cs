namespace StarshipTitanicAp;

internal static class MaitreDRightArmItem
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "MaitreD Right Arm",
        ApItemName = "Maitre'D Bot's Right Arm",
        PickupLocationName = "1st Class Restaurant - Maitre'D Bot's Right Arm",
        HomeRnvs = new[] { new RoomNodeView(49, 3, 2) },
        DefaultParent = new DefaultParent("MaitreD Arm Holder", "CMaitreDArmHolder"),
    };
}
