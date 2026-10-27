namespace StarshipTitanicAp;

public sealed partial class MainForm
{
    private const string TitaniaRepairedLocation = "Titania's Room - Repair Titania";

    /// <summary>Applies the class upgrade implied by AP items received so far, if any. With no class items yet,
    /// clamps a First/Second class (only reachable via a spoof that was saved or never reverted) back to Third.
    /// While a class spoof is active, updates the spoof's backed-up original class instead of the live value, so
    /// <see cref="TryRevertClassUpgradeSpoof"/> restores the new class rather than the stale one.</summary>
    private void SyncPassengerClassFromItems(long gameManager)
    {
        IReadOnlyDictionary<string, object>? slotData = _apConnection.SlotData;
        if (slotData is null)
            return;
        if (_currentInventoryRoom is null)
            return;

        string[] receivedItems = _apConnection.GetReceivedItemNames();
        if (receivedItems.Length == _lastItemsReceivedCount)
            return;
        _lastItemsReceivedCount = receivedItems.Length;

        int? currentClass = _classUpgradeSpoofOriginalClass ?? GameState.ReadPassengerClass(_mem, gameManager);
        if (currentClass is null)
            return;

        // Bridge access shares its raw value with None - never overwrite it once Titania has been repaired.
        if (currentClass.Value == SpecialPassengerClassValues.BridgeAccessClassValue
            && _apConnection.IsLocationChecked(TitaniaRepairedLocation))
            return;

        int? targetClass = ClassUpgradeTracker.ComputeClass(receivedItems, slotData);
        if (targetClass is null)
        {
            if (currentClass.Value != (int)PassengerClass.First && currentClass.Value != (int)PassengerClass.Second)
                return;
            targetClass = (int)PassengerClass.Third;
        }

        if (currentClass == targetClass)
            return;

        if (_classUpgradeSpoofOriginalClass is not null)
        {
            _classUpgradeSpoofOriginalClass = targetClass.Value;
            ShowActionResult(true, $"Class upgrade from items: {PassengerClassNames.GetName(targetClass.Value)} (applied once the class spoof ends)");
            return;
        }

        bool ok = GameActions.SetPassengerClassFull(_mem, gameManager, _currentInventoryRoom.Value, targetClass.Value);
        ShowActionResult(ok, $"Class upgrade from items: {PassengerClassNames.GetName(targetClass.Value)}");
    }

    /// <summary>Applies the stateroom assignment(s) implied by "Progressive Stateroom" AP items received so far,
    /// if any are still outstanding, via GameActions.AssignNextRoom. Assigns every outstanding tier in order:
    /// SGT/Third, then Second, then First.</summary>
    private void SyncStateroomFromItems(long gameManager)
    {
        IReadOnlyDictionary<string, object>? slotData = _apConnection.SlotData;
        if (slotData is null)
            return;
        if (_currentInventoryRoom is not { } petControlAddr)
            return;

        string[] receivedItems = _apConnection.GetReceivedItemNames();
        if (receivedItems.Length == _lastStateroomItemsReceivedCount)
            return;
        _lastStateroomItemsReceivedCount = receivedItems.Length;

        int? targetCount = StateroomAssignTracker.ComputeTargetAssignedCount(receivedItems, slotData);
        if (targetCount is null)
            return;

        int achievedCount = GameActions.GetAchievedStateroomClass(_mem, petControlAddr);
        for (int next = achievedCount + 1; next <= targetCount.Value; next++)
        {
            if (StateroomAssignTracker.ClassForAssignedCount(next) is not { } rawClass)
                break;

            bool ok = GameActions.AssignNextRoom(_mem, gameManager, petControlAddr, rawClass);
            ShowActionResult(ok, $"Stateroom assignment from items: {PassengerClassNames.GetName(rawClass)}");
            if (!ok)
                break;
        }
    }

    /// <summary>The DeskBot's own RNV in the Embarkation Lobby, where both class-upgrade interactions happen.</summary>
    private static readonly RoomNodeView DeskBotRnv = new(2, 4, 1);

    /// <summary>The Embarkation Lobby RNV the game force-turns the player to right after the DeskBot's very first
    /// (No Class) conversation, for a scripted Bellbot intro. An active spoof reports Third here; this RNV never
    /// starts a spoof on its own.</summary>
    private static readonly RoomNodeView BellBotIntroRnv = new(2, 4, 2);

