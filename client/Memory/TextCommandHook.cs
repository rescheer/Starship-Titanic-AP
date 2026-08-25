using System.Runtime.InteropServices;

namespace StarshipTitanicAp;

/// <summary>
/// Installs a persistent inline hook on CPetConversations::textLineEntered(),
/// confirmed live via disassembly (see project notes). Unlike RemoteCaller's
/// one-shot CreateRemoteThread calls, this patches the function's own entry
/// bytes with a detour to a stub that stays resident for the rest of the
/// session.
///
/// Behavior: if the typed line starts with '!', the stub copies it into a
/// small mailbox buffer (polled via PollCommand), calls the confirmed
/// _textInput clear function directly, and returns WITHOUT running any of
/// the original function - so CTextInputMsg / TrueTalk never sees it.
/// Anything not starting with '!' falls through to the original,
/// unmodified function via a trampoline (re-executing the 19 bytes we
/// overwrote, then jumping back past them).
///
/// IMPORTANT: this is genuinely experimental compared to the rest of this
/// app's remote-call mechanisms. Before relying on it, verify the
/// installed stub in x64dbg (Ctrl+G to the reported stub address) and
/// confirm it disassembles as a sensible check-then-branch, not garbage.
/// Test normal (non-'!') typing first to confirm the trampoline path is
/// transparent before testing the block path.
/// </summary>
public static class TextCommandHook
{
    private const int OriginalBytesLength = 19; // 8 pushes (12 bytes) + sub rsp,0xE8 (7 bytes)
    private const int MailboxTextSize = 120;     // keep <=120 so all copy-loop displacements fit in disp8 if ever changed
    private const int MailboxTotalSize = 1 + MailboxTextSize; // [0]=ready flag, [1..]=text bytes

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

        long funcAddr = mem.ModuleBase + GameOffsets.TextLineEnteredFunc;
        long clearFuncAddr = mem.ModuleBase + GameOffsets.ClearTextControlFunc;

        byte[]? original = mem.ReadBytes(funcAddr, OriginalBytesLength);
        if (original is null)
            return false;

        // --- Allocate mailbox (ready flag + text) ---
        byte[] mailboxInit = new byte[MailboxTotalSize]; // all zero
        long mailboxAddr = RemoteCaller.AllocateAndWrite(mem, mailboxInit);
        if (mailboxAddr == 0)
            return false;

        // --- Build the stub, two-pass (compute lengths, then patch the jne offset) ---
        byte[] commandBlock = BuildCommandBlock(mailboxAddr, clearFuncAddr);
        byte[] notCommandBlock = BuildNotCommandBlock(original, funcAddr);
        byte[] checkBlock = BuildCheckBlock(commandBlock.Length);

        byte[] stub = new byte[checkBlock.Length + commandBlock.Length + notCommandBlock.Length];
        Buffer.BlockCopy(checkBlock, 0, stub, 0, checkBlock.Length);
        Buffer.BlockCopy(commandBlock, 0, stub, checkBlock.Length, commandBlock.Length);
        Buffer.BlockCopy(notCommandBlock, 0, stub, checkBlock.Length + commandBlock.Length, notCommandBlock.Length);

        long stubAddr = RemoteCaller.AllocateAndWrite(mem, stub);
        if (stubAddr == 0)
        {
            RemoteCaller.FreeRemoteMemory(mem, mailboxAddr);
            return false;
        }

        // --- Build the 19-byte detour: 14-byte far jmp to stub, padded with 5 NOPs ---
        byte[] detour = new byte[OriginalBytesLength];
        detour[0] = 0xFF; detour[1] = 0x25; // jmp qword ptr [rip+0]
        detour[2] = 0x00; detour[3] = 0x00; detour[4] = 0x00; detour[5] = 0x00;
        BitConverter.GetBytes(stubAddr).CopyTo(detour, 6); // 8-byte absolute address
        detour[14] = 0x90; detour[15] = 0x90; detour[16] = 0x90; detour[17] = 0x90; detour[18] = 0x90; // NOP padding

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

