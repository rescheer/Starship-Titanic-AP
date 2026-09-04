namespace StarshipTitanicAp;

/// <summary>Intercepts CGameObject::setPassengerClass() so it records the attempted class instead of applying it.</summary>
public static class ClassUpgradeHook
{
    private const int OriginalBytesLength = 15; // through "mov ebx,edx", clean instruction boundary
    private const int MailboxTotalSize = 1 + 4; // [0]=ready flag, [1..4]=attempted class (int32)

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

        long funcAddr = mem.ModuleBase + GameOffsets.SetPassengerClassFunc;

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

    /// <summary>Checks the mailbox for a newly-blocked upgrade attempt.</summary>
    public static int? PollAttemptedClass(MemoryReader mem)
    {
        if (!_installed)
            return null;

        byte[]? readyByte = mem.ReadBytes(_mailboxAddr, 1);
        if (readyByte is null || readyByte[0] == 0)
            return null;

        byte[]? classBytes = mem.ReadBytes(_mailboxAddr + 1, 4);
        mem.WriteByte(_mailboxAddr, 0); // clear ready flag

        if (classBytes is null)
            return null;

        return BitConverter.ToInt32(classBytes, 0);
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

        // mov dword ptr [r10+1], edx (store the attempted class)
        b.AddRange(new byte[] { 0x41, 0x89, 0x52, 0x01 });

        // mov byte ptr [r10], 1 (ready flag)
        b.AddRange(new byte[] { 0x41, 0xC6, 0x02, 0x01 });

        b.Add(0xC3); // ret - the real function body never runs
        return b.ToArray();
    }
}
