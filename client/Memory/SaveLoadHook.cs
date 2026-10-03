namespace StarshipTitanicAp;

/// <summary>Detects in-process save loads. CProjectItem::loadGame keeps the same CProjectItem/CGameManager and
/// only swaps out the tree under them, so a changed _project pointer can't be used to notice a load - every
/// cached tree address (PET control, MailMan, glyphs, items) silently dangles instead.
///
/// Two pass-through detours, each just bumping a counter in a remote mailbox then running the real function:
///   loadGame entry  -> [mailbox+0] "loads started"
///   postLoad entry  -> [mailbox+4] "loads finished" (the new tree is fully re-parented by this point)
/// started > finished means a load is mid-flight and the tree must not be touched; a change in finished means
/// every cached address must be dropped and re-resolved.
///
/// Both stolen prologues are checked byte-for-byte against the expected ScummVM 2.9.1 build before patching,
/// since they're replayed (not just skipped) - a different build would otherwise crash the game.</summary>
public static class SaveLoadHook
{
    // loadGame: push r15/r14/r13/r12/rbp/rdi/rsi/rbx; sub rsp,0xC8 - no RIP-relative operands, replayed verbatim.
    private static readonly byte[] LoadGameExpectedBytes =
    {
        0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54, 0x55, 0x57, 0x56, 0x53,
        0x48, 0x81, 0xEC, 0xC8, 0x00, 0x00, 0x00,
    };

    // postLoad: push r12; sub rsp,0x20; lea rdx,[rip-0xCFD]; mov rax,[rcx] - the lea is rebuilt as an absolute
    // mov rdx,imm64 (see GameOffsets.ProjectPostLoadLeaTarget).
    private static readonly byte[] PostLoadExpectedBytes =
    {
        0x41, 0x54, 0x48, 0x83, 0xEC, 0x20, 0x48, 0x8D, 0x15, 0x03, 0xF3, 0xFF, 0xFF, 0x48, 0x8B, 0x01,
    };

    private const int MailboxTotalSize = 8; // [0..3]=loads started (int32), [4..7]=loads finished (int32)

    private static bool _installed;
    private static long _loadGameAddr;
    private static long _postLoadAddr;
    private static long _loadGameStubAddr;
    private static long _postLoadStubAddr;
    private static long _mailboxAddr;
    private static int _lastSeenFinished;

    public static bool IsInstalled => _installed;

    public static bool Install(MemoryReader mem)
    {
        if (_installed)
            return true;
        if (!mem.IsAttached)
            return false;

        long loadGameAddr = mem.ModuleBase + GameOffsets.ProjectLoadGameFunc;
        long postLoadAddr = mem.ModuleBase + GameOffsets.ProjectPostLoadFunc;

        byte[]? loadGameOriginal = mem.ReadBytes(loadGameAddr, LoadGameExpectedBytes.Length);
        byte[]? postLoadOriginal = mem.ReadBytes(postLoadAddr, PostLoadExpectedBytes.Length);
        if (loadGameOriginal is null || !loadGameOriginal.AsSpan().SequenceEqual(LoadGameExpectedBytes))
            return false;
        if (postLoadOriginal is null || !postLoadOriginal.AsSpan().SequenceEqual(PostLoadExpectedBytes))
            return false;

        long mailboxAddr = RemoteCaller.AllocateAndWrite(mem, new byte[MailboxTotalSize]);
        if (mailboxAddr == 0)
            return false;

        long loadGameStubAddr = RemoteCaller.AllocateAndWrite(mem, BuildLoadGameStub(mailboxAddr, loadGameAddr));
        long postLoadStubAddr = RemoteCaller.AllocateAndWrite(mem,
            BuildPostLoadStub(mailboxAddr, postLoadAddr, mem.ModuleBase + GameOffsets.ProjectPostLoadLeaTarget));
        if (loadGameStubAddr == 0 || postLoadStubAddr == 0)
        {
            RemoteCaller.FreeRemoteMemory(mem, mailboxAddr);
            RemoteCaller.FreeRemoteMemory(mem, loadGameStubAddr);
            RemoteCaller.FreeRemoteMemory(mem, postLoadStubAddr);
            return false;
        }

        if (!mem.WriteBytes(loadGameAddr, BuildDetour(loadGameStubAddr, LoadGameExpectedBytes.Length)))
        {
            RemoteCaller.FreeRemoteMemory(mem, mailboxAddr);
            RemoteCaller.FreeRemoteMemory(mem, loadGameStubAddr);
            RemoteCaller.FreeRemoteMemory(mem, postLoadStubAddr);
            return false;
        }
        if (!mem.WriteBytes(postLoadAddr, BuildDetour(postLoadStubAddr, PostLoadExpectedBytes.Length)))
        {
            mem.WriteBytes(loadGameAddr, LoadGameExpectedBytes);
            RemoteCaller.FreeRemoteMemory(mem, mailboxAddr);
            RemoteCaller.FreeRemoteMemory(mem, loadGameStubAddr);
            RemoteCaller.FreeRemoteMemory(mem, postLoadStubAddr);
            return false;
        }

        _installed = true;
        _loadGameAddr = loadGameAddr;
        _postLoadAddr = postLoadAddr;
        _loadGameStubAddr = loadGameStubAddr;
        _postLoadStubAddr = postLoadStubAddr;
        _mailboxAddr = mailboxAddr;
        _lastSeenFinished = 0;
        return true;
    }

