namespace StarshipTitanicAp;

/// <summary>Adds an extra condition on top of CGetLiftEye2::MouseDragStartMsg's own checkPoint() result, so the
/// broken elevator's Titania's Eye pickup can be blocked until the player already holds the LiftBot Head -
/// preventing a real softlock: taking the Eye leaves the elevator's head socket empty (tracked by CLift's own
/// _hasHead/_hasCorrectHead statics, set the instant the drag completes), and the elevator won't let the player
/// leave until something is put back in that socket. This can't be fixed via the real Eye item's own _canTake:
/// CGetLiftEye2::MouseDragStartMsg forwards a CPassOnDragStartMsg straight to that item without ever consulting
/// it - the same message CCarry::MouseDragStartMsg itself only sends after its own _canTake check passes - so the
/// block has to happen inside CGetLiftEye2::MouseDragStartMsg itself, before that forward ever happens.
///
/// The function compiles down to `bool result = checkPoint(...); if (result) {...side effects...} return result;`
/// with a single shared epilogue for both the true and false paths (see GameOffsets' doc comment for the full
/// layout). This hook overwrites the first 15 bytes of the "checkPoint succeeded" body - a clean instruction
/// boundary, none of it RIP-relative so it's safe to replay verbatim - with a gate check: if this app's own
/// remote "may take" byte is 0, jump straight to the shared epilogue with r12d=0, reproducing exactly what a
/// failed checkPoint() would have done (no vanilla message either way - CGetLiftEye2 shows none on a miss).
/// Otherwise, replay the original 15 bytes and jump back to resume the function exactly as if unhooked.</summary>
public static class GetLiftEye2GateHook
{
    private const int OriginalBytesLength = 15; // through "mov rcx,r13" - clean instruction boundary
    private const int GateTotalSize = 1;   // [0] = 1 if the pickup may proceed, 0 to block it
    private const int MailboxTotalSize = 1; // [0] = ready flag, set when a blocked attempt just fired

    private static bool _installed;
    private static long _hookedFuncAddr;
    private static byte[]? _originalBytes;
    private static long _stubAddr;
    private static long _gateAddr;
    private static long _mailboxAddr;

    public static bool IsInstalled => _installed;

    public static bool Install(MemoryReader mem)
    {
        if (_installed)
            return true;
        if (!mem.IsAttached)
            return false;

        long funcAddr = mem.ModuleBase + GameOffsets.GetLiftEye2MouseDragBodyFunc;
        long epilogueAddr = mem.ModuleBase + GameOffsets.GetLiftEye2MouseDragEpilogueFunc;
        long resumeAddr = funcAddr + OriginalBytesLength;

        byte[]? original = mem.ReadBytes(funcAddr, OriginalBytesLength);
        if (original is null)
            return false;

        long gateAddr = RemoteCaller.AllocateAndWrite(mem, new byte[GateTotalSize]); // starts blocked (0)
        if (gateAddr == 0)
            return false;

        long mailboxAddr = RemoteCaller.AllocateAndWrite(mem, new byte[MailboxTotalSize]);
        if (mailboxAddr == 0)
        {
            RemoteCaller.FreeRemoteMemory(mem, gateAddr);
            return false;
        }

        byte[] stub = BuildStub(original, gateAddr, mailboxAddr, resumeAddr, epilogueAddr);
        long stubAddr = RemoteCaller.AllocateAndWrite(mem, stub);
        if (stubAddr == 0)
        {
            RemoteCaller.FreeRemoteMemory(mem, gateAddr);
            RemoteCaller.FreeRemoteMemory(mem, mailboxAddr);
            return false;
        }

        // 14-byte far jmp, padded with a NOP to fill OriginalBytesLength.
        byte[] detour = new byte[OriginalBytesLength];
        detour[0] = 0xFF; detour[1] = 0x25; // jmp qword ptr [rip+0]
        detour[2] = 0x00; detour[3] = 0x00; detour[4] = 0x00; detour[5] = 0x00;
        BitConverter.GetBytes(stubAddr).CopyTo(detour, 6); // 8-byte absolute address
        for (int i = 14; i < OriginalBytesLength; i++)
            detour[i] = 0x90;

        if (!mem.WriteBytes(funcAddr, detour))
        {
            RemoteCaller.FreeRemoteMemory(mem, gateAddr);
            RemoteCaller.FreeRemoteMemory(mem, mailboxAddr);
            RemoteCaller.FreeRemoteMemory(mem, stubAddr);
            return false;
        }

        _installed = true;
        _hookedFuncAddr = funcAddr;
        _originalBytes = original;
        _stubAddr = stubAddr;
        _gateAddr = gateAddr;
        _mailboxAddr = mailboxAddr;
        return true;
    }

