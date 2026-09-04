namespace StarshipTitanicAp;

/// <summary>Intercepts CGameObject::petReassignRoom() (via its name-lookup wrapper, GameOffsets.PetReassignRoomFunc)
/// so it records the attempted class instead of assigning a room - unlike ClassUpgradeHook, this blocks every
/// class (including Third/SGT), since room assignment is now driven entirely by receiving "Progressive Stateroom"
/// items over the multiworld (see StateroomAssignTracker + GameActions.AssignNextRoom, which temporarily
/// Uninstall()s this hook to let the real function run for a grant, then Install()s it again).
///
/// The wrapper's own prologue (confirmed live via a Debug tab memory dump at ModuleBase+PetReassignRoomFunc):
///   push r12                          ; 41 54
///   sub rsp, 0x20                     ; 48 83 EC 20
///   mov r12d, edx                     ; 41 89 D4      (stash the incoming class arg)
///   call getPetControl()              ; E8 xx xx xx xx (rel32 -> GameOffsets.GetPetControlFunc)
///   test rax, rax                     ; 48 85 C0
///   je +0x1D                          ; 74 1D          (no PET control -> skip to a bare epilogue)
///   lea rcx, [rax+0xFF0]              ; 48 8D 88 F0 0F 00 00  (rcx = CPetRooms this, GameOffsets.PetRoomsOffset)
///   mov edx, r12d                     ; 44 89 E2
///   ...                               ; tail-jmp into the real CPetRooms::reassignRoom body
/// giving a clean 14-byte instruction boundary through the call - exactly enough for the far-jmp detour with no
/// NOP padding needed. Hooking this early (before the prologue even runs) means the blocked path never has to
/// unwind a pushed r12/adjusted rsp - it can just record and ret, same as ClassUpgradeHook/MaitreDHook.</summary>
public static class RoomAssignHook
{
    private const int OriginalBytesLength = 14; // through "call getPetControl()" - clean instruction boundary
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

        long funcAddr = mem.ModuleBase + GameOffsets.PetReassignRoomFunc;

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

        // 14-byte far jmp - exactly fills OriginalBytesLength, no NOP padding needed.
        byte[] detour = new byte[OriginalBytesLength];
        detour[0] = 0xFF; detour[1] = 0x25; // jmp qword ptr [rip+0]
        detour[2] = 0x00; detour[3] = 0x00; detour[4] = 0x00; detour[5] = 0x00;
        BitConverter.GetBytes(stubAddr).CopyTo(detour, 6); // 8-byte absolute address

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

    /// <summary>Checks the mailbox for a newly-blocked room-assignment attempt.</summary>
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

        b.Add(0xC3); // ret - the real function body (and its prologue) never runs
        return b.ToArray();
    }
}
