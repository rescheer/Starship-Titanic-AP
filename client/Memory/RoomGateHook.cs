namespace StarshipTitanicAp;

/// <summary>Locks rooms until their AP key arrives. Two patches share one remote gate table (indexed by room
/// number, 1 = allowed), which starts all-allowed so installing changes nothing until SetLockedRooms locks rooms.
///
/// Move gate - CGameState::changeView(CViewItem*, CMovieClip*) (GameOffsets.GameStateChangeViewFunc), the single
/// function every walking link, scripted mover (CMovePlayerTo, CExitLift, CExitPellerator, ...) and
/// cutscene-deferred move funnels through. Its own leaveView/preEnterView/enterView calls all happen inside it, so
/// returning from its entry cancels the move with no side effects. Its prologue (16 bytes, no RIP-relative
/// operands, replayed verbatim):
///   push r14 / push r13 / push r12 / push rdi / push rsi / push rbx   ; 41 56 41 55 41 54 57 56 53
///   sub rsp, 0x28                                                     ; 48 83 EC 28
///   mov rbx, rcx                                                      ; 48 89 CB
/// The stub resolves the destination room via the view's own virtual findRoom() and reads its _roomNumber. The
/// move proceeds when the destination is the current room (so a player somehow inside a locked room is never
/// frozen), the room number is outside the gate table, or the table allows it. Otherwise it records the from/to
/// room numbers in the mailbox and returns.
///
/// Link cursor - CViewItem::MouseMoveMsg's hovered-link branch (GameOffsets.ViewMouseMoveLinkCursorSite):
///   mov edx, [rax+0x70]     ; linkItem->_cursorId (rax = the hovered CLinkItem*)
///   mov rcx, [rdi+0x30]     ; CScreenManager::_mouseCursor
///   call setCursor          ; rel32 -> GameOffsets.MouseCursorSetCursorFunc
///   jmp epilogue            ; rel32 -> GameOffsets.ViewMouseMoveEpilogue
/// The stub rebuilds these with absolute addresses, swapping edx for CURSOR_INVALID when the link leads into a
/// locked room other than its own. Only the displayed cursor changes - the link's saved _cursorId is untouched.
///
/// Object cursor - CViewItem::handleMouseMsg's hovered-object branch (GameOffsets.ViewHandleMouseObjectCursorSite):
///   movsxd rdx, eax          ; idx
///   mov rdx, [r12+rdx*8]     ; gameObjects[idx]
///   mov edx, [rdx+0x11C]     ; ->_cursorId
///   cmp edx, CURSOR_IGNORE
///   je next                  ; rel8 -> GameOffsets.ViewHandleMouseObjectNext (try the next object)
/// Scripted doors (CMovePlayerTo, CTOWParrotNav, ...) are game objects whose destinations assembly can't resolve,
/// so the client resolves them (MoverDestinations) and lists the ones leading into locked rooms in a zero-terminated
/// pointer table; the stub swaps edx for CURSOR_INVALID when the hovered object is in it.</summary>
public static class RoomGateHook
{
    private const int MoveGateOriginalLength = 16;   // through "mov rbx,rcx" - clean instruction boundary
    private const int LinkCursorOriginalLength = 17; // through "jmp epilogue" - clean instruction boundary
    private const int ObjectCursorOriginalLength = 18; // through "je next" - clean instruction boundary
    public const int GateTableSize = 64;             // indexed by room number; room IDs top out at 57
    public const int MaxCursorObjects = 63;          // object table holds this many pointers plus a null terminator
    private const int MailboxTotalSize = 1 + 4 + 4;  // [0]=ready flag, [1..4]=from room, [5..8]=to room (int32s)

    private sealed record Patch(long Addr, byte[] OriginalBytes, long StubAddr);

    private static bool _installed;
    private static Patch? _moveGate;
    private static Patch? _linkCursor;
    private static Patch? _objectCursor;
    private static long _gateAddr;
    private static long _objectTableAddr;
    private static long _mailboxAddr;
    private static byte[]? _lastGate;
    private static byte[]? _lastObjectTable;

