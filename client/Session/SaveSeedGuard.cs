namespace StarshipTitanicAp;

public enum SaveSeedGuardState
{
    /// <summary>Not enough information yet - treated as "don't act" by callers, same as Blocked.</summary>
    Unverified,
    Ok,
    Blocked,
}

/// <summary>Decides whether it's safe to sync the attached save with the connected AP seed, by checking the seed tag
/// stored on the save's BeamBridge item (see <see cref="SaveSeedTag"/>). Tags fresh saves automatically; anything
/// else that doesn't match blocks AP syncing until the player overrides it with !force_seed.</summary>
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
                    ShowInGame("AP: This fresh save has been tagged and linked to the connected AP seed.");
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

    /// <summary>Handles the "!force_seed" command, a local-only override for the save/AP-seed guard.</summary>
    public void HandleForceSeedCommand()
    {
        if (!_ctx.Mem.IsAttached || _ctx.GameManager is null)
        {
            _ctx.Report.ReportAction(false, "!force_seed: not attached / game not resolved yet");
            ShowInGame("AP: !force_seed failed - client isn't attached to the game yet.");
            return;
        }
        if (_ctx.Ap.SeedName is not { } seedName)
        {
            _ctx.Report.ReportAction(false, "!force_seed: not connected to an AP server - no seed to tag with");
            ShowInGame("AP: !force_seed failed - not connected to an AP server.");
            return;
        }
        if (_ctx.Project is not { } project)
        {
            _ctx.Report.ReportAction(false, "!force_seed: project not resolved yet");
            ShowInGame("AP: !force_seed failed - game state not resolved yet, try again in a moment.");
            return;
        }

        long? beamBridgeAddr = SaveSeedTag.FindBeamBridgeAddress(_ctx.Mem, project);
        if (beamBridgeAddr is null)
        {
            _ctx.Report.ReportAction(false, "!force_seed: could not locate BeamBridge to tag");
            ShowInGame("AP: !force_seed failed - could not locate the BeamBridge item to tag.");
            return;
        }

        long tag = SaveSeedTag.ComputeSeedTag(seedName);
        bool ok = SaveSeedTag.WriteSeedTag(_ctx.Mem, beamBridgeAddr.Value, tag);
        if (ok)
            State = SaveSeedGuardState.Ok;

        string result = ok
            ? "!force_seed: save/AP-seed guard bypassed - this save is now tagged with the current seed"
            : "!force_seed: failed to write the guard tag";
        _ctx.Report.ReportAction(ok, result);
        _ctx.Report.AppendServerLog($"CLIENT: {result}", bold: true);
        ShowInGame(ok
            ? "AP: Save/seed guard bypassed - AP actions are re-enabled for this save."
            : "AP: !force_seed failed - see client log.");
    }

    /// <summary>Unconditionally overwrites BeamBridge's save/seed guard tag with the currently connected AP seed.</summary>
    public void ForceTag()
    {
        if (!_ctx.Mem.IsAttached)
        {
            _ctx.Report.ReportAction(false, "Not attached");
            return;
        }
        if (_ctx.GameManager is null)
        {
            _ctx.Report.ReportAction(false, "gameManager not resolved yet (menu / loading?)");
            return;
        }
        if (_ctx.Ap.SeedName is not { } seedName)
        {
            _ctx.Report.ReportAction(false, "Not connected to an AP server - no seed to tag with");
            return;
        }
        if (_ctx.Project is not { } project)
        {
            _ctx.Report.ReportAction(false, "Project not resolved yet");
            return;
        }

        long? beamBridgeAddr = SaveSeedTag.FindBeamBridgeAddress(_ctx.Mem, project);
        if (beamBridgeAddr is null)
        {
            _ctx.Report.ReportAction(false, "Could not locate BeamBridge to tag");
            return;
        }

        long tag = SaveSeedTag.ComputeSeedTag(seedName);
        bool ok = SaveSeedTag.WriteSeedTag(_ctx.Mem, beamBridgeAddr.Value, tag);
        if (ok)
            State = SaveSeedGuardState.Ok;
        _ctx.Report.ReportAction(ok, "Force-tagged save with current AP seed");
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
        ShowInGame("AP: This save's seed tag doesn't match the connected seed, so item/check syncing is paused. " +
            "If you're sure this save and seed really belong together, type !force_seed in game or in the client to bypass the guard.");
    }

    /// <summary>Shows a PET message, best-effort: looks the PET control up if it isn't resolved yet, and does nothing
    /// if it can't be found (e.g. before attaching).</summary>
    private void ShowInGame(string text)
    {
        long? inventoryRoom = _ctx.InventoryRoom;
        if (inventoryRoom is null && _ctx.Project is { } project)
            inventoryRoom = GameState.FindInventoryRoom(_ctx.Mem, project);

        if (inventoryRoom is not null)
            GameActions.DisplayMessageSmart(_ctx.Mem, inventoryRoom.Value, text);
    }
}