    public static bool Uninstall(MemoryReader mem)
    {
        if (!_installed)
            return false;

        bool restored = mem.WriteBytes(_loadGameAddr, LoadGameExpectedBytes);
        restored &= mem.WriteBytes(_postLoadAddr, PostLoadExpectedBytes);
        RemoteCaller.FreeRemoteMemory(mem, _loadGameStubAddr);
        RemoteCaller.FreeRemoteMemory(mem, _postLoadStubAddr);
        RemoteCaller.FreeRemoteMemory(mem, _mailboxAddr);

        _installed = false;
        _loadGameAddr = 0;
        _postLoadAddr = 0;
        _loadGameStubAddr = 0;
        _postLoadStubAddr = 0;
        _mailboxAddr = 0;
        _lastSeenFinished = 0;
        return restored;
    }

    public enum LoadState { Idle, Loading, JustLoaded }

    /// <summary>Loading while a load has started but not reached postLoad; JustLoaded exactly once per completed
    /// load. A load already in flight when the hook was installed only bumps "finished", so started &lt; finished
    /// is treated as not loading rather than stuck.</summary>
    public static LoadState Poll(MemoryReader mem)
    {
        if (!_installed)
            return LoadState.Idle;

        byte[]? raw = mem.ReadBytes(_mailboxAddr, MailboxTotalSize);
        if (raw is null)
            return LoadState.Idle;

        int started = BitConverter.ToInt32(raw, 0);
        int finished = BitConverter.ToInt32(raw, 4);

        if (finished != _lastSeenFinished)
        {
            _lastSeenFinished = finished;
            return LoadState.JustLoaded;
        }
        return started > finished ? LoadState.Loading : LoadState.Idle;
    }

    // ------------------------------------------------------------------
    // Stub construction
    // ------------------------------------------------------------------

    /// <summary>14-byte absolute far jmp, NOP-padded to the stolen length.</summary>
    private static byte[] BuildDetour(long stubAddr, int length)
    {
        byte[] detour = new byte[length];
        detour[0] = 0xFF; detour[1] = 0x25; // jmp qword ptr [rip+0]
        BitConverter.GetBytes(stubAddr).CopyTo(detour, 6);
        for (int i = 14; i < length; i++)
            detour[i] = 0x90;
        return detour;
    }

    private static void AppendCounterIncrement(List<byte> b, long counterAddr)
    {
        // mov r10, imm64 (counter address) - r10 is volatile and unused at both function entries
        b.AddRange(new byte[] { 0x49, 0xBA });
        b.AddRange(BitConverter.GetBytes(counterAddr));
        // inc dword ptr [r10]
        b.AddRange(new byte[] { 0x41, 0xFF, 0x02 });
    }

    private static void AppendJmpAbsolute(List<byte> b, long target)
    {
        b.AddRange(new byte[] { 0xFF, 0x25, 0x00, 0x00, 0x00, 0x00 }); // jmp qword ptr [rip+0]
        b.AddRange(BitConverter.GetBytes(target));
    }

    private static byte[] BuildLoadGameStub(long mailboxAddr, long loadGameAddr)
    {
        var b = new List<byte>();
        AppendCounterIncrement(b, mailboxAddr);
        b.AddRange(LoadGameExpectedBytes); // replay the real prologue
        AppendJmpAbsolute(b, loadGameAddr + LoadGameExpectedBytes.Length);
        return b.ToArray();
    }

    private static byte[] BuildPostLoadStub(long mailboxAddr, long postLoadAddr, long leaTarget)
    {
        var b = new List<byte>();
        AppendCounterIncrement(b, mailboxAddr + 4);

        b.AddRange(new byte[] { 0x41, 0x54 });             // push r12
        b.AddRange(new byte[] { 0x48, 0x83, 0xEC, 0x20 }); // sub rsp, 0x20
        b.AddRange(new byte[] { 0x48, 0xBA });             // mov rdx, imm64 (was lea rdx,[rip-0xCFD])
        b.AddRange(BitConverter.GetBytes(leaTarget));
        b.AddRange(new byte[] { 0x48, 0x8B, 0x01 });       // mov rax, [rcx]

        AppendJmpAbsolute(b, postLoadAddr + PostLoadExpectedBytes.Length);
        return b.ToArray();
    }
}