    /// <summary>Every RNV that can trigger or sustain a class-upgrade spoof, mapped to the PassengerClass value it
    /// should report there (raw values: 4 = no class, ..., 1 = First, 0 = First with Bridge access). Covers the
    /// DeskBot, the SGT Class Stateroom and its TV, the [22,1,2] corridor spot leading into it, and the doorway
    /// RNVs just outside each bottom-floor SGT room.</summary>
    private static readonly Dictionary<RoomNodeView, int> ClassSpoofTriggerRnvs = new()
    {
        [DeskBotRnv] = (int)PassengerClass.Third,
        [new RoomNodeView(27, 4, 1)] = (int)PassengerClass.Third,  // SGT TV for Magazine puzzle
        // SGT Staterooms and ground-floor entrances for the Magazine voice line
        [new RoomNodeView(27, 1, 2)] = (int)PassengerClass.Third,  // SGT Stateroom
        [new RoomNodeView(22, 1, 2)] = (int)PassengerClass.Third,  // SGT LittleLift
        [new RoomNodeView(11, 4, 2)] = (int)PassengerClass.Third,  // SGT Room 1
        [new RoomNodeView(11, 5, 2)] = (int)PassengerClass.Third,  // SGT Room 2
        [new RoomNodeView(11, 6, 2)] = (int)PassengerClass.Third,  // SGT Room 3
        [new RoomNodeView(11, 7, 2)] = (int)PassengerClass.Third,  // SGT Room 4
        [new RoomNodeView(11, 8, 2)] = (int)PassengerClass.Third,  // SGT Room 5
        [new RoomNodeView(11, 9, 2)] = (int)PassengerClass.Third,  // SGT Room 6
    };

    /// <summary>Backs up the true PassengerClass value while <see cref="TryApplyClassUpgradeSpoof"/> has temporarily
    /// overwritten it, so <see cref="TryRevertClassUpgradeSpoof"/> can put it back. Null when not currently spoofed.</summary>
    private int? _classUpgradeSpoofOriginalClass;

    /// <summary>Temporarily overwrites PassengerClass while the player is at one of <see cref="ClassSpoofTriggerRnvs"/>
    /// (or continues an active spoof at <see cref="BellBotIntroRnv"/>), so the vanilla class-gated DeskBot/SGT TV
    /// interactions run. Reports Third for a pending Magazine hand-off, Second for a pending 1st Class upgrade, and
    /// None at the DeskBot when no room has been assigned yet. The true value is saved in
    /// <see cref="_classUpgradeSpoofOriginalClass"/>.</summary>
    private void TryApplyClassUpgradeSpoof(RoomNodeView rnv, long gameManager)
    {
        if (rnv == BellBotIntroRnv)
        {
            // Only continues an already-active spoof.
            if (_classUpgradeSpoofOriginalClass is null)
                return;

            int? reportedClass = GameState.ReadPassengerClass(_mem, gameManager);
            if (reportedClass is not null && reportedClass.Value != (int)PassengerClass.Third
                && GameActions.SetPassengerClass(_mem, gameManager, (int)PassengerClass.Third))
            {
                ShowActionResult(true, $"Class upgrade spoof: reporting {PassengerClassNames.GetName((int)PassengerClass.Third)} for Bellbot intro (really {PassengerClassNames.GetName(_classUpgradeSpoofOriginalClass.Value)})");
            }
            return;
        }

        if (!ClassSpoofTriggerRnvs.TryGetValue(rnv, out int configuredSpoofClass))
            return;
        if (_currentProject is not { } project || _currentInventoryRoom is null)
            return;
        if (_classUpgradeSpoofOriginalClass is not null)
            return;
        if (!LocationChecks.TryGetClassUpgradeLocationName((int)PassengerClass.Second, out string secondLocationName))
            return;
        if (!LocationChecks.TryGetClassUpgradeLocationName((int)PassengerClass.First, out string firstLocationName))
            return;

        int? currentClass = GameState.ReadPassengerClass(_mem, gameManager);
        if (currentClass is null)
            return;

        int? spoofToClass = null;
        // Queued counts as checked, so a check made while offline still advances the spoof.
        bool secondClassPending = !_apConnection.IsLocationCheckedOrQueued(secondLocationName);
        // None before Titania's repair means the player hasn't checked in yet (after it, None is Bridge access).
        bool notCheckedIn = currentClass.Value == (int)PassengerClass.None
            && !_apConnection.IsLocationCheckedOrQueued(TitaniaRepairedLocation);

        // Not checked in: leave the DeskBot unspoofed so the check-in conversation runs first - otherwise the player
        // can never leave the Embarkation Lobby. Once it pays out the real class is Third, so a Magazine hand-off
        // works natively (even in the same visit) and later visits spoof as usual.
        if (rnv == DeskBotRnv && notCheckedIn)
            return;

        bool magazineHandOffPending = rnv == DeskBotRnv && secondClassPending
            && GameState.FindAllCarryItems(_mem, project).Any(item =>
                string.Equals(item.Name, "Magazine", StringComparison.OrdinalIgnoreCase)
                && item.ParentAddress == _currentInventoryRoom.Value);

        if (magazineHandOffPending)
        {
            // DeskbotScript's 2ndClassUpgrade dialogue (140) pays out only at exactly Third.
            if (currentClass.Value != (int)PassengerClass.Third)
                spoofToClass = (int)PassengerClass.Third;
        }
        else if (rnv != DeskBotRnv && currentClass.Value < (int)PassengerClass.Third && secondClassPending)
        {
            spoofToClass = configuredSpoofClass;
        }
        else if (rnv == DeskBotRnv && !secondClassPending && !_apConnection.IsLocationCheckedOrQueued(firstLocationName))
        {
            // DeskbotScript's 1st Class upgrade pays out only at exactly Second.
            if (currentClass.Value != (int)PassengerClass.Second)
                spoofToClass = (int)PassengerClass.Second;
        }
        else if (rnv == DeskBotRnv
            && currentClass.Value != (int)PassengerClass.None
            && !GameActions.HasAnyAssignedRoom(_mem, _currentInventoryRoom.Value))
        {
            // No room assigned yet: report None so the initial DeskBot conversation and room assignment run.
            spoofToClass = (int)PassengerClass.None;
        }

        if (spoofToClass is null)
            return;

        if (GameActions.SetPassengerClass(_mem, gameManager, spoofToClass.Value))
        {
            _classUpgradeSpoofOriginalClass = currentClass.Value;
            ShowActionResult(true, $"Class upgrade spoof: reporting {PassengerClassNames.GetName(spoofToClass.Value)} (really {PassengerClassNames.GetName(currentClass.Value)})");
        }
    }

