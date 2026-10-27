namespace StarshipTitanicAp;

public sealed partial class MainForm
{
    /// <summary>Computes and caches whether it's safe to act on the currently attached save under the currently connected AP seed,
    /// re-verifying periodically once a verdict is reached.</summary>
    private void EvaluateSaveSeedGuard(long gameManager)
    {
        if (_saveSeedGuardState != SaveSeedGuardState.Unverified
            && _tickCount % SaveSeedGuardRecheckIntervalTicks != 0)
            return;
        if (_apConnection.SeedName is not { } seedName)
            return;
        if (_currentProject is not { } project)
            return;

        long? beamBridgeAddr = SaveSeedGuard.FindBeamBridgeAddress(_mem, project);
        if (beamBridgeAddr is null)
        {
            _saveSeedGuardBeamBridgeMisses++;
            if (_saveSeedGuardBeamBridgeMisses > SaveSeedGuardBeamBridgeMissLimit)
            {
                BlockSaveSeedGuard("Save/seed guard: could not locate the BeamBridge item to verify this save's AP seed tag - blocking AP actions defensively. Force with !force_seed in game or client.");
            }
            return;
        }
        _saveSeedGuardBeamBridgeMisses = 0;

        long tag = SaveSeedGuard.ComputeSeedTag(seedName);
        long? stored = SaveSeedGuard.ReadStoredSeedTag(_mem, beamBridgeAddr.Value);

        if (stored is null)
        {
            BlockSaveSeedGuard("Save/seed guard: failed to read BeamBridge's guard tag - blocking AP actions defensively");
            return;
        }

        if (stored.Value == tag)
        {
            _saveSeedGuardTagMismatches = 0;
            _saveSeedGuardState = SaveSeedGuardState.Ok;
            return;
        }

        if (stored.Value == 0)
        {
            _saveSeedGuardTagMismatches = 0;
            bool? petActive = GameState.ReadPetActive(_mem, gameManager);
            if (petActive == false)
            {
                if (SaveSeedGuard.WriteSeedTag(_mem, beamBridgeAddr.Value, tag))
                {
                    _saveSeedGuardState = SaveSeedGuardState.Ok;
                    const string taggedMessage = "Save/seed guard: tagged fresh save with current AP seed";
                    ShowActionResult(true, taggedMessage);
                    AppendServerLog($"CLIENT: {taggedMessage}", bold: true);

                    long? inventoryRoom = _currentInventoryRoom
                        ?? GameState.FindInventoryRoom(_mem, project);
                    if (inventoryRoom is not null)
                    {
                        GameActions.DisplayMessageSmart(_mem, inventoryRoom.Value,
                            "AP: This fresh save has been tagged and linked to the connected AP seed.");
                    }
                }
                else
                {
                    BlockSaveSeedGuard("Save/seed guard: failed to tag fresh save - blocking AP actions defensively.");
                }
            }
            else
            {
                BlockSaveSeedGuard("Save/seed guard: existing save has no AP seed tag (played without the client?) - blocking AP actions. Force with !force_seed in game or client.");
            }
            return;
        }

        // Requires several consecutive mismatched reads before blocking.
        _saveSeedGuardTagMismatches++;
        if (_saveSeedGuardTagMismatches < SaveSeedGuardTagMismatchLimit)
            return;

        BlockSaveSeedGuard("Save/seed guard: this save belongs to a different AP seed - blocking AP actions to avoid corrupting it. Force with !force_seed in game or client.");
    }

    /// <summary>Common landing spot for every way EvaluateSaveSeedGuard can end up Blocked.</summary>
    private void BlockSaveSeedGuard(string reason)
    {
        bool alreadyBlocked = _saveSeedGuardState == SaveSeedGuardState.Blocked;
        _saveSeedGuardState = SaveSeedGuardState.Blocked;
        if (alreadyBlocked)
            return;

        ShowActionResult(false, reason);
        AppendServerLog($"CLIENT: {reason}", bold: true);

        long? inventoryRoom = _currentInventoryRoom;
        if (inventoryRoom is null && _currentProject is { } project)
            inventoryRoom = GameState.FindInventoryRoom(_mem, project);

        if (inventoryRoom is not null)
        {
            GameActions.DisplayMessageSmart(_mem, inventoryRoom.Value,
                "AP: This save's seed tag doesn't match the connected seed, so item/check syncing is paused. " +
                "If you're sure this save and seed really belong together, type !force_seed in game or in the client to bypass the guard.");
        }
    }

    /// <summary>Surfaces a !force_seed failure in-game, best-effort (the inventory room may not be known yet, e.g. before attaching).</summary>
    private void ShowForceSeedFailureInGame(string text)
    {
        long? inventoryRoom = _currentInventoryRoom;
        if (inventoryRoom is null && _currentProject is { } project)
            inventoryRoom = GameState.FindInventoryRoom(_mem, project);

        if (inventoryRoom is not null)
            GameActions.DisplayMessageSmart(_mem, inventoryRoom.Value, text);
    }

    /// <summary>Handles the "!force_seed" command, a local-only override for the save/AP-seed guard.</summary>
    private void HandleForceSeedCommand()
    {
        if (!_mem.IsAttached || _currentGameManager is null)
        {
            ShowActionResult(false, "!force_seed: not attached / game not resolved yet");
            ShowForceSeedFailureInGame("AP: !force_seed failed - client isn't attached to the game yet.");
            return;
        }
        if (_apConnection.SeedName is not { } seedName)
        {
            ShowActionResult(false, "!force_seed: not connected to an AP server - no seed to tag with");
            ShowForceSeedFailureInGame("AP: !force_seed failed - not connected to an AP server.");
            return;
        }
        if (_currentProject is not { } project)
        {
            ShowActionResult(false, "!force_seed: project not resolved yet");
            ShowForceSeedFailureInGame("AP: !force_seed failed - game state not resolved yet, try again in a moment.");
            return;
        }

        long? beamBridgeAddr = SaveSeedGuard.FindBeamBridgeAddress(_mem, project);
        if (beamBridgeAddr is null)
        {
            ShowActionResult(false, "!force_seed: could not locate BeamBridge to tag");
            ShowForceSeedFailureInGame("AP: !force_seed failed - could not locate the BeamBridge item to tag.");
            return;
        }

        long tag = SaveSeedGuard.ComputeSeedTag(seedName);
        bool ok = SaveSeedGuard.WriteSeedTag(_mem, beamBridgeAddr.Value, tag);
        if (ok)
            _saveSeedGuardState = SaveSeedGuardState.Ok;

        string result = ok
            ? "!force_seed: save/AP-seed guard bypassed - this save is now tagged with the current seed"
            : "!force_seed: failed to write the guard tag";
        ShowActionResult(ok, result);
        AppendServerLog($"CLIENT: {result}", bold: true);

        if (_currentInventoryRoom is not null)
        {
            GameActions.DisplayMessageSmart(_mem, _currentInventoryRoom.Value, ok
                ? "AP: Save/seed guard bypassed - AP actions are re-enabled for this save."
                : "AP: !force_seed failed - see client log.");
        }
    }
}
