namespace StarshipTitanicAp;

/// <summary>Cuts the Barbot's vodka gag short. CBarbot::ActMsg queues 18 ranges when he drinks the vodka and locks
/// the player until the last one ends. Only three matter: _frames[47] (sets _addedVodka and _drunkFlag) and
/// _frames[46] (his line) at the start, which notify, and the final _frames[40], which takes the lock and whose end
/// clears _frameNum. The 15 in between have no flags and only show frames.
///
/// A 5-byte jmp over those 15 calls, from the first one's bounds check straight to the last call's. Each call reloads
/// its own arguments and the _frames pointer from r12, so the final call needs nothing from the skipped ones, and no
/// branch lands inside the skipped range. No saved fields change.
///
/// Applied for the Quick Barbot Freakout option (ClientSession.SyncBarbotVodkaPatch).
///
/// The overwritten instruction is checked byte-for-byte against the expected ScummVM 2.9.1 build before patching.</summary>
public static class BarbotVodkaPatch
{
    // cmp dword ptr [r12+0x1B4], 0x28 - the first skipped call's bounds check on _frames.size().
    private static readonly byte[] ExpectedBytes = { 0x41, 0x83, 0xBC, 0x24, 0xB4, 0x01, 0x00, 0x00, 0x28 };

    private static bool _installed;
    private static long _patchAddr;

    public static bool IsInstalled => _installed;

    public static bool Install(MemoryReader mem)
    {
        if (_installed)
            return true;
        if (!mem.IsAttached)
            return false;

        long patchAddr = mem.ModuleBase + GameOffsets.BarbotVodkaSkipSite;
        byte[]? original = mem.ReadBytes(patchAddr, ExpectedBytes.Length);
        if (original is null || !original.AsSpan().SequenceEqual(ExpectedBytes))
            return false;

        // jmp rel32 to the final call's bounds check. The other 4 overwritten bytes are never reached.
        int rel = checked((int)(GameOffsets.BarbotVodkaSkipTarget - (GameOffsets.BarbotVodkaSkipSite + 5)));
        byte[] jmp = new byte[5];
        jmp[0] = 0xE9;
        BitConverter.GetBytes(rel).CopyTo(jmp, 1);
        if (!mem.WriteBytes(patchAddr, jmp))
            return false;

        _patchAddr = patchAddr;
        _installed = true;
        return true;
    }

    public static bool Uninstall(MemoryReader mem)
    {
        if (!_installed)
            return false;

        bool restored = mem.WriteBytes(_patchAddr, ExpectedBytes.AsSpan(0, 5).ToArray());
        _installed = false;
        _patchAddr = 0;
        return restored;
    }
}
