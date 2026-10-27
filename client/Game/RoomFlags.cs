namespace StarshipTitanicAp;

/// <summary>Bit-level decode for the room-flags scheme used by CGameObject::_roomFlags/_destRoomFlags.</summary>
public static class RoomFlags
{
    public const uint FirstClassSuite = 0x59706;

    /// <summary>True if this looks like one of the fixed named-room constants rather than a dynamically computed per-stateroom code.</summary>
    public static bool IsNamedRoom(uint roomFlags) => (roomFlags & 1) != 0;

    /// <summary>Decodes a room-flags value into its four components.</summary>
    public static (int elevatorNum, int classNum, int floorNum, int roomNum) Decode(uint roomFlags)
    {
        int elevatorNum = (int)((roomFlags >> 18) & 3) + 1;
        int classNum = (int)((roomFlags >> 16) & 3);
        int floorNum = DecodeFloor(roomFlags);
        int roomNum = (int)((roomFlags >> 1) & 0x7F);
        return (elevatorNum, classNum, floorNum, roomNum);
    }

    /// <summary>Decodes a floor number from its packed byte representation.</summary>
    private static int DecodeFloor(uint roomFlags)
    {
        uint bits = (roomFlags >> 8) & 0xFF;
        uint offset = bits & 0xF;
        uint hi = (bits >> 4) & 0xF;
        int baseVal = hi switch
        {
            9 => 0,
            0xD => 10,
            0xE => 20,
            0xF => 30,
            _ => 40,
        };
        return offset >= 10 ? 0 : baseVal + (int)offset;
    }
}