    /// <summary>
    /// Checks the mailbox for a newly submitted '!' command. Returns the
    /// command text (without the leading '!') if one is waiting, and
    /// clears the ready flag. Returns null if nothing is waiting.
    /// </summary>
    public static string? PollCommand(MemoryReader mem)
    {
        if (!_installed)
            return null;

        byte[]? readyByte = mem.ReadBytes(_mailboxAddr, 1);
        if (readyByte is null || readyByte[0] == 0)
            return null;

        byte[]? textBytes = mem.ReadBytes(_mailboxAddr + 1, MailboxTextSize);
        mem.WriteByte(_mailboxAddr, 0); // clear ready flag

        if (textBytes is null)
            return null;

        int nullIdx = Array.IndexOf(textBytes, (byte)0);
        int length = nullIdx >= 0 ? nullIdx : textBytes.Length;
        string text = System.Text.Encoding.ASCII.GetString(textBytes, 0, length);

        // Strip the leading '!' if present (it should always be, since
        // that's what the stub checks for before copying).
        return text.StartsWith("!") ? text[1..] : text;
    }

    public static long StubAddress => _stubAddr;
    public static long MailboxAddress => _mailboxAddr;

    // ------------------------------------------------------------------
    // Stub construction
    // ------------------------------------------------------------------

    private static byte[] BuildCheckBlock(int commandBlockLength)
    {
        var b = new List<byte>();
        b.AddRange(new byte[] { 0x48, 0x8B, 0x42, 0x08 });       // mov rax, [rdx+8]
        b.AddRange(new byte[] { 0x80, 0x38, 0x21 });             // cmp byte ptr [rax], 0x21 ('!')
        b.AddRange(new byte[] { 0x0F, 0x85 });                   // jne rel32 ->
        b.AddRange(BitConverter.GetBytes(commandBlockLength));    //   skip over commandBlock to notCommandBlock
        return b.ToArray();
    }

    private static byte[] BuildCommandBlock(long mailboxAddr, long clearFuncAddr)
    {
        var b = new List<byte>();
        long textDst = mailboxAddr + 1;
        long readyAddr = mailboxAddr;

        // mov r8, imm64 (destination pointer into mailbox text area)
        b.AddRange(new byte[] { 0x49, 0xB8 });
        b.AddRange(BitConverter.GetBytes(textDst));

        // 15x unrolled: mov r10,[rax]; mov [r8],r10; add rax,8; add r8,8
        for (int i = 0; i < MailboxTextSize / 8; i++)
        {
            b.AddRange(new byte[] { 0x4C, 0x8B, 0x10 });         // mov r10, [rax]
            b.AddRange(new byte[] { 0x4D, 0x89, 0x10 });         // mov [r8], r10
            b.AddRange(new byte[] { 0x48, 0x83, 0xC0, 0x08 });   // add rax, 8
            b.AddRange(new byte[] { 0x49, 0x83, 0xC0, 0x08 });   // add r8, 8
        }

        // mov r11, imm64 (ready flag address)
        b.AddRange(new byte[] { 0x49, 0xBB });
        b.AddRange(BitConverter.GetBytes(readyAddr));
        // mov byte ptr [r11], 1
        b.AddRange(new byte[] { 0x41, 0xC6, 0x03, 0x01 });

        // lea rcx, [rcx+0x4B0]   (rcx still holds original 'this' - untouched so far)
        b.AddRange(new byte[] { 0x48, 0x8D, 0x89 });
        b.AddRange(BitConverter.GetBytes((int)GameOffsets.TextInputFieldOffset));

        // mov rax, imm64 (clear function address); call rax
        b.AddRange(new byte[] { 0x48, 0xB8 });
        b.AddRange(BitConverter.GetBytes(clearFuncAddr));
        b.AddRange(new byte[] { 0xFF, 0xD0 });

        b.Add(0xC3); // ret
        return b.ToArray();
    }

    private static byte[] BuildNotCommandBlock(byte[] originalBytes, long funcAddr)
    {
        var b = new List<byte>();
        b.AddRange(originalBytes); // re-execute the real, overwritten instructions

        long resumeAddr = funcAddr + OriginalBytesLength;
        b.AddRange(new byte[] { 0xFF, 0x25, 0x00, 0x00, 0x00, 0x00 }); // jmp qword ptr [rip+0]
        b.AddRange(BitConverter.GetBytes(resumeAddr));
        return b.ToArray();
    }
}
