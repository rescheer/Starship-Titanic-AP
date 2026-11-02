namespace StarshipTitanicAp;

/// <summary>Keeps the Fuse Box's state in slot data storage for <see cref="RebuiltGame"/>, sending it only when it
/// changes. Only for seeds that can rebuild, so other seeds don't use the storage at all.</summary>
internal sealed class FuseBoxSync
{
    private const string DispenserRoomName = "SgtLobby";

    private readonly SessionContext _ctx;

    private long[]? _sockets;
    private long[]? _knobs;
    private long? _dispenser;
    private bool _notFoundReported;

    public FuseBoxSync(SessionContext ctx)
    {
        _ctx = ctx;
    }

    /// <summary>Forgets the addresses found in the current game tree. Call when the tree is rebuilt.</summary>
    public void Reset()
    {
        _sockets = null;
        _knobs = null;
        _dispenser = null;
        _notFoundReported = false;
    }

    public void Sync()
    {
        if (!_ctx.Ap.IsConnected || !QuickStart.IsEnabled(_ctx.Ap.SlotData) || _ctx.Project is not { } project)
            return;
        if (_sockets is null && !Resolve(project))
            return;
        if (Read() is not { } state)
            return;

        long encoded = state.Encode();
        long stored = _ctx.Ap.GetStoredValue(FuseBoxState.StorageKey) ?? FuseBoxState.Initial.Encode();
        if (encoded == stored)
            return;

        bool sent = _ctx.Ap.SetStoredValue(FuseBoxState.StorageKey, encoded);
        _ctx.Report.AppendLog(sent
            ? $"Fuse Box changed - stored as {encoded}"
            : $"Fuse Box changed, but couldn't be stored - will retry");
    }

    private bool Resolve(long project)
    {
        long? view = GameState.FindFuseBoxView(_ctx.Mem, project);
        long? dispenserRoom = GameState.FindRoomByName(_ctx.Mem, project, DispenserRoomName);
        long? dispenser = dispenserRoom is { } room
            ? GameState.FindDescendantByName(_ctx.Mem, room, FuseBoxState.ChickenDispenserName)
            : null;

        long[] Find(IEnumerable<string> names) => names
            .Select(name => view is { } v ? GameState.FindDescendantByName(_ctx.Mem, v, name) ?? 0 : 0)
            .ToArray();
        long[] sockets = Find(FuseBoxState.AllSockets.Select(socket => socket.Name));
        long[] knobs = Find(FuseBoxState.Knobs);

        if (dispenser is null || sockets.Contains(0) || knobs.Contains(0))
        {
            if (!_notFoundReported)
            {
                _notFoundReported = true;
                _ctx.Report.ReportAction(false, "Fuse Box objects not found - its state won't be stored");
            }
            return false;
        }

        _sockets = sockets;
        _knobs = knobs;
        _dispenser = dispenser;
        return true;
    }

    /// <summary>Null if anything couldn't be read, or a socket holds something other than a fuse.</summary>
    private FuseBoxState? Read()
    {
        var sockets = new string?[_sockets!.Length];
        for (int i = 0; i < sockets.Length; i++)
        {
            string? item = GameState.ReadShipSettingItemName(_ctx.Mem, _sockets[i]);
            if (item is null || (item != GameOffsets.ShipSettingEmpty && !FuseBoxState.Fuses.Contains(item)))
                return null;
            sockets[i] = item == GameOffsets.ShipSettingEmpty ? null : item;
        }

        var pressed = new bool[_knobs!.Length];
        var enabled = new bool[_knobs.Length];
        for (int i = 0; i < _knobs.Length; i++)
        {
            if (_ctx.Mem.ReadBytes(_knobs[i] + GameOffsets.ShipSettingButtonPressedOffset, 2) is not [byte knobPressed, byte knobEnabled])
                return null;
            pressed[i] = knobPressed != 0;
            enabled[i] = knobEnabled != 0;
        }

        if (_ctx.Mem.ReadInt32(_dispenser!.Value + GameOffsets.ChickenDispensorModeOffset) is not { } mode)
            return null;
        return new FuseBoxState(sockets, pressed, enabled, mode);
    }
}
