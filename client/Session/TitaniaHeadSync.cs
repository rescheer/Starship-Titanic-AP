namespace StarshipTitanicAp;

/// <summary>Keeps what's installed in Titania in slot data storage for <see cref="RebuiltGame"/>, sending it only when it
/// changes. Only for seeds that can rebuild, so other seeds don't use the storage at all.</summary>
internal sealed class TitaniaHeadSync
{
    private const int TitaniaRoom = 37;

    private readonly SessionContext _ctx;

    private long[]? _faceSlots;
    private long _coreSlot;
    private long[]? _brainSlots;
    private bool _notFoundReported;

    public TitaniaHeadSync(SessionContext ctx)
    {
        _ctx = ctx;
    }

    /// <summary>Forgets the addresses found in the current game tree. Call when the tree is rebuilt.</summary>
    public void Reset()
    {
        _faceSlots = null;
        _brainSlots = null;
        _notFoundReported = false;
    }

    public void Sync()
    {
        if (!_ctx.Ap.IsConnected || !QuickStart.IsEnabled(_ctx.Ap.SlotData) || _ctx.Project is not { } project)
            return;
        if (_faceSlots is null && !Resolve(project))
            return;
        if (Read() is not { } state)
            return;

        long encoded = state.Encode();
        long stored = _ctx.Ap.GetStoredValue(TitaniaHeadState.StorageKey) ?? TitaniaHeadState.Initial.Encode();
        if (encoded == stored)
            return;

        bool sent = _ctx.Ap.SetStoredValue(TitaniaHeadState.StorageKey, encoded);
        _ctx.Report.AppendLog(sent
            ? $"Titania's parts changed - stored as {encoded}"
            : $"Titania's parts changed, but couldn't be stored - will retry");
    }

    private bool Resolve(long project)
    {
        long? room = GameState.FindRoomByName(_ctx.Mem, project, RoomNames.GetName(TitaniaRoom));

        long Find(string name) => room is { } r ? GameState.FindDescendantByName(_ctx.Mem, r, name) ?? 0 : 0;
        long[] faceSlots = TitaniaHeadState.FaceSlots.Select(slot => Find(slot.Name)).ToArray();
        long[] brainSlots = TitaniaHeadState.BrainSlots.Select(slot => Find(slot.Name)).ToArray();
        long coreSlot = Find(TitaniaHeadState.CoreSlotName);

        if (coreSlot == 0 || faceSlots.Contains(0) || brainSlots.Contains(0))
        {
            if (!_notFoundReported)
            {
                _notFoundReported = true;
                _ctx.Report.ReportAction(false, "Titania's slots not found - her parts won't be stored");
            }
            return false;
        }

        _faceSlots = faceSlots;
        _brainSlots = brainSlots;
        _coreSlot = coreSlot;
        return true;
    }

    /// <summary>Null if anything couldn't be read, or a brain slot holds something other than a card.</summary>
    private TitaniaHeadState? Read()
    {
        var face = new bool[_faceSlots!.Length];
        for (int i = 0; i < face.Length; i++)
        {
            if (ReadOccupied(_faceSlots[i] + GameOffsets.HeadSlotOccupiedOffset) is not { } occupied)
                return null;
            face[i] = occupied;
        }

        if (ReadOccupied(_coreSlot + GameOffsets.BrainSlotOccupiedOffset) is not { } core)
            return null;

        var brain = new string?[_brainSlots!.Length];
        for (int i = 0; i < brain.Length; i++)
        {
            if (ReadOccupied(_brainSlots[i] + GameOffsets.BrainSlotOccupiedOffset) is not { } occupied)
                return null;
            if (!occupied)
                continue;

            string? card = GameState.ReadCString(_ctx.Mem, _brainSlots[i] + GameOffsets.BrainSlotTargetOffset);
            if (card is null || !TitaniaHeadState.BrainSlots.Any(slot => slot.Card == card))
                return null;
            brain[i] = card;
        }

        return new TitaniaHeadState(face, core, brain);
    }

    private bool? ReadOccupied(long address) =>
        _ctx.Mem.ReadBytes(address, 1) is [byte occupied] ? occupied != 0 : null;
}
