namespace StarshipTitanicAp;

/// <summary>The AP seed tag stored on a save's BeamBridge item, which <see cref="SaveSeedGuard"/> uses to tell
/// whether a save belongs to the connected seed.</summary>
public static class SaveSeedTag
{
    private const string BeamBridgeItemName = "BeamBridge";

    /// <summary>Deterministic positive tag for a seed_name + slot name pair (so two Starship Titanic slots in the same
    /// seed get different tags): an FNV-1a hash reduced to [1, 16,000,000], an integer a 32-bit float represents
    /// exactly. 0 means "untagged".</summary>
    public static long ComputeSeedTag(string seedName, string slotName)
    {
        ulong hash = 14695981039346656037UL;
        foreach (char c in $"{seedName}\0{slotName}")
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }
        return unchecked((long)(hash % 16_000_000UL) + 1);
    }

    /// <summary>Locates the BeamBridge item via a full carry-item tree walk.</summary>
    public static long? FindBeamBridgeAddress(MemoryReader mem, long project)
    {
        foreach (CarryItemLocation item in GameState.FindAllCarryItems(mem, project))
        {
            if (string.Equals(item.Name, BeamBridgeItemName, StringComparison.OrdinalIgnoreCase))
                return item.Address;
        }
        return null;
    }

    /// <summary>Reads back the tag written by <see cref="WriteSeedTag"/>, decoding it as the double
    /// value it was stored as (not as raw 64-bit bits).</summary>
    public static long? ReadStoredSeedTag(MemoryReader mem, long beamBridgeAddr)
    {
        long? raw = mem.ReadInt64(beamBridgeAddr + GameOffsets.GameObjectUnused3Offset);
        if (raw is null)
            return null;

        double value = BitConverter.Int64BitsToDouble(raw.Value);
        if (double.IsNaN(value) || double.IsInfinity(value))
            return null;

        return (long)Math.Round(value);
    }

    /// <summary>Stores tag as the double value it numerically represents, not as raw 64-bit bits.</summary>
    public static bool WriteSeedTag(MemoryReader mem, long beamBridgeAddr, long tag) =>
        mem.WriteInt64(beamBridgeAddr + GameOffsets.GameObjectUnused3Offset, BitConverter.DoubleToInt64Bits(tag));
}
