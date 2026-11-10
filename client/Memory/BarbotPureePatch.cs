namespace StarshipTitanicAp;

/// <summary>Fixes a vanilla race in the Starling Puret hand-off. CBarbot::MovieFrameMsg says "What's this?" (250580)
/// and opens the PET's conversation tab on the first frame of _frames[32], but the TrueTalk state that accepts the
/// answer (BarbotScript state 57, set by scriptChanged case 145) was only set by CBarbot::MovieEndMsg at the end of
/// that range, about 3 seconds later. An answer typed in between got a random reply, and the next sentence after the
/// state was set had to be the answer or the puret was lost.
///
/// Two edits:
/// - MovieFrameMsg: the "What's this?" branch (GameOffsets.BarbotWhatsThisSite) jumps to a stub that, when
///   _glassContent is GG_BIRD, calls startTalking(this, 145) first. The stub then replays the 15 overwritten bytes
///   (the 250580 call's argument setup) and jumps back to its call. The branch also runs for mustard and tomato
///   (_frames[55]), which don't take the answer, hence the GG_BIRD check. 145 produces no speech and the 250580 line
///   has no state-table entry, so state 57 survives "What's this?".
/// - MovieEndMsg: the original startTalking(this, 145) at the end of _frames[32] (GameOffsets.BarbotPuretEndTalkCall)
///   becomes a 5-byte NOP. Left in, it would set state 57 again after an answer given during the animation, and the
///   player's next sentence would be taken as a second answer and, unless it named the puret, get the mustard/tomato
///   reply.
///
/// Always applied (ClientSession.InstallHooks). No saved fields change. Both sites are checked byte-for-byte against
/// the expected ScummVM 2.9.1 build before patching.</summary>
public static class BarbotPureePatch
{
    // mov rdx,r12 / mov rcx,r12 / xor r9d,r9d / mov r8d,250580 - the "What's this?" call's argument setup.
    private static readonly byte[] ExpectedWhatsThisBytes =
    {
        0x4C, 0x89, 0xE2,
        0x4C, 0x89, 0xE1,
        0x45, 0x31, 0xC9,
        0x41, 0xB8, 0xD4, 0xD2, 0x03, 0x00,
    };

    // call CGameObject::startTalking (rel32) - the MovieEndMsg call that passes 145.
    private static readonly byte[] ExpectedEndTalkCall = { 0xE8, 0x4F, 0xA5, 0xF8, 0xFF };

    private static readonly byte[] Nop5 = { 0x0F, 0x1F, 0x44, 0x00, 0x00 };

    private const int GlassContentOffset = 0x200; // CBarbot::_glassContent
    private const int GlassBird = 3;              // GG_BIRD
    private const int PuretAnswerScriptId = 145;  // BarbotScript::scriptChanged case 145 -> setState(57)

    private static bool _installed;
    private static long _whatsThisAddr;
    private static long _endTalkAddr;
    private static long _stubAddr;

    public static bool IsInstalled => _installed;

