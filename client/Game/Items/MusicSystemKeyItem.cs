namespace StarshipTitanicAp;

internal static class MusicSystemKeyItem
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "Music System Key",
        DefaultParent = new DefaultParent("MaitreD Left Arm", "CMaitreDLeftArm"),
        IsOneDirectional = true,
    };
}
