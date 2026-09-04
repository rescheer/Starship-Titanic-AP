namespace StarshipTitanicAp;

/// <summary>Bit-level encode/decode for the room-flags scheme used by CGameObject::_roomFlags/_destRoomFlags.</summary>
public static class RoomFlags
{
    public const uint FirstClassSuite = 0x59706;

    /// <summary>True if this looks like one of the fixed named-room constants rather than a dynamically computed per-stateroom code.</summary>
    public static bool IsNamedRoom(uint roomFlags) => (roomFlags & 1) != 0;

    /// <summary>Builds a room-flags value from its four components in one step.</summary>
    public static uint Compute(int elevatorNum, int classNum, int floorNum, int roomNum)
    {
        uint flags = 0;

        flags |= (uint)(((elevatorNum - 1) & 3) << 18);
        flags |= (uint)((classNum & 3) << 16);
        flags |= EncodeFloor(floorNum);
        flags |= (uint)((roomNum & 0x7F) << 1);

        return flags;
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

    /// <summary>Maps a bare floor number to its passenger class.</summary>
    public static int WhatPassengerClass(int floorNum)
    {
        if (floorNum >= 2 && floorNum <= 19)
            return (int)PassengerClass.First;
        return (floorNum >= 20 && floorNum <= 27) ? (int)PassengerClass.Second : (int)PassengerClass.Third;
    }

    /// <summary>Encodes a floor number into its packed byte representation.</summary>
    private static uint EncodeFloor(int floorNum)
    {
        uint baseVal = (floorNum / 10) switch
        {
            0 => 0x90u,
            1 => 0xD0u,
            2 => 0xE0u,
            3 => 0xF0u,
            _ => 0u,
        };
        return (baseVal | (uint)(floorNum % 10)) << 8;
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