    public static bool Install(MemoryReader mem)
    {
        if (_installed)
            return true;
        if (!mem.IsAttached)
            return false;

        long whatsThisAddr = mem.ModuleBase + GameOffsets.BarbotWhatsThisSite;
        long endTalkAddr = mem.ModuleBase + GameOffsets.BarbotPuretEndTalkCall;
        byte[]? whatsThis = mem.ReadBytes(whatsThisAddr, ExpectedWhatsThisBytes.Length);
        byte[]? endTalk = mem.ReadBytes(endTalkAddr, ExpectedEndTalkCall.Length);
        if (whatsThis is null || !whatsThis.AsSpan().SequenceEqual(ExpectedWhatsThisBytes)
            || endTalk is null || !endTalk.AsSpan().SequenceEqual(ExpectedEndTalkCall))
            return false;

        long startTalking = mem.ModuleBase + GameOffsets.GameObjectStartTalkingFunc;
        long resumeAddr = whatsThisAddr + ExpectedWhatsThisBytes.Length;
        long stubAddr = RemoteCaller.AllocateAndWrite(mem, BuildStub(startTalking, resumeAddr));
        if (stubAddr == 0)
            return false;

        // NOP the end-of-animation call first, so a failure part way never leaves 145 called twice.
        if (!mem.WriteBytes(endTalkAddr, Nop5))
        {
            RemoteCaller.FreeRemoteMemory(mem, stubAddr);
            return false;
        }

        // 14-byte far jmp, padded with a NOP to the end of the replayed bytes. Nothing jumps into those bytes.
        byte[] detour = new byte[ExpectedWhatsThisBytes.Length];
        detour[0] = 0xFF; detour[1] = 0x25; // jmp qword ptr [rip+0]
        BitConverter.GetBytes(stubAddr).CopyTo(detour, 6);
        for (int i = 14; i < detour.Length; i++)
            detour[i] = 0x90;

        if (!mem.WriteBytes(whatsThisAddr, detour))
        {
            mem.WriteBytes(endTalkAddr, ExpectedEndTalkCall);
            RemoteCaller.FreeRemoteMemory(mem, stubAddr);
            return false;
        }

        _installed = true;
        _whatsThisAddr = whatsThisAddr;
        _endTalkAddr = endTalkAddr;
        _stubAddr = stubAddr;
        return true;
    }

    public static bool Uninstall(MemoryReader mem)
    {
        if (!_installed)
            return false;

        // Restore the detour before freeing the stub it jumps to.
        bool restored = mem.WriteBytes(_whatsThisAddr, ExpectedWhatsThisBytes);
        restored &= mem.WriteBytes(_endTalkAddr, ExpectedEndTalkCall);
        RemoteCaller.FreeRemoteMemory(mem, _stubAddr);

        _installed = false;
        _whatsThisAddr = 0;
        _endTalkAddr = 0;
        _stubAddr = 0;
        return restored;
    }

    /// <summary>Jumped to from MovieFrameMsg, so rsp keeps the function body's alignment and shadow space, and r12 (the
    /// CBarbot, callee-saved) survives the extra call. Every register the replayed bytes set is reloaded after it.</summary>
    private static byte[] BuildStub(long startTalking, long resumeAddr)
    {
        var b = new List<byte>();

        // cmp dword ptr [r12+0x200], GG_BIRD
        b.AddRange(new byte[] { 0x41, 0x83, 0xBC, 0x24 });
        b.AddRange(BitConverter.GetBytes(GlassContentOffset));
        b.Add(GlassBird);
        // jne replay
        b.AddRange(new byte[] { 0x75, 0x00 });
        int jneOperand = b.Count - 1;
        // startTalking(this, this, 145, nullptr): mov rdx,r12 / mov rcx,r12 / xor r9d,r9d / mov r8d,145
        b.AddRange(new byte[] { 0x4C, 0x89, 0xE2, 0x4C, 0x89, 0xE1, 0x45, 0x31, 0xC9, 0x41, 0xB8 });
        b.AddRange(BitConverter.GetBytes(PuretAnswerScriptId));
        // mov rax, imm64 / call rax
        b.AddRange(new byte[] { 0x48, 0xB8 }); b.AddRange(BitConverter.GetBytes(startTalking));
        b.AddRange(new byte[] { 0xFF, 0xD0 });

        b[jneOperand] = checked((byte)(sbyte)(b.Count - (jneOperand + 1)));
        // replay: the overwritten argument setup for startTalking(this, this, 250580, nullptr)
        b.AddRange(ExpectedWhatsThisBytes);
        // jmp qword ptr [rip+0] -> the original call
        b.AddRange(new byte[] { 0xFF, 0x25, 0x00, 0x00, 0x00, 0x00 }); b.AddRange(BitConverter.GetBytes(resumeAddr));

        return b.ToArray();
    }
}
