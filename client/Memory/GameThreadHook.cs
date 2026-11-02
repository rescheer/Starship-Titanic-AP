namespace StarshipTitanicAp;

/// <summary>Runs the client's calls into the game on the game's own thread, between frames. A remote thread would run
/// alongside the game thread, and neither the engine nor its objects are thread-safe: a PET highlight on a remote
/// thread and that glyph's draw on the game thread both lazily loading its movie is enough to crash.
///
/// A detour on Events::pollEvents counts a heartbeat, and runs the call posted in the mailbox if there is one. It's
/// the outermost point of the game's loop and every nested one (cutscenes included), reached before that frame's
/// events and drawing.
///
/// The stolen prologue is checked byte-for-byte against the expected ScummVM 2.9.1 build before patching.</summary>
public static class GameThreadHook
{
    // push r13/r12/rbp/rdi/rsi/rbx; sub rsp,0x78; xor r12d,r12d - no RIP-relative operands, replayed verbatim.
    private static readonly byte[] PollEventsExpectedBytes =
    {
        0x41, 0x55, 0x41, 0x54, 0x55, 0x57, 0x56, 0x53, 0x48, 0x83, 0xEC, 0x78, 0x45, 0x31, 0xE4,
    };

    private const int StateIdle = 0;
    private const int StateRequested = 1;
    private const int StateRunning = 2;
    private const int StateDone = 3;

    private const int MailboxState = 0x00;
    private const int MailboxHeartbeat = 0x04;
    private const int MailboxFunc = 0x08;
    private const int MailboxRcx = 0x10;
    private const int MailboxRdx = 0x18;
    private const int MailboxR8 = 0x20;
    private const int MailboxR9d = 0x28;
    private const int MailboxArg5 = 0x30;
    private const int MailboxArg6 = 0x34;
    private const int MailboxResult = 0x38;
    private const int MailboxTotalSize = 0x40;

    /// <summary>How long the heartbeat can stand still before the game counts as not running its loop (ScummVM's
    /// menu, the launcher). Long enough that a slow frame, such as a room change, doesn't count.</summary>
    private const long PumpStaleMs = 1000;

    private static readonly object CallLock = new();

    private static bool _installed;
    private static long _pollEventsAddr;
    private static long _stubAddr;
    private static long _mailboxAddr;
    private static int _lastHeartbeat;
    private static long _lastHeartbeatChangeMs;

    public static bool IsInstalled => _installed;

    public static bool Install(MemoryReader mem)
    {
        if (_installed)
            return true;
        if (!mem.IsAttached)
            return false;

        long pollEventsAddr = mem.ModuleBase + GameOffsets.EventsPollEventsFunc;
        byte[]? original = mem.ReadBytes(pollEventsAddr, PollEventsExpectedBytes.Length);
        if (original is null || !original.AsSpan().SequenceEqual(PollEventsExpectedBytes))
            return false;

        long mailboxAddr = RemoteCaller.AllocateAndWrite(mem, new byte[MailboxTotalSize]);
        if (mailboxAddr == 0)
            return false;

        long stubAddr = RemoteCaller.AllocateAndWrite(mem, BuildStub(mailboxAddr, pollEventsAddr));
        if (stubAddr == 0)
        {
            RemoteCaller.FreeRemoteMemory(mem, mailboxAddr);
            return false;
        }

        if (!mem.WriteBytes(pollEventsAddr, BuildDetour(stubAddr, PollEventsExpectedBytes.Length)))
        {
            RemoteCaller.FreeRemoteMemory(mem, mailboxAddr);
            RemoteCaller.FreeRemoteMemory(mem, stubAddr);
            return false;
        }

        _pollEventsAddr = pollEventsAddr;
        _stubAddr = stubAddr;
        _mailboxAddr = mailboxAddr;
        _lastHeartbeat = 0;
        _lastHeartbeatChangeMs = Environment.TickCount64;
        _installed = true;
        return true;
    }

    public static bool Uninstall(MemoryReader mem)
    {
        if (!_installed)
            return false;

        lock (CallLock)
        {
            bool restored = mem.WriteBytes(_pollEventsAddr, PollEventsExpectedBytes);
            RemoteCaller.FreeRemoteMemory(mem, _stubAddr);
            RemoteCaller.FreeRemoteMemory(mem, _mailboxAddr);

            _installed = false;
            _pollEventsAddr = 0;
            _stubAddr = 0;
            _mailboxAddr = 0;
            return restored;
        }
    }

    /// <summary>Whether the game is running its loop, so a posted call would run soon. Always true without the hook,
    /// since calls then don't need the loop.</summary>
    public static bool IsPumping(MemoryReader mem)
    {
        if (!_installed)
            return true;

        lock (CallLock)
            return IsPumpingLocked(mem);
    }

    private static bool IsPumpingLocked(MemoryReader mem)
    {
        long now = Environment.TickCount64;
        if (mem.ReadInt32(_mailboxAddr + MailboxHeartbeat) is { } heartbeat && heartbeat != _lastHeartbeat)
        {
            _lastHeartbeat = heartbeat;
            _lastHeartbeatChangeMs = now;
        }
        return now - _lastHeartbeatChangeMs < PumpStaleMs;
    }

