namespace StarshipTitanicAp;

/// <summary>Intercepts CTrueTalkManager::setFlags() so the Maitre'D's "pacified" flag (index 8) can be kept clear
/// until he's defeated. The correct music sets it (MaitreDScript event 110), and while it's set prodding and fighting
/// him do nothing - vanilla needs the win before the music can be fixed, but here the music can come first.
///
/// While diverting, setFlags(8, val) stores val in the unused slot 0 of the same array instead (no script sets it,
/// and getValue(0) reads another static), so the value the game meant to give the flag is kept, and saved with the
/// game. Otherwise the stub replays the original 14 bytes. Those use rip-relative addressing, so they're rebuilt with
/// absolute addresses.</summary>
public static class MaitreDPacifiedHook
{
    private const int OriginalBytesLength = 14; // cmp ecx,6 / ja default / lea r8,[jump table] / mov eax,ecx
    private static readonly byte[] ExpectedOriginal =
        { 0x83, 0xF9, 0x06, 0x77, 0x72, 0x4C, 0x8D, 0x05, 0x88, 0xA8, 0xAC, 0x02, 0x89, 0xC8 };
    private const int GateTotalSize = 1; // [0] = 1 to divert writes to flag 8 into slot 0

    private static bool _installed;
    private static long _hookedFuncAddr;
    private static byte[]? _originalBytes;
    private static long _stubAddr;
    private static long _gateAddr;

    public static bool IsInstalled => _installed;

    public static bool Install(MemoryReader mem)
    {
        if (_installed)
            return true;
        if (!mem.IsAttached)
            return false;

        long funcAddr = mem.ModuleBase + GameOffsets.TrueTalkSetFlagsFunc;

        byte[]? original = mem.ReadBytes(funcAddr, OriginalBytesLength);
        if (original is null || !original.AsSpan().SequenceEqual(ExpectedOriginal))
            return false;

        long gateAddr = RemoteCaller.AllocateAndWrite(mem, new byte[GateTotalSize]); // starts not diverting (0)
        if (gateAddr == 0)
            return false;

        byte[] stub = BuildStub(gateAddr, mem.ModuleBase);
        long stubAddr = RemoteCaller.AllocateAndWrite(mem, stub);
        if (stubAddr == 0)
        {
            RemoteCaller.FreeRemoteMemory(mem, gateAddr);
            return false;
        }

        // 14-byte far jmp, exactly OriginalBytesLength.
        byte[] detour = new byte[OriginalBytesLength];
        detour[0] = 0xFF; detour[1] = 0x25; // jmp qword ptr [rip+0]
        BitConverter.GetBytes(stubAddr).CopyTo(detour, 6); // 8-byte absolute address

        if (!mem.WriteBytes(funcAddr, detour))
        {
            RemoteCaller.FreeRemoteMemory(mem, gateAddr);
            RemoteCaller.FreeRemoteMemory(mem, stubAddr);
            return false;
        }

        _installed = true;
        _hookedFuncAddr = funcAddr;
        _originalBytes = original;
        _stubAddr = stubAddr;
        _gateAddr = gateAddr;
        return true;
    }

    public static bool Uninstall(MemoryReader mem)
    {
        if (!_installed || _originalBytes is null)
            return false;

        bool restored = mem.WriteBytes(_hookedFuncAddr, _originalBytes);
        RemoteCaller.FreeRemoteMemory(mem, _stubAddr);
        RemoteCaller.FreeRemoteMemory(mem, _gateAddr);

        _installed = false;
        _originalBytes = null;
        _hookedFuncAddr = 0;
        _stubAddr = 0;
        _gateAddr = 0;
        return restored;
    }

    /// <summary>Sets whether writes to flag 8 go to slot 0 - call every reconcile tick.</summary>
    public static bool SetDiverting(MemoryReader mem, bool diverting)
    {
        if (!_installed)
            return false;
        return mem.WriteByte(_gateAddr, (byte)(diverting ? 1 : 0));
    }

    // ------------------------------------------------------------------
    // Stub construction
    // ------------------------------------------------------------------

    private static byte[] BuildStub(long gateAddr, long moduleBase)
    {
        var b = new List<byte>();

        // cmp ecx, 8 / jne original
        b.AddRange(new byte[] { 0x83, 0xF9, GameOffsets.MaitreDPacifiedFlagIndex });
        b.AddRange(new byte[] { 0x75, 0x00 });
        int jneOriginal = b.Count - 1;

        // mov r10, imm64 (gate address) / cmp byte ptr [r10], 0 / je original
        b.AddRange(new byte[] { 0x49, 0xBA });
        b.AddRange(BitConverter.GetBytes(gateAddr));
        b.AddRange(new byte[] { 0x41, 0x80, 0x3A, 0x00 });
        b.AddRange(new byte[] { 0x74, 0x00 });
        int jeOriginal = b.Count - 1;

        // mov r10, imm64 (&_v11[0]) / mov dword ptr [r10], edx / ret
        b.AddRange(new byte[] { 0x49, 0xBA });
        b.AddRange(BitConverter.GetBytes(moduleBase + GameOffsets.TrueTalkFlagsStatic + GameOffsets.MaitreDPacifiedShadowIndex * 4));
        b.AddRange(new byte[] { 0x41, 0x89, 0x12 });
        b.Add(0xC3);

        int original = b.Count;
        b[jneOriginal] = (byte)(original - (jneOriginal + 1));
        b[jeOriginal] = (byte)(original - (jeOriginal + 1));

        // cmp ecx, 6 / jbe (over the far jmp) / jmp qword ptr [rip+0] -> the default case
        b.AddRange(new byte[] { 0x83, 0xF9, 0x06 });
        b.AddRange(new byte[] { 0x76, 0x0E });
        b.AddRange(new byte[] { 0xFF, 0x25, 0x00, 0x00, 0x00, 0x00 });
        b.AddRange(BitConverter.GetBytes(moduleBase + GameOffsets.TrueTalkSetFlagsDefaultCase));

        // mov r8, imm64 (jump table) / mov eax, ecx / jmp qword ptr [rip+0] -> resume
        b.AddRange(new byte[] { 0x49, 0xB8 });
        b.AddRange(BitConverter.GetBytes(moduleBase + GameOffsets.TrueTalkSetFlagsJumpTable));
        b.AddRange(new byte[] { 0x89, 0xC8 });
        b.AddRange(new byte[] { 0xFF, 0x25, 0x00, 0x00, 0x00, 0x00 });
        b.AddRange(BitConverter.GetBytes(moduleBase + GameOffsets.TrueTalkSetFlagsFunc + OriginalBytesLength));

        return b.ToArray();
    }
}
