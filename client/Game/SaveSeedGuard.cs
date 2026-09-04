namespace StarshipTitanicAp;

public enum SaveSeedGuardState
{
    /// <summary>Not enough information yet - treated as "don't act" by callers, same as Blocked.</summary>
    Unverified,
    Ok,
    Blocked,
}

/// <summary>Guards against attaching to a save file that belongs to a different Archipelago seed, or that was played without the client.</summary>
public static class SaveSeedGuard
{
    private const string BeamBridgeItemName = "BeamBridge";

    /// <summary>Deterministic 64-bit tag for a seed_name.</summary>
    public static long ComputeSeedTag(string seedName)
    {
        ulong hash = 14695981039346656037UL;
        foreach (char c in seedName)
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }
        return unchecked((long)hash);
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

    public static long? ReadStoredSeedTag(MemoryReader mem, long beamBridgeAddr) =>
        mem.ReadInt64(beamBridgeAddr + GameOffsets.GameObjectUnused3Offset);

    public static bool WriteSeedTag(MemoryReader mem, long beamBridgeAddr, long tag) =>
        mem.WriteInt64(beamBridgeAddr + GameOffsets.GameObjectUnused3Offset, tag);
}