    /// <summary>Runs a function on the game thread and waits for it, returning its rax. Null, without having run it,
    /// if the game isn't running its loop.</summary>
    public static long? Call(MemoryReader mem, long funcAddr, long rcx, long rdx, long r8, int r9d, int arg5, int arg6)
    {
        lock (CallLock)
        {
            if (!_installed || !IsPumpingLocked(mem))
                return null;

            byte[] request = new byte[MailboxResult - MailboxFunc];
            BitConverter.GetBytes(funcAddr).CopyTo(request, MailboxFunc - MailboxFunc);
            BitConverter.GetBytes(rcx).CopyTo(request, MailboxRcx - MailboxFunc);
            BitConverter.GetBytes(rdx).CopyTo(request, MailboxRdx - MailboxFunc);
            BitConverter.GetBytes(r8).CopyTo(request, MailboxR8 - MailboxFunc);
            BitConverter.GetBytes(r9d).CopyTo(request, MailboxR9d - MailboxFunc);
            BitConverter.GetBytes(arg5).CopyTo(request, MailboxArg5 - MailboxFunc);
            BitConverter.GetBytes(arg6).CopyTo(request, MailboxArg6 - MailboxFunc);

            if (!mem.WriteBytes(_mailboxAddr + MailboxFunc, request)
                || !mem.WriteInt32(_mailboxAddr + MailboxState, StateRequested))
                return null;

            while (true)
            {
                if (mem.HasProcessExited() || mem.ReadInt32(_mailboxAddr + MailboxState) is not { } state)
                    return null;

                if (state == StateDone)
                {
                    long? result = mem.ReadInt64(_mailboxAddr + MailboxResult);
                    mem.WriteInt32(_mailboxAddr + MailboxState, StateIdle);
                    return result;
                }

                // Withdrawn so it can't run later against a stale address. A running call is waited for however long
                // it takes, since the game thread is the one running it.
                if (state == StateRequested && !IsPumpingLocked(mem))
                {
                    mem.WriteInt32(_mailboxAddr + MailboxState, StateIdle);
                    return null;
                }

                Thread.Sleep(1);
            }
        }
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

    private static byte[] BuildStub(long mailboxAddr, long pollEventsAddr)
    {
        var run = new List<byte>();
        run.AddRange(new byte[] { 0x41, 0xC7, 0x02 }); // mov dword ptr [r10], StateRunning
        run.AddRange(BitConverter.GetBytes(StateRunning));
        run.Add(0x51);                                 // push rcx (Events* for the real pollEvents)
        run.Add(0x55);                                 // push rbp
        run.AddRange(new byte[] { 0x48, 0x89, 0xE5 }); // mov rbp, rsp
        run.AddRange(new byte[] { 0x48, 0x83, 0xE4, 0xF0 }); // and rsp, -16
        run.AddRange(new byte[] { 0x48, 0x83, 0xEC, 0x40 }); // sub rsp, 0x40
        run.AddRange(new byte[] { 0x41, 0x8B, 0x42, MailboxArg5 }); // mov eax, [r10+arg5]
        run.AddRange(new byte[] { 0x89, 0x44, 0x24, 0x20 });        // mov [rsp+0x20], eax
        run.AddRange(new byte[] { 0x41, 0x8B, 0x42, MailboxArg6 }); // mov eax, [r10+arg6]
        run.AddRange(new byte[] { 0x89, 0x44, 0x24, 0x28 });        // mov [rsp+0x28], eax
        run.AddRange(new byte[] { 0x49, 0x8B, 0x4A, MailboxRcx });  // mov rcx, [r10+rcx]
        run.AddRange(new byte[] { 0x49, 0x8B, 0x52, MailboxRdx });  // mov rdx, [r10+rdx]
        run.AddRange(new byte[] { 0x4D, 0x8B, 0x42, MailboxR8 });   // mov r8, [r10+r8]
        run.AddRange(new byte[] { 0x45, 0x8B, 0x4A, MailboxR9d });  // mov r9d, [r10+r9d]
        run.AddRange(new byte[] { 0x41, 0xFF, 0x52, MailboxFunc }); // call [r10+func]
        run.AddRange(new byte[] { 0x49, 0xBA });                    // mov r10, imm64 (the call clobbered it)
        run.AddRange(BitConverter.GetBytes(mailboxAddr));
        run.AddRange(new byte[] { 0x49, 0x89, 0x42, MailboxResult }); // mov [r10+result], rax
        run.AddRange(new byte[] { 0x41, 0xC7, 0x02 });                // mov dword ptr [r10], StateDone
        run.AddRange(BitConverter.GetBytes(StateDone));
        run.AddRange(new byte[] { 0x48, 0x89, 0xEC });                // mov rsp, rbp
        run.Add(0x5D);                                                // pop rbp
        run.Add(0x59);                                                // pop rcx

        var b = new List<byte>();
        b.AddRange(new byte[] { 0x49, 0xBA }); // mov r10, imm64 (mailbox) - r10 is volatile and unused at entry
        b.AddRange(BitConverter.GetBytes(mailboxAddr));
        b.AddRange(new byte[] { 0x41, 0xFF, 0x42, MailboxHeartbeat });    // inc dword ptr [r10+heartbeat]
        b.AddRange(new byte[] { 0x41, 0x83, 0x3A, (byte)StateRequested }); // cmp dword ptr [r10], StateRequested
        b.AddRange(new byte[] { 0x75, checked((byte)run.Count) });        // jne past the call
        b.AddRange(run);

        b.AddRange(PollEventsExpectedBytes); // replay the real prologue
        b.AddRange(new byte[] { 0xFF, 0x25, 0x00, 0x00, 0x00, 0x00 }); // jmp qword ptr [rip+0]
        b.AddRange(BitConverter.GetBytes(pollEventsAddr + PollEventsExpectedBytes.Length));
        return b.ToArray();
    }
}
