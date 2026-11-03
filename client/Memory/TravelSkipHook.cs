namespace StarshipTitanicAp;

/// <summary>Shortens elevator and pellerator trips by rewriting the frame ranges their StatusChangeMsg handlers queue,
/// without patching any of the 82 call sites. A detour on CGameObject::playMovie checks the return address; every
/// other caller runs playMovie untouched.
///
/// Elevator: each range keeps only its last LiftKeepFrames frames, so each segment still shows its slow-down. Flags
/// are left alone, so every range still fires its MovieEndMsg (class-boundary hum crossfades, then the arrival line
/// and move to the exit view).
///
/// Pellerator: a trip is one segment per stop passed, each a speed-up clip, travel loops and (usually) a slow-down clip
/// with MOVIE_WAIT_FOR_FINISH, then a single 264-264 MOVIE_NOTIFY_OBJECT range that hides the pellerator and plays the
/// arrival announcement. The hook plays the trip's first speed-up clip, drops everything else, and remembers the last
/// slow-down it dropped. When the arrival range comes in, it plays that slow-down first (keeping its input lock),
/// so every trip looks like a single stop. A trip whose segment has no slow-down gets the wait flag on the arrival
/// range instead, so input stays locked until it has fired. Whole clips are kept, since starting a clip part way in
/// shows artifacts and held frames.
///
/// Each half has its own enabled flag (the Quick Elevator and Quick Pellerator options); a disabled one leaves its
/// transport's ranges untouched.
///
/// The stolen prologue is checked byte-for-byte against the expected ScummVM 2.9.1 build before patching.</summary>
public static class TravelSkipHook
{
    // push r14/r13/r12/rbp/rdi/rsi/rbx; sub rsp,0x30 - no RIP-relative operands, replayed verbatim.
    private static readonly byte[] PlayMovieExpectedBytes =
    {
        0x41, 0x56, 0x41, 0x55, 0x41, 0x54, 0x55, 0x57, 0x56, 0x53, 0x48, 0x83, 0xEC, 0x30,
    };

    public const int DefaultLiftKeepFrames = 15; // 1 second at the engine's 15 fps

    private const int MovieNotifyObject = 0x04;
    private const int MovieWaitForFinish = 0x10;

    private const byte MailboxLiftKeep = 0x00;
    private const byte MailboxLiftCount = 0x04;          // lift ranges trimmed
    private const byte MailboxSlowDownStart = 0x08;      // last pellerator slow-down dropped this trip
    private const byte MailboxSlowDownEnd = 0x0C;
    private const byte MailboxSlowDownSaved = 0x10;      // 1 if the two above hold one
    private const byte MailboxSpeedUpPlayed = 0x14;      // 1 once this trip's first clip has been let through
    private const byte MailboxPelleratorSkipped = 0x18;  // pellerator ranges dropped
    private const byte MailboxPelleratorTrips = 0x1C;    // pellerator arrivals seen
    private const byte MailboxLiftEnabled = 0x20;
    private const byte MailboxPelleratorEnabled = 0x24;
    private const int MailboxTotalSize = 0x28;

    private static bool _installed;
    private static long _playMovieAddr;
    private static long _stubAddr;
    private static long _mailboxAddr;
    private static MemoryReader? _mem;

    public static bool IsInstalled => _installed;

    /// <summary>How many frames at the end of each lift range to keep. The stub reads it on every call, so a change
    /// applies from the next trip.</summary>
    public static int LiftKeepFrames { get; private set; } = DefaultLiftKeepFrames;

    public static bool SetLiftKeepFrames(int frames)
    {
        LiftKeepFrames = Math.Max(0, frames);
        return !_installed || _mem is not { } mem || mem.WriteInt32(_mailboxAddr + MailboxLiftKeep, LiftKeepFrames);
    }

    /// <summary>Whether elevator trips are shortened. Like the other settings, read by the stub on every call.</summary>
    public static bool LiftEnabled { get; private set; } = true;

    /// <summary>Whether pellerator trips are shortened.</summary>
    public static bool PelleratorEnabled { get; private set; } = true;

