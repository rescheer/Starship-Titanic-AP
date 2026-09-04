namespace StarshipTitanicAp;

internal static class AuditoryCentreItem
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "AuditoryCentre",
        PickupLocationName = "1st Class Restaurant - Titania's Auditory Center",
        DefaultParent = new DefaultParent("MaitreD Right Arm", "CMaitreDRightArm"),
        IsOneDirectional = true,
    };
}
