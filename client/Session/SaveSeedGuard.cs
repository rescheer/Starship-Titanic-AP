namespace StarshipTitanicAp;

public enum SaveSeedGuardState
{
    /// <summary>Not enough information yet - treated as "don't act" by callers, same as Blocked.</summary>
    Unverified,
    Ok,
    Blocked,
}

/// <summary>Outcome of <see cref="SaveSeedGuard.ForceTag"/>.</summary>
public enum ForceTagResult
{
    NotAttached,
    GameNotResolved,
    NotConnected,
    ProjectNotResolved,
    BeamBridgeNotFound,
    WriteFailed,
    Tagged,
}

/// <summary>Decides whether it's safe to sync the attached save with the connected AP seed, by checking the seed tag
/// stored on the save's BeamBridge item (see <see cref="SaveSeedTag"/>). Tags fresh saves automatically; anything
/// else that doesn't match blocks AP syncing until the player overrides it with <see cref="ForceTag"/>.</summary>
internal sealed class SaveSeedGuard
{
    private const int BeamBridgeMissLimit = 100; // 100 * 50ms = 5s
    private const int TagMismatchLimit = 40; // 40 * 50ms = 2s of consecutive mismatched reads before blocking
    private const int RecheckIntervalTicks = 40; // every 40 * 50ms = 2s, once verified

    private readonly SessionContext _ctx;
    private int _beamBridgeMisses;
    private int _tagMismatches;

    public SaveSeedGuard(SessionContext ctx)
    {
        _ctx = ctx;
    }

    public SaveSeedGuardState State { get; private set; } = SaveSeedGuardState.Unverified;

    /// <summary>Forgets any verdict, e.g. after a save load or a new AP connection.</summary>
    public void Reset()
    {
        State = SaveSeedGuardState.Unverified;
        _beamBridgeMisses = 0;
        _tagMismatches = 0;
    }

    /// <summary>Computes and caches whether it's safe to act on the currently attached save under the currently connected AP seed,
    /// re-verifying periodically once a verdict is reached.</summary>
    public void Evaluate(long gameManager)
    {
        if (State != SaveSeedGuardState.Unverified
            && _ctx.TickCount % RecheckIntervalTicks != 0)
            return;
        if (_ctx.Ap.SeedName is not { } seedName)
            return;
        if (_ctx.Project is not { } project)
            return;

        long? beamBridgeAddr = SaveSeedTag.FindBeamBridgeAddress(_ctx.Mem, project);
        if (beamBridgeAddr is null)
        {
            _beamBridgeMisses++;
            if (_beamBridgeMisses > BeamBridgeMissLimit)
            {
                Block("Save/seed guard: could not locate the BeamBridge item to verify this save's AP seed tag - blocking AP actions defensively. Force with !force_seed in game or client.");
            }
            return;
        }
        _beamBridgeMisses = 0;

        long tag = SaveSeedTag.ComputeSeedTag(seedName);
        long? stored = SaveSeedTag.ReadStoredSeedTag(_ctx.Mem, beamBridgeAddr.Value);

        if (stored is null)
        {
            Block("Save/seed guard: failed to read BeamBridge's guard tag - blocking AP actions defensively");
            return;
        }

        if (stored.Value == tag)
        {
            _tagMismatches = 0;
            State = SaveSeedGuardState.Ok;
            return;
        }

        if (stored.Value == 0)
        {
            _tagMismatches = 0;
            bool? petActive = GameState.ReadPetActive(_ctx.Mem, gameManager);
            if (petActive == false)
            {
                if (SaveSeedTag.WriteSeedTag(_ctx.Mem, beamBridgeAddr.Value, tag))
                {
                    State = SaveSeedGuardState.Ok;
                    const string taggedMessage = "Save/seed guard: tagged fresh save with current AP seed";
                    _ctx.Report.ReportAction(true, taggedMessage);
                    _ctx.Report.AppendServerLog($"CLIENT: {taggedMessage}", bold: true);
                    _ctx.ShowPetMessage("AP: This fresh save has been tagged and linked to the connected AP seed.");
                }
                else
                {
                    Block("Save/seed guard: failed to tag fresh save - blocking AP actions defensively.");
                }
            }
            else
            {
                Block("Save/seed guard: existing save has no AP seed tag (played without the client?) - blocking AP actions. Force with !force_seed in game or client.");
            }
            return;
        }

        // Requires several consecutive mismatched reads before blocking.
        _tagMismatches++;
        if (_tagMismatches < TagMismatchLimit)
            return;

        Block("Save/seed guard: this save belongs to a different AP seed - blocking AP actions to avoid corrupting it. Force with !force_seed in game or client.");
    }

    /// <summary>Unconditionally overwrites BeamBridge's save/seed guard tag with the currently connected AP seed,
    /// unblocking the guard. Reports nothing itself - callers word the outcome for wherever they were invoked from.</summary>
    public ForceTagResult ForceTag()
    {
        if (!_ctx.Mem.IsAttached)
            return ForceTagResult.NotAttached;
        if (_ctx.GameManager is null)
            return ForceTagResult.GameNotResolved;
        if (_ctx.Ap.SeedName is not { } seedName)
            return ForceTagResult.NotConnected;
        if (_ctx.Project is not { } project)
            return ForceTagResult.ProjectNotResolved;

        long? beamBridgeAddr = SaveSeedTag.FindBeamBridgeAddress(_ctx.Mem, project);
        if (beamBridgeAddr is null)
            return ForceTagResult.BeamBridgeNotFound;

        long tag = SaveSeedTag.ComputeSeedTag(seedName);
        if (!SaveSeedTag.WriteSeedTag(_ctx.Mem, beamBridgeAddr.Value, tag))
            return ForceTagResult.WriteFailed;

        State = SaveSeedGuardState.Ok;
        return ForceTagResult.Tagged;
    }

    /// <summary>Common landing spot for every way <see cref="Evaluate"/> can end up Blocked.</summary>
    private void Block(string reason)
    {
        bool alreadyBlocked = State == SaveSeedGuardState.Blocked;
        State = SaveSeedGuardState.Blocked;
        if (alreadyBlocked)
            return;

        _ctx.Report.ReportAction(false, reason);
        _ctx.Report.AppendServerLog($"CLIENT: {reason}", bold: true);
        _ctx.ShowPetMessage("AP: This save's seed tag doesn't match the connected seed, so item/check syncing is paused. " +
            "If you're sure this save and seed really belong together, type !force_seed in game or in the client to bypass the guard.");
    }
}