    public static bool SetEnabled(bool lift, bool pellerator)
    {
        LiftEnabled = lift;
        PelleratorEnabled = pellerator;
        return !_installed || _mem is not { } mem
            || (mem.WriteInt32(_mailboxAddr + MailboxLiftEnabled, lift ? 1 : 0)
                && mem.WriteInt32(_mailboxAddr + MailboxPelleratorEnabled, pellerator ? 1 : 0));
    }

    public static bool Install(MemoryReader mem)
    {
        if (_installed)
            return true;
        if (!mem.IsAttached)
            return false;

        long playMovieAddr = mem.ModuleBase + GameOffsets.GameObjectPlayMovieFunc;
        byte[]? original = mem.ReadBytes(playMovieAddr, PlayMovieExpectedBytes.Length);
        if (original is null || !original.AsSpan().SequenceEqual(PlayMovieExpectedBytes))
            return false;

        byte[] mailboxInit = new byte[MailboxTotalSize];
        BitConverter.GetBytes(LiftKeepFrames).CopyTo(mailboxInit, MailboxLiftKeep);
        BitConverter.GetBytes(LiftEnabled ? 1 : 0).CopyTo(mailboxInit, MailboxLiftEnabled);
        BitConverter.GetBytes(PelleratorEnabled ? 1 : 0).CopyTo(mailboxInit, MailboxPelleratorEnabled);
        long mailboxAddr = RemoteCaller.AllocateAndWrite(mem, mailboxInit);
        if (mailboxAddr == 0)
            return false;

        long stubAddr = RemoteCaller.AllocateAndWrite(mem, BuildStub(mem.ModuleBase, mailboxAddr, playMovieAddr));
        if (stubAddr == 0)
        {
            RemoteCaller.FreeRemoteMemory(mem, mailboxAddr);
            return false;
        }

        if (!mem.WriteBytes(playMovieAddr, BuildDetour(stubAddr)))
        {
            RemoteCaller.FreeRemoteMemory(mem, mailboxAddr);
            RemoteCaller.FreeRemoteMemory(mem, stubAddr);
            return false;
        }

        _playMovieAddr = playMovieAddr;
        _stubAddr = stubAddr;
        _mailboxAddr = mailboxAddr;
        _mem = mem;
        _installed = true;
        return true;
    }

    public static bool Uninstall(MemoryReader mem)
    {
        if (!_installed)
            return false;

        // The prologue goes back first, so the game can't enter the stub once it's freed. A call already inside the
        // stub is a few instructions from leaving it.
        bool restored = mem.WriteBytes(_playMovieAddr, PlayMovieExpectedBytes);
        RemoteCaller.FreeRemoteMemory(mem, _stubAddr);
        RemoteCaller.FreeRemoteMemory(mem, _mailboxAddr);

        _installed = false;
        _playMovieAddr = 0;
        _stubAddr = 0;
        _mailboxAddr = 0;
        _mem = null;
        return restored;
    }

    /// <summary>What the hook has done since it was installed, for the Debug tab.</summary>
    public static (int LiftTrimmed, int PelleratorTrips, int PelleratorSkipped)? ReadCounts()
    {
        if (!_installed || _mem is not { } mem)
            return null;
        if (mem.ReadInt32(_mailboxAddr + MailboxLiftCount) is not { } lift
            || mem.ReadInt32(_mailboxAddr + MailboxPelleratorTrips) is not { } trips
            || mem.ReadInt32(_mailboxAddr + MailboxPelleratorSkipped) is not { } skipped)
            return null;
        return (lift, trips, skipped);
    }

    // ------------------------------------------------------------------
    // Stub construction
    // ------------------------------------------------------------------

    /// <summary>14-byte absolute far jmp - exactly the stolen length.</summary>
    private static byte[] BuildDetour(long stubAddr)
    {
        byte[] detour = new byte[PlayMovieExpectedBytes.Length];
        detour[0] = 0xFF; detour[1] = 0x25; // jmp qword ptr [rip+0]
        BitConverter.GetBytes(stubAddr).CopyTo(detour, 6);
        return detour;
    }

