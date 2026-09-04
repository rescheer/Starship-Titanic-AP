namespace StarshipTitanicAp;

/// <summary>CarryParrot never enters the state machine at all - only its natural pickup is detected.</summary>
internal static class CarryParrotItem
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "CarryParrot",
        DefaultParent = new DefaultParent("PerchedParrot", "CParrot"),
    };
}
