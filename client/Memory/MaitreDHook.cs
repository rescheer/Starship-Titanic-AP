namespace StarshipTitanicAp;

/// <summary>Intercepts CScraliontisTable::MaitreDDefeatedMsg() so defeating the MaitreD records the attempt
/// instead of granting table access directly - table access only ever applies from receiving the "Table Access"
/// item over the multiworld (see GameActions.GrantScraliontisTableAccess), avoiding a softlock where completing
/// the Music Room puzzle first (possible if "Music System Key" is granted before the natural Maitre'D win)
/// permanently blocks further interaction with Maitre'D.</summary>
public static class MaitreDHook
{
    private const int OriginalBytesLength = 15; // mov eax,1 (5) + mov dword ptr [rcx+11C],4 (10) - clean instruction boundary
    private const int MailboxTotalSize = 1; // [0]=ready flag

    private static bool _installed;
    private static long _hookedFuncAddr;
    private static byte[]? _originalBytes;
    private static long _stubAddr;
    private static long _mailboxAddr;

    public static bool IsInstalled => _installed;

    public static bool Install(MemoryReader mem)
    {
        if (_installed)
            return true;
        if (!mem.IsAttached)
            return false;

        long funcAddr = mem.ModuleBase + GameOffsets.MaitreDDefeatedMsgFunc;

        byte[]? original = mem.ReadBytes(funcAddr, OriginalBytesLength);
        if (original is null)
            return false;

        byte[] mailboxInit = new byte[MailboxTotalSize]; // all zero
        long mailboxAddr = RemoteCaller.AllocateAndWrite(mem, mailboxInit);
        if (mailboxAddr == 0)
            return false;

        byte[] stub = BuildStub(mailboxAddr);
        long stubAddr = RemoteCaller.AllocateAndWrite(mem, stub);
        if (stubAddr == 0)
        {
            RemoteCaller.FreeRemoteMemory(mem, mailboxAddr);
            return false;
        }

        // 14-byte far jmp, padded with NOPs to fill OriginalBytesLength.
        byte[] detour = new byte[OriginalBytesLength];
        detour[0] = 0xFF; detour[1] = 0x25; // jmp qword ptr [rip+0]
        detour[2] = 0x00; detour[3] = 0x00; detour[4] = 0x00; detour[5] = 0x00;
        BitConverter.GetBytes(stubAddr).CopyTo(detour, 6); // 8-byte absolute address
        for (int i = 14; i < OriginalBytesLength; i++)
            detour[i] = 0x90;

        if (!mem.WriteBytes(funcAddr, detour))
        {
            RemoteCaller.FreeRemoteMemory(mem, mailboxAddr);
            RemoteCaller.FreeRemoteMemory(mem, stubAddr);
            return false;
        }

        _installed = true;
        _hookedFuncAddr = funcAddr;
        _originalBytes = original;
        _stubAddr = stubAddr;
        _mailboxAddr = mailboxAddr;
        return true;
    }

    public static bool Uninstall(MemoryReader mem)
    {
        if (!_installed || _originalBytes is null)
            return false;

        bool restored = mem.WriteBytes(_hookedFuncAddr, _originalBytes);
        RemoteCaller.FreeRemoteMemory(mem, _stubAddr);
        RemoteCaller.FreeRemoteMemory(mem, _mailboxAddr);

        _installed = false;
        _originalBytes = null;
        _hookedFuncAddr = 0;
        _stubAddr = 0;
        _mailboxAddr = 0;
        return restored;
    }

    /// <summary>Checks the mailbox for a newly-blocked Maitre'D win.</summary>
    public static bool PollDefeated(MemoryReader mem)
    {
        if (!_installed)
            return false;

        byte[]? readyByte = mem.ReadBytes(_mailboxAddr, 1);
        if (readyByte is null || readyByte[0] == 0)
            return false;

        mem.WriteByte(_mailboxAddr, 0); // clear ready flag
        return true;
    }

    // ------------------------------------------------------------------
    // Stub construction
    // ------------------------------------------------------------------

    private static byte[] BuildStub(long mailboxAddr)
    {
        var b = new List<byte>();

        // mov r10, imm64 (mailbox address)
        b.AddRange(new byte[] { 0x49, 0xBA });
        b.AddRange(BitConverter.GetBytes(mailboxAddr));

        // mov byte ptr [r10], 1 (ready flag)
        b.AddRange(new byte[] { 0x41, 0xC6, 0x02, 0x01 });

        // mov eax, 1 (preserve the original function's return value - true/handled)
        b.AddRange(new byte[] { 0xB8, 0x01, 0x00, 0x00, 0x00 });

        b.Add(0xC3); // ret - the real field writes never run
        return b.ToArray();
    }
}