    /// <summary>Restores the true PassengerClass value once the player moves to an RNV that isn't one of
    /// <see cref="ClassSpoofTriggerRnvs"/> (nor <see cref="BellBotIntroRnv"/>), undoing
    /// <see cref="TryApplyClassUpgradeSpoof"/> and redrawing the PET.</summary>
    private void TryRevertClassUpgradeSpoof(RoomNodeView newRnv, long gameManager)
    {
        if (_classUpgradeSpoofOriginalClass is not { } originalClass)
            return;
        if (ClassSpoofTriggerRnvs.ContainsKey(newRnv) || newRnv == BellBotIntroRnv)
            return;

        if (_currentInventoryRoom is { } petControlAddr)
            GameActions.SetPassengerClassFull(_mem, gameManager, petControlAddr, originalClass);
        else
            GameActions.SetPassengerClass(_mem, gameManager, originalClass);

        _classUpgradeSpoofOriginalClass = null;
    }

    /// <summary>The two RNVs inside the player's own randomly-assigned SGT Class Stateroom where the bed/TV
    /// puzzle plays out.</summary>
    private static readonly RoomNodeView[] SgtGlyphSpoofTriggerRnvs =
    {
        new(27, 1, 2), // SGT Stateroom
        new(27, 4, 1), // SGT TV
    };

    private long? _sgtGlyphAddr;
    private int? _sgtGlyphOriginalMode;

    /// <summary>Temporarily re-flags the player's RGM_PREV_ASSIGNED_ROOM SGT Class Stateroom glyph as RGM_ASSIGNED_ROOM
    /// while the player is in one of <see cref="SgtGlyphSpoofTriggerRnvs"/>, so the SGT bed/TV puzzle can be completed.
    /// The glyph is found by its encoded class + mode via GameActions.FindPrevAssignedGlyphForClass.
    /// <see cref="TryRevertSgtGlyphSpoof"/> restores the original mode.</summary>
    private void TryApplySgtGlyphSpoof(RoomNodeView rnv)
    {
        if (_sgtGlyphAddr is not null)
            return; // already spoofed
        if (Array.IndexOf(SgtGlyphSpoofTriggerRnvs, rnv) < 0)
            return;
        if (_currentInventoryRoom is not { } petControlAddr)
            return;

        long? glyphAddr = GameActions.FindPrevAssignedGlyphForClass(_mem, petControlAddr, (int)PassengerClass.Third);
        if (glyphAddr is null)
            return; // no prev-assigned Third-class glyph - either never upgraded, or nothing to spoof

        int? currentMode = GameActions.ReadGlyphMode(_mem, glyphAddr.Value);
        if (currentMode is null || currentMode == GameOffsets.RgmAssignedRoom)
            return; // unreadable, or already the real assignment - nothing to spoof

        if (GameActions.WriteGlyphMode(_mem, glyphAddr.Value, GameOffsets.RgmAssignedRoom))
        {
            _sgtGlyphAddr = glyphAddr.Value;
            _sgtGlyphOriginalMode = currentMode.Value;
            ShowActionResult(true, "SGT Stateroom glyph spoof: reporting assigned (really prev-assigned)");
        }
    }

    /// <summary>Restores the SGT Class Stateroom glyph's true _mode once the player leaves <see cref="SgtGlyphSpoofTriggerRnvs"/>,
    /// undoing <see cref="TryApplySgtGlyphSpoof"/>.</summary>
    private void TryRevertSgtGlyphSpoof(RoomNodeView newRnv)
    {
        if (_sgtGlyphAddr is not { } glyphAddr || _sgtGlyphOriginalMode is not { } originalMode)
            return;
        if (Array.IndexOf(SgtGlyphSpoofTriggerRnvs, newRnv) >= 0)
            return;

        GameActions.WriteGlyphMode(_mem, glyphAddr, originalMode);

        _sgtGlyphAddr = null;
        _sgtGlyphOriginalMode = null;
    }
}
