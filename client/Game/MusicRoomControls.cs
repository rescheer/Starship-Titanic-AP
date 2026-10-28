namespace StarshipTitanicAp;

/// <summary>The Music Room panel's controls (CMusicControl subclasses, titanic/gfx/music_*.h) and the settings that
/// solve the puzzle, for the Easy Music Room Puzzle option.</summary>
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

        var targets = new Dictionary<string, (int Instrument, int Value)>(StringComparer.OrdinalIgnoreCase);
        foreach ((string control, int[] values) in CorrectValues)
        {
            for (int i = 0; i < Instruments.Length; i++)
                targets[$"{Instruments[i]} {control} Control"] = (i + 1, values[i]);
        }

        Dictionary<string, long> found = GameState.FindDescendantsByName(mem, room.Value, targets.Keys);

        int written = 0;
        foreach ((string name, long addr) in found)
        {
            (int instrument, int value) = targets[name];

            // Guards the derived offsets: a control must belong to its own instrument and be able to hold the value
            if (mem.ReadInt32(addr + ControlAreaOffset) != instrument
                || mem.ReadInt32(addr + ControlMaxOffset) is not int max || value > max)
            {
                continue;
            }

            if (mem.WriteInt32(addr + ControlValOffset, value))
                written++;
        }

        return (written, targets.Count - written);
    }
}
