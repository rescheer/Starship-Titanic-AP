namespace StarshipTitanicAp;

/// <summary>The Music Room panel's controls (CMusicControl subclasses, titanic/gfx/music_*.h) and the settings that
/// solve the puzzle, for the Easy Music Room Puzzle option and for detecting the puzzle being solved.</summary>
public static class MusicRoomControls
{
    public const string SlotDataKey = "easy_music_room_puzzle";
    public const string RoomName = "MusicRoom";

    // CMusicControl fields, following CBackground's (_startFrame@0x124, _endFrame@0x128, _string1@0x130,
    // _string2@0x158, _isBlocking@0x180). Each control's EnterViewMsg picks its frame from _controlVal, so a write
    // shows the next time the panel's view is entered.
    public const long ControlAreaOffset = 0x184; // MusicInstrument: 0 Bells, 1 Snake, 2 Piano, 3 Bass
    public const long ControlValOffset = 0x188;
    public const long ControlMaxOffset = 0x18C;

    // CMusicPlayer fields (CMusicConsoleButton's base). _isActive is a bool, so it packs right after CGameObject's
    // _visible@0x120, the same way CBackground's _startFrame packs at 0x124.
    public const long PlayerIsActiveOffset = 0x121;
    public const long PlayerStopTargetOffset = 0x128; // CString

    // Indexed by MusicInstrument - 1. Bells (0) is left alone: its controls are locked, and the puzzle doesn't check it.
    private static readonly string[] Instruments = { "Snake", "Piano", "Bass" };

    // Raw _controlVal per instrument (Snake, Piano, Bass). CMusicRoom::setupMusic sets the target values, and
    // CMusicRoomHandler::checkInstrument wants pitch/speed to cancel them out and the switches to match. Pitch reports
    // _controlVal - 2, speed _controlVal - 1, the switches _controlVal.
    private static readonly Dictionary<string, int[]> CorrectValues = new()
    {
        ["Pitch"] = new[] { 0, 2, 1 },
        ["Speed"] = new[] { 0, 2, 3 },
        ["Inversion"] = new[] { 0, 1, 0 },
        ["Direction"] = new[] { 0, 1, 1 },
        ["Mute"] = new[] { 0, 0, 0 },
    };

    /// <summary>Whether the seed has the Easy Music Room Puzzle option on. False for seeds without the slot data key.</summary>
    public static bool IsEnabled(IReadOnlyDictionary<string, object>? slotData)
    {
        if (slotData is null || !slotData.TryGetValue(SlotDataKey, out object? raw))
            return false;
        try
        {
            return Convert.ToBoolean(raw);
        }
        catch
        {
            return false; // malformed slot data, treat as missing
        }
    }

    /// <summary>Sets every control on the Music Room panel to its correct value. Returns how many controls were
    /// written, and how many were missing or failed the layout check.</summary>
    public static (int Written, int Failed) Solve(MemoryReader mem, long project)
    {
        long? room = GameState.FindRoomByName(mem, project, RoomName);
        if (room is null)
            return (0, Instruments.Length * CorrectValues.Count);

        Dictionary<string, (string Control, int Instrument, int Value)> targets = BuildTargets();
        Dictionary<string, long> found = GameState.FindDescendantsByName(mem, room.Value, targets.Keys);

        int written = 0;
        foreach ((string name, long addr) in found)
        {
            (_, int instrument, int value) = targets[name];
            if (!IsExpectedControl(mem, addr, instrument, value))
                continue;

            if (mem.WriteInt32(addr + ControlValOffset, value))
                written++;
        }

        return (written, targets.Count - written);
    }

    /// <summary>True if the panel is set to the solution, judged the way CMusicRoomHandler::checkInstrument judges
    /// it: pitch and speed by exact value, and the switches only by whether they're on (_controlVal == 1).</summary>
    public static bool IsSolution(MemoryReader mem, long project)
    {
        long? room = GameState.FindRoomByName(mem, project, RoomName);
        if (room is null)
            return false;

        Dictionary<string, (string Control, int Instrument, int Value)> targets = BuildTargets();
        Dictionary<string, long> found = GameState.FindDescendantsByName(mem, room.Value, targets.Keys);
        if (found.Count != targets.Count)
            return false;

        foreach ((string name, long addr) in found)
        {
            (string control, int instrument, int value) = targets[name];
            if (!IsExpectedControl(mem, addr, instrument, value) || mem.ReadInt32(addr + ControlValOffset) is not int current)
                return false;

            bool matches = control is "Pitch" or "Speed" ? current == value : (current == 1) == (value == 1);
            if (!matches)
                return false;
        }

        return true;
    }

    /// <summary>Finds the panel's music console button (CMusicConsoleButton), whose press starts the music and is
    /// where the engine checks the solution.</summary>
    public static long? FindConsoleButton(MemoryReader mem, long project)
    {
        long? room = GameState.FindRoomByName(mem, project, RoomName);
        if (room is null)
            return null;

        long? console = GameState.FindDescendant(mem, room.Value, null, "CMusicConsoleButton");

        // Guards the derived offsets: _stopTarget must read as a string
        if (console is null || GameState.ReadCString(mem, console.Value + PlayerStopTargetOffset) is null)
            return null;
        return console;
    }

    /// <summary>Reads the console button's _isActive (music playing). Null if unreadable or not a bool.</summary>
    public static bool? ReadConsoleActive(MemoryReader mem, long consoleAddr) =>
        mem.ReadBytes(consoleAddr + PlayerIsActiveOffset, 1) is [byte raw] && raw <= 1 ? raw == 1 : null;

    /// <summary>Every puzzle control's name, mapped to its control kind, instrument and correct _controlVal.</summary>
    private static Dictionary<string, (string Control, int Instrument, int Value)> BuildTargets()
    {
        var targets = new Dictionary<string, (string Control, int Instrument, int Value)>(StringComparer.OrdinalIgnoreCase);
        foreach ((string control, int[] values) in CorrectValues)
        {
            for (int i = 0; i < Instruments.Length; i++)
                targets[$"{Instruments[i]} {control} Control"] = (control, i + 1, values[i]);
        }
        return targets;
    }

    /// <summary>Guards the derived offsets: a control must belong to its own instrument and be able to hold the value.</summary>
    private static bool IsExpectedControl(MemoryReader mem, long addr, int instrument, int value) =>
        mem.ReadInt32(addr + ControlAreaOffset) == instrument
        && mem.ReadInt32(addr + ControlMaxOffset) is int max && value <= max;
}
