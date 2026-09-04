namespace StarshipTitanicAp;

/// <summary>Canonical list of the carryable item names.</summary>
public static class ItemNames
{
    public static readonly string[] All = Items.All.Select(d => d.Name).ToArray();

    /// <summary>True if the given tree-node name is a carryable item.</summary>
    public static bool IsKnownItemName(string name) => Items.TryGet(name, out _);
}