    public static bool IsInstalled => _installed;

    public static bool Install(MemoryReader mem)
    {
        if (_installed)
            return true;
        if (!mem.IsAttached)
            return false;

        byte[] gateInit = new byte[GateTableSize];
        Array.Fill(gateInit, (byte)1); // all rooms allowed until SetLockedRooms says otherwise
        long gateAddr = RemoteCaller.AllocateAndWrite(mem, gateInit);
        if (gateAddr == 0)
            return false;

        byte[] objectTableInit = new byte[(MaxCursorObjects + 1) * 8]; // empty (null-terminated at [0])
        long objectTableAddr = RemoteCaller.AllocateAndWrite(mem, objectTableInit);
        if (objectTableAddr == 0)
        {
            RemoteCaller.FreeRemoteMemory(mem, gateAddr);
            return false;
        }

        long mailboxAddr = RemoteCaller.AllocateAndWrite(mem, new byte[MailboxTotalSize]);
        if (mailboxAddr == 0)
        {
            RemoteCaller.FreeRemoteMemory(mem, gateAddr);
            RemoteCaller.FreeRemoteMemory(mem, objectTableAddr);
            return false;
        }

        var patches = new List<Patch>();

        long moveGateAddr = mem.ModuleBase + GameOffsets.GameStateChangeViewFunc;
        Patch? moveGate = InstallPatch(mem, moveGateAddr, MoveGateOriginalLength,
            original => BuildMoveGateStub(original, gateAddr, mailboxAddr, moveGateAddr + MoveGateOriginalLength));
        if (moveGate is not null)
            patches.Add(moveGate);

        Patch? linkCursor = moveGate is null ? null : InstallPatch(mem,
            mem.ModuleBase + GameOffsets.ViewMouseMoveLinkCursorSite, LinkCursorOriginalLength,
            _ => BuildLinkCursorStub(gateAddr, mem.ModuleBase + GameOffsets.MouseCursorSetCursorFunc,
                mem.ModuleBase + GameOffsets.ViewMouseMoveEpilogue));
        if (linkCursor is not null)
            patches.Add(linkCursor);

        long objectCursorAddr = mem.ModuleBase + GameOffsets.ViewHandleMouseObjectCursorSite;
        Patch? objectCursor = linkCursor is null ? null : InstallPatch(mem, objectCursorAddr, ObjectCursorOriginalLength,
            _ => BuildObjectCursorStub(objectTableAddr, mem.ModuleBase + GameOffsets.ViewHandleMouseObjectNext,
                objectCursorAddr + ObjectCursorOriginalLength));

        if (moveGate is null || linkCursor is null || objectCursor is null)
        {
            foreach (Patch patch in patches)
                UninstallPatch(mem, patch);
            RemoteCaller.FreeRemoteMemory(mem, gateAddr);
            RemoteCaller.FreeRemoteMemory(mem, objectTableAddr);
            RemoteCaller.FreeRemoteMemory(mem, mailboxAddr);
            return false;
        }

        _installed = true;
        _moveGate = moveGate;
        _linkCursor = linkCursor;
        _objectCursor = objectCursor;
        _gateAddr = gateAddr;
        _objectTableAddr = objectTableAddr;
        _mailboxAddr = mailboxAddr;
        _lastGate = gateInit;
        _lastObjectTable = objectTableInit;
        return true;
    }

    public static bool Uninstall(MemoryReader mem)
    {
        if (!_installed || _moveGate is null || _linkCursor is null || _objectCursor is null)
            return false;

        bool restored = UninstallPatch(mem, _objectCursor);
        restored &= UninstallPatch(mem, _linkCursor);
        restored &= UninstallPatch(mem, _moveGate);
        RemoteCaller.FreeRemoteMemory(mem, _gateAddr);
        RemoteCaller.FreeRemoteMemory(mem, _objectTableAddr);
        RemoteCaller.FreeRemoteMemory(mem, _mailboxAddr);

        _installed = false;
        _moveGate = null;
        _linkCursor = null;
        _objectCursor = null;
        _gateAddr = 0;
        _objectTableAddr = 0;
        _mailboxAddr = 0;
        _lastGate = null;
        _lastObjectTable = null;
        return restored;
    }

