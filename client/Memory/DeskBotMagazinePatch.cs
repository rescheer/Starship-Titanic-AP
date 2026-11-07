namespace StarshipTitanicAp;

/// <summary>Lets the DeskBot pay out the 2nd Class upgrade for the Magazine at any class. Vanilla gates the hand-off
/// twice: CDeskbot::ActMsg only starts conversation 140 for a class above Second (Third or None), and
/// DeskbotScript::scriptChanged's case 140 only plays the upgrade (addAssignedRoomDialogue3, which ends in
/// setPassengerClass(2)) at exactly Third. Under item-gated class upgrades the player can hold any class while the
/// 2nd Class check is pending, so both conditional jumps are NOPed and the rest runs as vanilla: ClassUpgradeHook
/// catches the setPassengerClass(2) and sends the check.
///
/// ClassSync.SyncDeskBotMagazinePatch applies it only while the 2nd Class check is pending and the player has
/// really checked in. Before check-in the vanilla gates stay, so the DeskBot eats the Magazine without paying out and
/// ItemSync's safety net returns it.
///
/// The overwritten instructions are checked byte-for-byte against the expected ScummVM 2.9.1 build before patching.</summary>
public static class DeskBotMagazinePatch
{
    // cmp eax, 2; jle -0x20
    private static readonly byte[] ActMsgGateBytes = { 0x83, 0xF8, 0x02, 0x7E, 0xE0 };
    private const int ActMsgJumpOffset = 3;
    private const int ActMsgJumpLength = 2;

    // cmp eax, 3; jne -0xB5
    private static readonly byte[] ScriptGateBytes = { 0x83, 0xF8, 0x03, 0x0F, 0x85, 0x4B, 0xFF, 0xFF, 0xFF };
    private const int ScriptJumpOffset = 3;
    private const int ScriptJumpLength = 6;

    private static bool _installed;

    public static bool IsInstalled => _installed;

    public static bool Install(MemoryReader mem)
    {
        if (_installed)
            return true;
        if (!mem.IsAttached)
            return false;

        long actMsgAddr = mem.ModuleBase + GameOffsets.DeskBotMagazineActMsgGate;
        long scriptAddr = mem.ModuleBase + GameOffsets.DeskBotMagazineScriptGate;
        if (!Matches(mem, actMsgAddr, ActMsgGateBytes) || !Matches(mem, scriptAddr, ScriptGateBytes))
            return false;

        if (!mem.WriteBytes(actMsgAddr + ActMsgJumpOffset, Nops(ActMsgJumpLength)))
            return false;
        if (!mem.WriteBytes(scriptAddr + ScriptJumpOffset, Nops(ScriptJumpLength)))
        {
            mem.WriteBytes(actMsgAddr + ActMsgJumpOffset, ActMsgGateBytes[ActMsgJumpOffset..]);
            return false;
        }

        _installed = true;
        return true;
    }

    public static bool Uninstall(MemoryReader mem)
    {
        if (!_installed)
            return false;

        bool restored = mem.WriteBytes(mem.ModuleBase + GameOffsets.DeskBotMagazineActMsgGate + ActMsgJumpOffset,
            ActMsgGateBytes[ActMsgJumpOffset..]);
        restored &= mem.WriteBytes(mem.ModuleBase + GameOffsets.DeskBotMagazineScriptGate + ScriptJumpOffset,
            ScriptGateBytes[ScriptJumpOffset..]);
        _installed = false;
        return restored;
    }

    private static bool Matches(MemoryReader mem, long addr, byte[] expected)
    {
        byte[]? actual = mem.ReadBytes(addr, expected.Length);
        return actual is not null && actual.AsSpan().SequenceEqual(expected);
    }

    private static byte[] Nops(int count)
    {
        byte[] nops = new byte[count];
        Array.Fill(nops, (byte)0x90);
        return nops;
    }
}