    public static bool Uninstall(MemoryReader mem)
    {
        if (!_installed || _originalBytes is null)
            return false;

        bool restored = mem.WriteBytes(_hookedFuncAddr, _originalBytes);
        RemoteCaller.FreeRemoteMemory(mem, _stubAddr);
        RemoteCaller.FreeRemoteMemory(mem, _gateAddr);
        RemoteCaller.FreeRemoteMemory(mem, _mailboxAddr);

        _installed = false;
        _originalBytes = null;
        _hookedFuncAddr = 0;
        _stubAddr = 0;
        _gateAddr = 0;
        _mailboxAddr = 0;
        return restored;
    }

    /// <summary>Sets whether the Eye pickup may currently proceed - call every reconcile tick.</summary>
    public static bool SetGateAllowed(MemoryReader mem, bool allowed)
    {
        if (!_installed)
            return false;
        return mem.WriteByte(_gateAddr, (byte)(allowed ? 1 : 0));
    }

    /// <summary>Checks whether a blocked pickup attempt fired since the last poll.</summary>
    public static bool PollBlockedAttempt(MemoryReader mem)
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

    /// <summary>Builds the gate-check stub. On "allowed", replays originalBytes (none of it RIP-relative, so
    /// safe to relocate verbatim) and jumps back to resumeAddr to continue the function normally. On "blocked",
    /// signals the mailbox, zeroes r12d (the function's return value register on this path), and jumps straight
    /// to epilogueAddr - the same shared epilogue a real failed checkPoint() would have reached.</summary>
    private static byte[] BuildStub(byte[] originalBytes, long gateAddr, long mailboxAddr, long resumeAddr, long epilogueAddr)
    {
        var allowedPath = new List<byte>();
        allowedPath.AddRange(originalBytes); // replay the real, overwritten instructions
        allowedPath.AddRange(new byte[] { 0xFF, 0x25, 0x00, 0x00, 0x00, 0x00 }); // jmp qword ptr [rip+0]
        allowedPath.AddRange(BitConverter.GetBytes(resumeAddr));

        var blockedPath = new List<byte>();
        // mov r10, imm64 (mailbox address)
        blockedPath.AddRange(new byte[] { 0x49, 0xBA });
        blockedPath.AddRange(BitConverter.GetBytes(mailboxAddr));
        // mov byte ptr [r10], 1 (ready flag)
        blockedPath.AddRange(new byte[] { 0x41, 0xC6, 0x02, 0x01 });
        // xor r12d, r12d (this function's return value on the shared epilogue is `mov eax, r12d`)
        blockedPath.AddRange(new byte[] { 0x45, 0x31, 0xE4 });
        // jmp qword ptr [rip+0]
        blockedPath.AddRange(new byte[] { 0xFF, 0x25, 0x00, 0x00, 0x00, 0x00 });
        blockedPath.AddRange(BitConverter.GetBytes(epilogueAddr));

        var b = new List<byte>();
        // mov r10, imm64 (gate address)
        b.AddRange(new byte[] { 0x49, 0xBA });
        b.AddRange(BitConverter.GetBytes(gateAddr));
        // mov al, byte ptr [r10]
        b.AddRange(new byte[] { 0x41, 0x8A, 0x02 });
        // test al, al
        b.AddRange(new byte[] { 0x84, 0xC0 });
        // je blockedPath (rel32, patched below once lengths are known)
        b.AddRange(new byte[] { 0x0F, 0x84 });
        int jeOperandOffset = b.Count;
        b.AddRange(new byte[4]); // placeholder

        b.AddRange(allowedPath);
        int blockedPathStart = b.Count;
        b.AddRange(blockedPath);

        int jeRel32 = blockedPathStart - (jeOperandOffset + 4);
        byte[] jeRel32Bytes = BitConverter.GetBytes(jeRel32);
        for (int i = 0; i < 4; i++)
            b[jeOperandOffset + i] = jeRel32Bytes[i];

        return b.ToArray();
    }
}