    /// <summary>Rewrites the remote object table so hovering any of these game objects shows CURSOR_INVALID - call
    /// every reconcile tick. Only the first MaxCursorObjects are used. Only touches remote memory when the table
    /// actually changes.</summary>
    public static bool SetCursorBlockedObjects(MemoryReader mem, IEnumerable<long> objectAddrs)
    {
        if (!_installed)
            return false;

        byte[] table = new byte[(MaxCursorObjects + 1) * 8]; // trailing entry stays 0 as the terminator
        int count = 0;
        foreach (long addr in objectAddrs)
        {
            if (addr == 0 || count == MaxCursorObjects)
                continue;
            BitConverter.GetBytes(addr).CopyTo(table, count * 8);
            count++;
        }

        if (_lastObjectTable is not null && table.AsSpan().SequenceEqual(_lastObjectTable))
            return true;

        bool ok = mem.WriteBytes(_objectTableAddr, table);
        if (ok)
            _lastObjectTable = table;
        return ok;
    }

    /// <summary>Rewrites the remote gate table so only rooms in lockedRooms are blocked - call every reconcile
    /// tick. Only touches remote memory when the table actually changes.</summary>
    public static bool SetLockedRooms(MemoryReader mem, IEnumerable<int> lockedRooms)
    {
        if (!_installed)
            return false;

        byte[] gate = new byte[GateTableSize];
        Array.Fill(gate, (byte)1);
        foreach (int room in lockedRooms)
        {
            if (room >= 0 && room < GateTableSize)
                gate[room] = 0;
        }

        if (_lastGate is not null && gate.AsSpan().SequenceEqual(_lastGate))
            return true;

        bool ok = mem.WriteBytes(_gateAddr, gate);
        if (ok)
            _lastGate = gate;
        return ok;
    }

    /// <summary>Checks whether a move was blocked since the last poll, returning the room the player stayed in
    /// and the locked room they tried to enter.</summary>
    public static (int FromRoom, int ToRoom)? PollBlockedMove(MemoryReader mem)
    {
        if (!_installed)
            return null;

        byte[]? readyByte = mem.ReadBytes(_mailboxAddr, 1);
        if (readyByte is null || readyByte[0] == 0)
            return null;

        byte[]? rooms = mem.ReadBytes(_mailboxAddr + 1, 8);
        mem.WriteByte(_mailboxAddr, 0); // clear ready flag

        if (rooms is null)
            return null;

        return (BitConverter.ToInt32(rooms, 0), BitConverter.ToInt32(rooms, 4));
    }

    /// <summary>Writes buildStub's stub into the target process and overwrites length bytes at addr with a far jmp
    /// to it (NOP-padded). buildStub receives the original bytes being overwritten.</summary>
    private static Patch? InstallPatch(MemoryReader mem, long addr, int length, Func<byte[], byte[]> buildStub)
    {
        byte[]? original = mem.ReadBytes(addr, length);
        if (original is null)
            return null;

        long stubAddr = RemoteCaller.AllocateAndWrite(mem, buildStub(original));
        if (stubAddr == 0)
            return null;

        // 14-byte far jmp, padded with NOPs to fill length.
        byte[] detour = new byte[length];
        detour[0] = 0xFF; detour[1] = 0x25; // jmp qword ptr [rip+0]
        detour[2] = 0x00; detour[3] = 0x00; detour[4] = 0x00; detour[5] = 0x00;
        BitConverter.GetBytes(stubAddr).CopyTo(detour, 6); // 8-byte absolute address
        for (int i = 14; i < length; i++)
            detour[i] = 0x90;

        if (!mem.WriteBytes(addr, detour))
        {
            RemoteCaller.FreeRemoteMemory(mem, stubAddr);
            return null;
        }

        return new Patch(addr, original, stubAddr);
    }

