namespace StarshipTitanicAp;

/// <summary>One of the Fuse Box's four sockets (CShipSetting), the object showing its fuse, and the fuse it starts the
/// game holding.</summary>
internal readonly record struct FuseBoxSocket(string Name, string FrameTarget, string? StartingFuse);

/// <summary>The Fuse Box as the game leaves it: what's in each socket, each knob's two flags, and the chicken dispenser's
/// mode. Packed into one integer for slot data storage. Everything else the Fuse Box touches follows from these, as a knob
/// only changes its target's flag together with its own. The flags can't be worked out from the sockets, since
/// ItemSync empties a socket without telling its knob, and Creator's Chamber relies on the Red Fuse's knob staying pressed.
/// </summary>
internal sealed record FuseBoxState(string?[] Sockets, bool[] Pressed, bool[] Enabled, int DispenseMode)
{
    /// <summary>The data storage key this is kept under.</summary>
    public const string StorageKey = "fuse_box";

    public const string BeamFuse = "BeamBridge";
    public const string ChickenFuse = "ChickenBridge";
    public const string FanFuse = "FanBridge";
    public const string SeasonFuse = "SeasonBridge";

    public const string HeadSmasherName = "HeadSmasher";
    public const string ChickenDispenserName = "ChickenDispenser";
    public const string FanControllerName = "FanController";
    public const string SeasonalAdjustName = "SeasonalAdjust";

    public const int DispenseHot = 1;

    /// <summary>Encoded with the position in this list plus one, and 0 for an empty socket, so only append to it.</summary>
    public static readonly string[] Fuses = { BeamFuse, ChickenFuse, FanFuse, SeasonFuse };

    public static readonly FuseBoxSocket[] AllSockets =
    {
        new("BeamSetting", "BeamAnim", BeamFuse),
        new("ChickenSetting", "ChickenAnim", ChickenFuse),
        new("FanSetting", "FanAnim", null),
        new("SeasonSetting", "SeasonAnim", null),
    };

    /// <summary>In the order of <see cref="Pressed"/> and <see cref="Enabled"/>. The last has no socket.</summary>
    public static readonly string[] Knobs = { "BeamKnob", "ChickenKnob", "FanKnob", "SeasonKnob", "SUBKnob" };
    public const int SuccUBusKnobIndex = 4;

    private const int SocketBits = 3;
    private const int SocketMask = (1 << SocketBits) - 1;
    private static readonly int PressedShift = AllSockets.Length * SocketBits;
    private static readonly int EnabledShift = PressedShift + Knobs.Length;
    private static readonly int DispenseModeShift = EnabledShift + Knobs.Length;
    private const int DispenseModeMask = 3;

    /// <summary>A new game's Fuse Box.</summary>
    public static readonly FuseBoxState Initial = new(
        AllSockets.Select(socket => socket.StartingFuse).ToArray(),
        new[] { true, true, false, false, true },
        new bool[Knobs.Length],
        DispenseHot);

    public long Encode()
    {
        long packed = (long)DispenseMode << DispenseModeShift;
        for (int i = 0; i < Sockets.Length; i++)
            packed |= (long)(Array.IndexOf(Fuses, Sockets[i]) + 1) << (i * SocketBits);
        for (int i = 0; i < Knobs.Length; i++)
        {
            if (Pressed[i])
                packed |= 1L << (PressedShift + i);
            if (Enabled[i])
                packed |= 1L << (EnabledShift + i);
        }
        return packed;
    }

    /// <summary>Null for no value, or one this client can't make sense of.</summary>
    public static FuseBoxState? Decode(long? value)
    {
        if (value is not { } packed || packed < 0 || packed >> (DispenseModeShift + 2) != 0)
            return null;

        var sockets = new string?[AllSockets.Length];
        for (int i = 0; i < sockets.Length; i++)
        {
            int fuse = (int)(packed >> (i * SocketBits)) & SocketMask;
            if (fuse > Fuses.Length)
                return null;
            sockets[i] = fuse == 0 ? null : Fuses[fuse - 1];
        }

        var pressed = new bool[Knobs.Length];
        var enabled = new bool[Knobs.Length];
        for (int i = 0; i < Knobs.Length; i++)
        {
            pressed[i] = (packed >> (PressedShift + i) & 1) != 0;
            enabled[i] = (packed >> (EnabledShift + i) & 1) != 0;
        }
        return new FuseBoxState(sockets, pressed, enabled, (int)(packed >> DispenseModeShift) & DispenseModeMask);
    }

    /// <summary>The frame CShipSetting::EnterRoomMsg shows a socket's fuse with.</summary>
    public static int FrameFor(string? fuse) => Array.IndexOf(new[] { ChickenFuse, FanFuse, SeasonFuse, BeamFuse }, fuse) + 1;
}
