namespace StarshipTitanicAp;

/// <summary>Makes the DeskBot's conversation see a chosen passenger class without touching the real one. Every class
/// read in DeskbotScript, and in the TTnpcScript code it shares, goes through TTnpcScript::getValue(1)
/// (GameOffsets.NpcScriptGetValueFunc); the only other inlined reads of it are the German-only sentence tests and
/// addRandomResponse, which DeskbotScript never calls. The DeskBot object's own getPassengerClass() calls (the Magazine
/// gate, TrueTalk actions 20-23) still see the real class.
///
/// Detours getValue's entry, the only point where `this` (rcx) is still the script - the prologue overwrites rcx with
/// its jump table. When the remote block's enabled byte is set, testNum (edx) is 1 and `this`'s vtable is
/// DeskbotScript's (GameOffsets.DeskbotScriptVtable), returns the block's class. Otherwise replays the 20-byte prologue,
/// with its `ja rel32` and RIP-relative `lea` rewritten to absolute addresses, and jumps back. The spoofed path skips
/// the CTrueTalkManager::_v3 write the real one makes, so the spoofed value never reaches a save.
///
/// Installed for seeds with item-gated class upgrades (ClientSession.SyncDeskBotClassHook); ClassSync sets the
/// block every tick. The prologue is checked byte-for-byte against the expected ScummVM 2.9.1 build before patching.</summary>
public static class DeskBotClassHook
{
    // sub rsp,0x28 / cmp edx,7 / ja rel32 / lea rcx,[rip+disp32]
    private static readonly byte[] ExpectedPrologue =
    {
        0x48, 0x83, 0xEC, 0x28,
        0x83, 0xFA, 0x07,
        0x0F, 0x87, 0x22, 0x01, 0x00, 0x00,
        0x48, 0x8D, 0x0D, 0xC8, 0x79, 0xAC, 0x02,
    };
    private const int JaRel32Offset = 9;
    private const int JaEnd = 13;
    private const int LeaDisp32Offset = 16;
    private const int PrologueLength = 20;

    private const int BlockTotalSize = 1 + 4; // [0] = enabled, [1..4] = class to report (int32)

    private static bool _installed;
    private static long _hookedFuncAddr;
    private static long _stubAddr;
    private static long _blockAddr;
    private static int? _reportedClass;

    public static bool IsInstalled => _installed;

    /// <summary>The class the DeskBot's conversation is being shown, or null for the real one.</summary>
    public static int? ReportedClass => _reportedClass;

    public static bool Install(MemoryReader mem)
    {
        if (_installed)
            return true;
        if (!mem.IsAttached)
            return false;

        long funcAddr = mem.ModuleBase + GameOffsets.NpcScriptGetValueFunc;
        byte[]? original = mem.ReadBytes(funcAddr, PrologueLength);
        if (original is null || !original.AsSpan().SequenceEqual(ExpectedPrologue))
            return false;

        long jaTarget = funcAddr + JaEnd + BitConverter.ToInt32(original, JaRel32Offset);
        long jumpTable = funcAddr + PrologueLength + BitConverter.ToInt32(original, LeaDisp32Offset);
        long vtable = mem.ModuleBase + GameOffsets.DeskbotScriptVtable;

        long blockAddr = RemoteCaller.AllocateAndWrite(mem, new byte[BlockTotalSize]); // starts disabled
        if (blockAddr == 0)
            return false;

        byte[] stub = BuildStub(blockAddr, vtable, jaTarget, jumpTable, funcAddr + PrologueLength);
        long stubAddr = RemoteCaller.AllocateAndWrite(mem, stub);
        if (stubAddr == 0)
        {
            RemoteCaller.FreeRemoteMemory(mem, blockAddr);
            return false;
        }

        // 14-byte far jmp, padded with NOPs to the end of the replayed prologue. Nothing jumps into those bytes.
        byte[] detour = new byte[PrologueLength];
        detour[0] = 0xFF; detour[1] = 0x25; // jmp qword ptr [rip+0]
        BitConverter.GetBytes(stubAddr).CopyTo(detour, 6);
        for (int i = 14; i < PrologueLength; i++)
            detour[i] = 0x90;

        if (!mem.WriteBytes(funcAddr, detour))
        {
            RemoteCaller.FreeRemoteMemory(mem, blockAddr);
            RemoteCaller.FreeRemoteMemory(mem, stubAddr);
            return false;
        }

        _installed = true;
        _hookedFuncAddr = funcAddr;
        _stubAddr = stubAddr;
        _blockAddr = blockAddr;
        _reportedClass = null;
        return true;
    }