    private static bool UninstallPatch(MemoryReader mem, Patch patch)
    {
        bool restored = mem.WriteBytes(patch.Addr, patch.OriginalBytes);
        RemoteCaller.FreeRemoteMemory(mem, patch.StubAddr);
        return restored;
    }

    // ------------------------------------------------------------------
    // Stub construction
    // ------------------------------------------------------------------

    /// <summary>Collects forward jumps to one label, patching their rel32 operands once the label's offset is known.</summary>
    private sealed class ForwardLabel(List<byte> code)
    {
        private readonly List<int> _operandOffsets = new();

        public void Jcc(byte opcode2)
        {
            code.AddRange(new byte[] { 0x0F, opcode2 });
            _operandOffsets.Add(code.Count);
            code.AddRange(new byte[4]); // placeholder
        }

        public void Bind()
        {
            foreach (int operandOffset in _operandOffsets)
            {
                byte[] rel32 = BitConverter.GetBytes(code.Count - (operandOffset + 4));
                for (int i = 0; i < 4; i++)
                    code[operandOffset + i] = rel32[i];
            }
        }
    }

    /// <summary>Builds the move-gate stub. Entered with the hooked function's own entry state (rcx = CGameState*,
    /// rdx = newView, r8 = clip, rsp = 8 mod 16). Pushing the three live args leaves rsp 16-byte aligned for the
    /// findRoom() call, with the 0x20 shadow space below them.</summary>
    private static byte[] BuildMoveGateStub(byte[] originalBytes, long gateAddr, long mailboxAddr, long resumeAddr)
    {
        var b = new List<byte>();
        var allow = new ForwardLabel(b);

        b.Add(0x51);                                     // push rcx
        b.Add(0x52);                                     // push rdx
        b.AddRange(new byte[] { 0x41, 0x50 });           // push r8
        b.AddRange(new byte[] { 0x48, 0x83, 0xEC, 0x20 }); // sub rsp, 0x20 (shadow space)

        b.AddRange(new byte[] { 0x48, 0x85, 0xD2 });     // test rdx, rdx
        allow.Jcc(0x84);                                 // je allow (no destination view)

        b.AddRange(new byte[] { 0x48, 0x89, 0xD1 });     // mov rcx, rdx
        b.AddRange(new byte[] { 0x48, 0x8B, 0x01 });     // mov rax, [rcx] (vtable)
        b.AddRange(new byte[] { 0xFF, 0x90 });           // call [rax + findRoom slot]
        b.AddRange(BitConverter.GetBytes((int)GameOffsets.NamedItemFindRoomVtableOffset));
        b.AddRange(new byte[] { 0x48, 0x85, 0xC0 });     // test rax, rax
        allow.Jcc(0x84);                                 // je allow (no room)

        b.AddRange(new byte[] { 0x8B, 0x40, (byte)GameOffsets.RoomItemRoomNumberOffset }); // mov eax, [rax + _roomNumber]
        b.AddRange(new byte[] { 0x48, 0x8B, 0x4C, 0x24, 0x30 }); // mov rcx, [rsp+0x30] (saved CGameState*)
        b.AddRange(new byte[] { 0x8B, 0x49, (byte)GameOffsets.GameStateRoomNumberOffset }); // mov ecx, [rcx + _gameLocation._roomNumber]
        b.AddRange(new byte[] { 0x39, 0xC8 });           // cmp eax, ecx
        allow.Jcc(0x84);                                 // je allow (same room)
        b.AddRange(new byte[] { 0x83, 0xF8, GateTableSize }); // cmp eax, GateTableSize
        allow.Jcc(0x83);                                 // jae allow (unsigned - also catches -1)

        b.AddRange(new byte[] { 0x49, 0xBA });           // mov r10, imm64 (gate table)
        b.AddRange(BitConverter.GetBytes(gateAddr));
        b.AddRange(new byte[] { 0x41, 0x80, 0x3C, 0x02, 0x00 }); // cmp byte ptr [r10+rax], 0
        allow.Jcc(0x85);                                 // jne allow

        // Blocked: record the attempt and return without running the real function.
        b.AddRange(new byte[] { 0x49, 0xBA });           // mov r10, imm64 (mailbox)
        b.AddRange(BitConverter.GetBytes(mailboxAddr));
        b.AddRange(new byte[] { 0x41, 0x89, 0x4A, 0x01 }); // mov [r10+1], ecx (from room)
        b.AddRange(new byte[] { 0x41, 0x89, 0x42, 0x05 }); // mov [r10+5], eax (to room)
        b.AddRange(new byte[] { 0x41, 0xC6, 0x02, 0x01 }); // mov byte ptr [r10], 1 (ready flag)
        AddRestoreArgs(b);
        b.Add(0xC3);                                     // ret

        // Allowed: restore the args, replay the real overwritten instructions and resume the function.
        allow.Bind();
        AddRestoreArgs(b);
        b.AddRange(originalBytes);
        AddFarJmp(b, resumeAddr);

        return b.ToArray();
    }

