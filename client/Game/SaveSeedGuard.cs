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

    /// <summary>Deterministic small positive tag for a seed_name.
    /// BeamBridge's _unused3 field is a double in memory, but the game's save format writes it as
    /// text via "%f" (6 fixed decimal places) and reads it back with sscanf("%f", &amp;floatValue) into a
    /// 32-bit float (see CGameObject::save/load). A raw 64-bit hash reinterpreted as a double would be
    /// mangled or destroyed by that round trip, so the tag must be a plain integer small enough for a
    /// 32-bit float to represent exactly (&lt;= 2^24) - anything larger risks precision loss across a
    /// save/load. 0 is reserved to mean "untagged", so the range is offset by 1.</summary>
    public static long ComputeSeedTag(string seedName)
    {
        ulong hash = 14695981039346656037UL;
        foreach (char c in seedName)
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
    /// value it was stored as (not as raw 64-bit bits) - see <see cref="ComputeSeedTag"/>.</summary>
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

    /// <summary>Stores tag as the double value it numerically represents, not as raw 64-bit bits -
    /// see <see cref="ComputeSeedTag"/> for why that distinction matters.</summary>
    public static bool WriteSeedTag(MemoryReader mem, long beamBridgeAddr, long tag) =>
        mem.WriteInt64(beamBridgeAddr + GameOffsets.GameObjectUnused3Offset, BitConverter.DoubleToInt64Bits(tag));
}