    public static bool Uninstall(MemoryReader mem)
    {
        if (!_installed)
            return false;

        bool restored = mem.WriteBytes(_hookedFuncAddr, ExpectedPrologue);
        RemoteCaller.FreeRemoteMemory(mem, _stubAddr);
        RemoteCaller.FreeRemoteMemory(mem, _blockAddr);

        _installed = false;
        _hookedFuncAddr = 0;
        _stubAddr = 0;
        _blockAddr = 0;
        _reportedClass = null;
        return restored;
    }

    /// <summary>Sets the class the DeskBot's conversation sees, or null for the real one. Only writes on a change.</summary>
    public static bool SetReportedClass(MemoryReader mem, int? passengerClass)
    {
        if (!_installed)
            return false;
        if (passengerClass == _reportedClass)
            return true;

        bool ok = passengerClass is { } value
            ? mem.WriteInt32(_blockAddr + 1, value) && mem.WriteByte(_blockAddr, 1)
            : mem.WriteByte(_blockAddr, 0);
        if (ok)
            _reportedClass = passengerClass;
        return ok;
    }

    // ------------------------------------------------------------------
    // Stub construction
    // ------------------------------------------------------------------

    private static byte[] BuildStub(long blockAddr, long vtable, long jaTarget, long jumpTable, long resumeAddr)
    {
        var b = new List<byte>();
        var toOriginal = new List<int>(); // rel8 operands that jump to the replayed prologue

        // cmp edx, 1
        b.AddRange(new byte[] { 0x83, 0xFA, 0x01 });
        // jne original
        b.AddRange(new byte[] { 0x75, 0x00 }); toOriginal.Add(b.Count - 1);
        // mov r10, imm64 (block)
        b.AddRange(new byte[] { 0x49, 0xBA }); b.AddRange(BitConverter.GetBytes(blockAddr));
        // cmp byte ptr [r10], 0
        b.AddRange(new byte[] { 0x41, 0x80, 0x3A, 0x00 });
        // je original
        b.AddRange(new byte[] { 0x74, 0x00 }); toOriginal.Add(b.Count - 1);
        // mov rax, [rcx] (this's vtable)
        b.AddRange(new byte[] { 0x48, 0x8B, 0x01 });
        // mov r11, imm64 (DeskbotScript vtable)
        b.AddRange(new byte[] { 0x49, 0xBB }); b.AddRange(BitConverter.GetBytes(vtable));
        // cmp rax, r11
        b.AddRange(new byte[] { 0x4C, 0x39, 0xD8 });
        // jne original
        b.AddRange(new byte[] { 0x75, 0x00 }); toOriginal.Add(b.Count - 1);
        // mov eax, [r10+1]
        b.AddRange(new byte[] { 0x41, 0x8B, 0x42, 0x01 });
        // ret
        b.Add(0xC3);

        int original = b.Count;
        foreach (int operand in toOriginal)
            b[operand] = checked((byte)(sbyte)(original - (operand + 1)));

        // The replayed prologue: sub rsp,0x28 / cmp edx,7
        b.AddRange(new byte[] { 0x48, 0x83, 0xEC, 0x28, 0x83, 0xFA, 0x07 });
        // ja default (rel8, to the absolute jmp below)
        b.AddRange(new byte[] { 0x77, 0x00 });
        int jaOperand = b.Count - 1;
        // mov rcx, imm64 (jump table) in place of the RIP-relative lea
        b.AddRange(new byte[] { 0x48, 0xB9 }); b.AddRange(BitConverter.GetBytes(jumpTable));
        // jmp qword ptr [rip+0] -> resume
        b.AddRange(new byte[] { 0xFF, 0x25, 0x00, 0x00, 0x00, 0x00 }); b.AddRange(BitConverter.GetBytes(resumeAddr));

        b[jaOperand] = checked((byte)(sbyte)(b.Count - (jaOperand + 1)));
        // default: jmp qword ptr [rip+0] -> the out-of-range testNum path
        b.AddRange(new byte[] { 0xFF, 0x25, 0x00, 0x00, 0x00, 0x00 }); b.AddRange(BitConverter.GetBytes(jaTarget));

        return b.ToArray();
    }
}