    private static void AddRestoreArgs(List<byte> b)
    {
        b.AddRange(new byte[] { 0x48, 0x83, 0xC4, 0x20 }); // add rsp, 0x20
        b.AddRange(new byte[] { 0x41, 0x58 });           // pop r8
        b.Add(0x5A);                                     // pop rdx
        b.Add(0x59);                                     // pop rcx
    }

    /// <summary>Builds the link-cursor stub. Entered mid-function (rax = hovered CLinkItem*, rdi = CScreenManager*,
    /// rsp already 16-byte aligned with shadow space), so it can call setCursor directly. Only volatile r10/r11
    /// are used as scratch.</summary>
    private static byte[] BuildLinkCursorStub(long gateAddr, long setCursorAddr, long epilogueAddr)
    {
        var b = new List<byte>();
        var setCursor = new ForwardLabel(b);

        b.AddRange(new byte[] { 0x8B, 0x50, (byte)GameOffsets.LinkItemCursorIdOffset });   // mov edx, [rax + _cursorId]
        b.AddRange(new byte[] { 0x44, 0x8B, 0x50, (byte)GameOffsets.LinkItemRoomNumberOffset }); // mov r10d, [rax + _roomNumber] (destination)
        b.AddRange(new byte[] { 0x41, 0x83, 0xFA, GateTableSize }); // cmp r10d, GateTableSize
        setCursor.Jcc(0x83);                             // jae setCursor (unsigned - also catches -1)

        b.AddRange(new byte[] { 0x49, 0xBB });           // mov r11, imm64 (gate table)
        b.AddRange(BitConverter.GetBytes(gateAddr));
        b.AddRange(new byte[] { 0x43, 0x80, 0x3C, 0x13, 0x00 }); // cmp byte ptr [r11+r10], 0
        setCursor.Jcc(0x85);                             // jne setCursor (allowed)

        // Destination is locked - unless it's the link's own room (link -> view -> node -> room via _parent).
        b.AddRange(new byte[] { 0x4C, 0x8B, 0x58, (byte)GameOffsets.Parent }); // mov r11, [rax + _parent] (view)
        b.AddRange(new byte[] { 0x4D, 0x85, 0xDB });     // test r11, r11
        setCursor.Jcc(0x84);
        b.AddRange(new byte[] { 0x4D, 0x8B, 0x5B, (byte)GameOffsets.Parent }); // mov r11, [r11 + _parent] (node)
        b.AddRange(new byte[] { 0x4D, 0x85, 0xDB });     // test r11, r11
        setCursor.Jcc(0x84);
        b.AddRange(new byte[] { 0x4D, 0x8B, 0x5B, (byte)GameOffsets.Parent }); // mov r11, [r11 + _parent] (room)
        b.AddRange(new byte[] { 0x4D, 0x85, 0xDB });     // test r11, r11
        setCursor.Jcc(0x84);
        b.AddRange(new byte[] { 0x45, 0x3B, 0x53, (byte)GameOffsets.RoomItemRoomNumberOffset }); // cmp r10d, [r11 + _roomNumber]
        setCursor.Jcc(0x84);                             // je setCursor (same room)
        b.Add(0xBA);                                     // mov edx, CURSOR_INVALID
        b.AddRange(BitConverter.GetBytes(GameOffsets.CursorInvalid));

        // Rebuilt original tail: setCursor(edx) on the screen manager's mouse cursor, then the shared epilogue.
        setCursor.Bind();
        b.AddRange(new byte[] { 0x48, 0x8B, 0x4F, 0x30 }); // mov rcx, [rdi+0x30] (CScreenManager::_mouseCursor)
        b.AddRange(new byte[] { 0x48, 0xB8 });           // mov rax, imm64 (setCursor)
        b.AddRange(BitConverter.GetBytes(setCursorAddr));
        b.AddRange(new byte[] { 0xFF, 0xD0 });           // call rax
        AddFarJmp(b, epilogueAddr);

        return b.ToArray();
    }