    /// <summary>Uses only rax, r10 and r11, which are volatile and not playMovie arguments, outside the one call it
    /// makes itself. At entry [rsp] is the caller's return address, rcx the object, edx the start frame, r8d the end
    /// frame and r9d the flags.</summary>
    private static byte[] BuildStub(long moduleBase, long mailboxAddr, long playMovieAddr)
    {
        var b = new List<byte>();
        var fixups = new List<(int Operand, string Label)>();
        var labels = new Dictionary<string, int>();

        void MovR10(long value) { b.AddRange(new byte[] { 0x49, 0xBA }); b.AddRange(BitConverter.GetBytes(value)); }
        void Jump(byte[] opcode, string label) // rel32 form
        {
            b.AddRange(opcode);
            fixups.Add((b.Count, label));
            b.AddRange(new byte[4]);
        }
        void Jcc(byte opcode, string label) => Jump(new byte[] { 0x0F, opcode }, label);
        void Jmp(string label) => Jump(new byte[] { 0xE9 }, label);
        void Call(string label) => Jump(new byte[] { 0xE8 }, label);
        const byte Jb = 0x82, Jae = 0x83, Je = 0x84, Jne = 0x85, Jge = 0x8D, Jle = 0x8E, Jg = 0x8F;

        // Mailbox field access, with r10 = mailbox.
        void SetField(byte offset, int value) // mov dword ptr [r10+offset], imm32
        {
            b.AddRange(new byte[] { 0x41, 0xC7, 0x42, offset });
            b.AddRange(BitConverter.GetBytes(value));
        }
        void CmpFieldZero(byte offset) => b.AddRange(new byte[] { 0x41, 0x83, 0x7A, offset, 0x00 }); // cmp dword ptr [r10+offset], 0
        void IncField(byte offset) => b.AddRange(new byte[] { 0x41, 0xFF, 0x42, offset });          // inc dword ptr [r10+offset]
        void TestR9d(int mask) { b.AddRange(new byte[] { 0x41, 0xF7, 0xC1 }); b.AddRange(BitConverter.GetBytes(mask)); }

        // Jumps to `label` unless the return address in rax is in [start, end).
        void UnlessCalledFrom(long start, long end, string label)
        {
            MovR10(moduleBase + start);
            b.AddRange(new byte[] { 0x4C, 0x39, 0xD0 }); // cmp rax, r10
            Jcc(Jb, label);
            MovR10(moduleBase + end);
            b.AddRange(new byte[] { 0x4C, 0x39, 0xD0 }); // cmp rax, r10
            Jcc(Jae, label);
        }

        b.AddRange(new byte[] { 0x48, 0x8B, 0x04, 0x24 }); // mov rax, [rsp] (return address)

        // --- Lift: move the start frame to within LiftKeepFrames of the end, in whichever direction the range plays.
        UnlessCalledFrom(GameOffsets.LiftStatusChangeMsgStart, GameOffsets.LiftStatusChangeMsgEnd, "notLift");
        MovR10(mailboxAddr);
        CmpFieldZero(MailboxLiftEnabled);
        Jcc(Je, "original");
        b.AddRange(new byte[] { 0x45, 0x8B, 0x5A, MailboxLiftKeep }); // mov r11d, [r10+keep]
        b.AddRange(new byte[] { 0x44, 0x89, 0xC0 });                  // mov eax, r8d
        b.AddRange(new byte[] { 0x44, 0x39, 0xC2 });                  // cmp edx, r8d
        Jcc(Jg, "liftReversed");
        b.AddRange(new byte[] { 0x44, 0x29, 0xD8 });                  // sub eax, r11d (end - keep)
        b.AddRange(new byte[] { 0x39, 0xC2 });                        // cmp edx, eax
        Jcc(Jge, "original");                                         // already short enough
        Jmp("liftApply");
        labels["liftReversed"] = b.Count;
        b.AddRange(new byte[] { 0x44, 0x01, 0xD8 });                  // add eax, r11d (end + keep)
        b.AddRange(new byte[] { 0x39, 0xC2 });                        // cmp edx, eax
        Jcc(Jle, "original");
        labels["liftApply"] = b.Count;
        b.AddRange(new byte[] { 0x89, 0xC2 });                        // mov edx, eax
        IncField(MailboxLiftCount);
        Jmp("original");

        // --- Pellerator
        labels["notLift"] = b.Count;
        UnlessCalledFrom(GameOffsets.PelleratorStatusChangeMsgStart, GameOffsets.PelleratorStatusChangeMsgEnd, "original");
        MovR10(mailboxAddr);
        CmpFieldZero(MailboxPelleratorEnabled);
        Jcc(Je, "original");
        TestR9d(MovieNotifyObject);
        Jcc(Jne, "pellArrive");
        TestR9d(MovieWaitForFinish);
        Jcc(Jne, "pellSlowDown");

        // No flags: let the trip's first clip (always a speed-up) through, drop the rest.
        CmpFieldZero(MailboxSpeedUpPlayed);
        Jcc(Jne, "pellSkip");
        SetField(MailboxSpeedUpPlayed, 1);
        Jmp("original");

        // Slow-down: remember it in place of any earlier one, and drop it.
        labels["pellSlowDown"] = b.Count;
        b.AddRange(new byte[] { 0x41, 0x89, 0x52, MailboxSlowDownStart }); // mov [r10+start], edx
        b.AddRange(new byte[] { 0x45, 0x89, 0x42, MailboxSlowDownEnd });   // mov [r10+end], r8d
        SetField(MailboxSlowDownSaved, 1);
        labels["pellSkip"] = b.Count;
        IncField(MailboxPelleratorSkipped);
        b.Add(0xC3); // ret - playMovie returns void

        // Arrival (264-264, notify): end the trip, playing the last slow-down first if there was one.
        labels["pellArrive"] = b.Count;
        IncField(MailboxPelleratorTrips);
        SetField(MailboxSpeedUpPlayed, 0);
        CmpFieldZero(MailboxSlowDownSaved);
        Jcc(Jne, "pellPlaySlowDown");
        b.AddRange(new byte[] { 0x41, 0x81, 0xC9 }); // or r9d, MovieWaitForFinish - nothing else holds the lock
        b.AddRange(BitConverter.GetBytes(MovieWaitForFinish));
        Jmp("original");

        labels["pellPlaySlowDown"] = b.Count;
        SetField(MailboxSlowDownSaved, 0);
        // Entry rsp is 8 mod 16; four pushes keep it there, and sub 0x28 (shadow space + 8) aligns it for the call.
        b.AddRange(new byte[] { 0x51, 0x52, 0x41, 0x50, 0x41, 0x51 });       // push rcx/rdx/r8/r9
        b.AddRange(new byte[] { 0x48, 0x83, 0xEC, 0x28 });                   // sub rsp, 0x28
        b.AddRange(new byte[] { 0x41, 0x8B, 0x52, MailboxSlowDownStart });   // mov edx, [r10+start]
        b.AddRange(new byte[] { 0x45, 0x8B, 0x42, MailboxSlowDownEnd });     // mov r8d, [r10+end]
        b.AddRange(new byte[] { 0x41, 0xB9 });                               // mov r9d, MovieWaitForFinish
        b.AddRange(BitConverter.GetBytes(MovieWaitForFinish));
        Call("original"); // the real playMovie, through the trampoline below, so it doesn't re-enter this stub
        b.AddRange(new byte[] { 0x48, 0x83, 0xC4, 0x28 });                   // add rsp, 0x28
        b.AddRange(new byte[] { 0x41, 0x59, 0x41, 0x58, 0x5A, 0x59 });       // pop r9/r8/rdx/rcx
        // Falls through to queue the arrival range itself.

        labels["original"] = b.Count;
        b.AddRange(PlayMovieExpectedBytes); // replay the real prologue
        b.AddRange(new byte[] { 0xFF, 0x25, 0x00, 0x00, 0x00, 0x00 }); // jmp qword ptr [rip+0]
        b.AddRange(BitConverter.GetBytes(playMovieAddr + PlayMovieExpectedBytes.Length));

        byte[] result = b.ToArray();
        foreach (var (operand, label) in fixups)
            BitConverter.GetBytes(labels[label] - (operand + 4)).CopyTo(result, operand);
        return result;
    }
}
