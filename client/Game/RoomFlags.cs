namespace StarshipTitanicAp;

/// <summary>Bit-level encode/decode for the room-flags scheme used by CGameObject::_roomFlags/_destRoomFlags.</summary>
public static class RoomFlags
{
    public const uint FirstClassSuite = 0x59706;

    /// <summary>True if this looks like one of the fixed named-room constants rather than a dynamically computed per-stateroom code.</summary>
    public static bool IsNamedRoom(uint roomFlags) => (roomFlags & 1) != 0;

    /// <summary>Encodes a stateroom's room flags from its four components (the inverse of <see cref="Decode"/>).</summary>
    public static uint Encode(int elevatorNum, int classNum, int floorNum, int roomNum) =>
        (uint)((elevatorNum - 1) & 3) << 18
        | (uint)(classNum & 3) << 16
        | EncodeFloor(floorNum) << 8
        | (uint)(roomNum & 0x7F) << 1;

    /// <summary>CRoomFlags::compareLocation: the same elevator, floor and room, whatever the class bits say.</summary>
    public static bool CompareLocation(uint flags1, uint flags2)
    {
        (int elevator1, _, int floor1, int room1) = Decode(flags1);
        (int elevator2, _, int floor2, int room2) = Decode(flags2);
        return elevator1 == elevator2 && floor1 == floor2 && room1 == room2;
    }

    /// <summary>Decodes a room-flags value into its four components.</summary>
    public static (int elevatorNum, int classNum, int floorNum, int roomNum) Decode(uint roomFlags)
    {
        int elevatorNum = (int)((roomFlags >> 18) & 3) + 1;
        int classNum = (int)((roomFlags >> 16) & 3);
        int floorNum = DecodeFloor(roomFlags);
        int roomNum = (int)((roomFlags >> 1) & 0x7F);
        return (elevatorNum, classNum, floorNum, roomNum);
    }

    /// <summary>Encodes a floor number into its packed byte representation (CRoomFlags::setFloorNum, with no +4 offset).</summary>
    private static uint EncodeFloor(int floorNum)
    {
        uint baseVal = (floorNum / 10) switch
        {
            0 => 0x90,
            1 => 0xD0,
            2 => 0xE0,
            3 => 0xF0,
            _ => 0,
        };
        return baseVal | (uint)(floorNum % 10);
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