    /// <summary>Builds the object-cursor stub. Entered mid-loop (eax = idx, r12 = gameObjects' data) - eax must
    /// survive to nextAddr, where the loop decrements it. Only rcx/rdx and volatile r10/r11 are used as scratch;
    /// the original code reloads rax/rcx right after resumeAddr.</summary>
    private static byte[] BuildObjectCursorStub(long objectTableAddr, long nextAddr, long resumeAddr)
    {
        var b = new List<byte>();
        var check = new ForwardLabel(b);
        var invalid = new ForwardLabel(b);
        var resume = new ForwardLabel(b);

        b.AddRange(new byte[] { 0x48, 0x63, 0xD0 });     // movsxd rdx, eax
        b.AddRange(new byte[] { 0x49, 0x8B, 0x14, 0xD4 }); // mov rdx, [r12+rdx*8] (gameObjects[idx])
        b.AddRange(new byte[] { 0x49, 0x89, 0xD3 });     // mov r11, rdx (keep the object for the table lookup)
        b.AddRange(new byte[] { 0x8B, 0x92 });           // mov edx, [rdx + _cursorId]
        b.AddRange(BitConverter.GetBytes((int)GameOffsets.GameObjectCursorIdOffset));
        b.AddRange(new byte[] { 0x83, 0xFA, (byte)GameOffsets.CursorIgnore }); // cmp edx, CURSOR_IGNORE
        check.Jcc(0x85);                                 // jne check
        AddFarJmp(b, nextAddr);                          // ignored object - the original "je next"

        check.Bind();
        b.AddRange(new byte[] { 0x49, 0xBA });           // mov r10, imm64 (object table)
        b.AddRange(BitConverter.GetBytes(objectTableAddr));
        int loopStart = b.Count;
        b.AddRange(new byte[] { 0x49, 0x8B, 0x0A });     // mov rcx, [r10]
        b.AddRange(new byte[] { 0x48, 0x85, 0xC9 });     // test rcx, rcx
        resume.Jcc(0x84);                                // je resume (end of table - not blocked)
        b.AddRange(new byte[] { 0x4C, 0x39, 0xD9 });     // cmp rcx, r11
        invalid.Jcc(0x84);                               // je invalid
        b.AddRange(new byte[] { 0x49, 0x83, 0xC2, 0x08 }); // add r10, 8
        b.Add(0xEB);                                     // jmp loopStart (rel8)
        b.Add(unchecked((byte)(sbyte)(loopStart - (b.Count + 1))));

        invalid.Bind();
        b.Add(0xBA);                                     // mov edx, CURSOR_INVALID
        b.AddRange(BitConverter.GetBytes(GameOffsets.CursorInvalid));

        resume.Bind();
        AddFarJmp(b, resumeAddr);

        return b.ToArray();
    }

    private static void AddFarJmp(List<byte> b, long target)
    {
        b.AddRange(new byte[] { 0xFF, 0x25, 0x00, 0x00, 0x00, 0x00 }); // jmp qword ptr [rip+0]
        b.AddRange(BitConverter.GetBytes(target));
    }
}
