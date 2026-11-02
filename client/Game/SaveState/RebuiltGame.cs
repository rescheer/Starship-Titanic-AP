namespace StarshipTitanicAp;

/// <summary>What the AP server knows about a slot's progress, which a rebuilt game is generated from.</summary>
/// <param name="CheckedLocations">Locations checked on the server or queued to be.</param>
/// <param name="ReceivedItems">AP item names received, with a repeat for each copy.</param>
/// <param name="FuseBox">The Fuse Box as last stored, or null if it's never changed.</param>
/// <param name="TitaniaHead">What's in Titania as last stored, or null if it's never changed.</param>
internal sealed record SlotProgress(
    IReadOnlySet<string> CheckedLocations,
    IReadOnlyCollection<string> ReceivedItems,
    IReadOnlyDictionary<string, object>? SlotData,
    FuseBoxState? FuseBox = null,
    TitaniaHeadState? TitaniaHead = null);

/// <summary>Works out a game world from <see cref="SlotProgress"/>, so a new game picks up where the slot left off. The
/// result only has to be consistent and leave the remaining checks doable, not match the game that was lost: every item
/// the slot has received comes back to the player, so any puzzle left undone can be done again.</summary>
internal static class RebuiltGame
{
    private const string TitaniaName = "TitaniaControl";
    private const string TitaniaClass = "CTitania";
    private const string BridgeVisitedLocation = "Bridge - Visited";

    /// <summary>CTitania::save writes its version, then a flag for each of these parts, then _showSpeech.</summary>
    private static readonly string[] TitaniaFlagParts =
    {
        "SpeechCentre", "OlfactoryCentre", "AuditoryCentre", "CentralCore", "VisionCentre",
        "Eye1", "Eye2", "Ear1", "Ear 2", "Nose", "Mouth",
    };
    private const int TitaniaFirstPartField = 1;

    /// <summary>The line of CCarry::_canTake on the parts a new game starts with it off, until their puzzle is solved.
    /// ItemSync only corrects it on an item in the inventory, and a Vision Center left with it off freezes when dragged out
    /// of its slot. Ear 2 can't be dragged out, but is on in a real save with it installed.</summary>
    private static readonly Dictionary<string, int> PartCanTakeFields = new(StringComparer.Ordinal)
    {
        ["VisionCentre"] = 10,
        ["Ear 2"] = 12,
    };

    private static readonly int TitaniaShowSpeechField = TitaniaFirstPartField + TitaniaFlagParts.Length;

    private const string ShipSettingClass = "CShipSetting";
    private const string ShipSettingButtonClass = "CShipSettingButton";
    private const string BackgroundClass = "CBackground";
    private const string SuccUBusClass = "CSuccUBus";
    private const string BilgeSuccUBusClass = "CBilgeSuccUBus";
    private const string ParrotSuccUBusClass = "CParrotSuccUBus";
    private const string HeadSlotClass = "CHeadSlot";
    private const string BrainSlotClass = "CBrainSlot";

    private const int CursorHand = 8;

    // Lines after the class name, in the order each class's save() writes them
    private const int ShipSettingItemNameField = 3;
    private const int ShipSettingCursorField = 16;
    private const int BackgroundFrameField = 10;
    private const int KnobPressedField = 2;
    private const int KnobEnabledField = 3;
    private const int HeadSmasherEnabledField = 1;
    private const int DispenserDisabledField = 1;
    private const int DispenserModeField = 2;
    private const int FanEnabledField = 2;
    private const int SeasonalEnabledField = 2;
    private const int SuccUBusFuseboxOnField = 52;
    private const int BilgeSuccUBusFieldsBefore = 5;
    private const int ParrotSuccUBusFieldsBefore = 4;
    private const int HeadSlotOccupiedField = 1;
    private const int HeadSlotSenseField = 2;
    private const int HeadSlotTargetField = 3;
    private const int HeadSlotWokenField = 9;
    private const int HeadSlotCursorField = 16;
    private const int BrainSlotOccupiedField = 1;
    private const int BrainSlotTargetField = 2;
    private const int BrainSlotNumAddedField = 3;
    private const int BrainSlotWokenField = 4;
    private const int BrainSlotFrameField = 9;
    private const int BrainSlotCursorField = 10;

    /// <summary>Titania.Node 1.S, on the way to the Bridge. Titania's repair leaves the player with no class, which is
    /// let through to the Bridge from here (see CRestrictedMove) but kept in the Embarkation Lobby.</summary>
    private static readonly RoomNodeView RepairedStartRnv = new(37, 1, 3);

    /// <summary>Checks that can be made before talking to the DeskBot. Any other shows that the player has checked in,
    /// as the lobby can't be left until then.</summary>
    private static readonly HashSet<string> BeforeCheckInLocations = new(StringComparer.Ordinal)
    {
        "Embarkation Lobby - Visited",
        "Embarkation Lobby - Succ-U-Bus",
    };

    private static bool IsTitaniaRepaired(SlotProgress progress) =>
        progress.CheckedLocations.Contains(ClassSync.TitaniaRepairedLocation)
        || progress.CheckedLocations.Contains(BridgeVisitedLocation);

    private static bool IsCheckedIn(SlotProgress progress) =>
        progress.CheckedLocations.Any(location => !BeforeCheckInLocations.Contains(location));

    /// <summary>Whether the slot has progress to restore, as opposed to a new game that should just start.</summary>
    public static bool HasProgress(SlotProgress progress) =>
        progress.CheckedLocations.Count > 0 || progress.FuseBox is not null || progress.TitaniaHead is not null;

    /// <summary>Applies <paramref name="progress"/> to a save that's at the start of the game. Throws
    /// InvalidDataException if the save doesn't have the objects it should.</summary>
    public static string Apply(string save, SlotProgress progress)
    {
        SaveTree tree = SaveTree.Parse(save);
        SetUpItems(tree, progress);
        SetUpFuseBox(tree, progress);
        SetUpTitaniaHead(tree, progress);
        SetUpPassengerClass(tree, progress);
        if (IsTitaniaRepaired(progress))
            SetUpRepairedTitania(tree);
        return tree.Render();
    }

    /// <summary>How many staterooms the player should hold under vanilla stateroom assignment, where the DeskBot hands
    /// them out: 0 to 3, in the scale of GameActions.GetAchievedStateroomClass.</summary>
    public static int VanillaStateroomCount(SlotProgress progress)
    {
        if (LocationChecks.TryGetClassUpgradeLocationName((int)PassengerClass.First, out string first)
            && progress.CheckedLocations.Contains(first))
            return 3;
        if (LocationChecks.TryGetClassUpgradeLocationName((int)PassengerClass.Second, out string second)
            && progress.CheckedLocations.Contains(second))
            return 2;
        return IsCheckedIn(progress) ? 1 : 0;
    }

    /// <summary>The passenger class to start with.</summary>
    internal static int ComputePassengerClass(SlotProgress progress)
    {
        if (IsTitaniaRepaired(progress))
            return SpecialPassengerClassValues.BridgeAccessClassValue;

        int checkedInClass = IsCheckedIn(progress) ? (int)PassengerClass.Third : (int)PassengerClass.None;
        if (progress.SlotData is not { } slotData)
            return checkedInClass;

        if (!ClassUpgradeTracker.IsVanilla(slotData))
            return ClassUpgradeTracker.ComputeClass(progress.ReceivedItems, slotData) ?? checkedInClass;

        return VanillaStateroomCount(progress) switch
        {
            3 => (int)PassengerClass.First,
            2 => (int)PassengerClass.Second,
            _ => checkedInClass,
        };
    }

    private static void SetUpPassengerClass(SaveTree tree, SlotProgress progress)
    {
        int passengerClass = ComputePassengerClass(progress);
        int priorClass = passengerClass switch
        {
            (int)PassengerClass.First => (int)PassengerClass.Second,
            (int)PassengerClass.Second => (int)PassengerClass.Third,
            (int)PassengerClass.Third => (int)PassengerClass.None,
            _ when IsTitaniaRepaired(progress) => (int)PassengerClass.First,
            _ => (int)PassengerClass.None,
        };
        tree.SetStateNumber(SaveTree.StatePassengerClass, passengerClass);
        tree.SetStateNumber(SaveTree.StatePriorClass, priorClass);
    }

    /// <summary>Takes every found item out of the world. ItemSync.ReconcileTrackedItems then treats it as it does one
    /// found before being received: the mail or inventory if the slot has it, and otherwise the hidden room until it
    /// arrives. An item that's been received but not found needs nothing here, as a fresh tree already gets it delivered
    /// and returned to its home spot for its check.</summary>
    private static void SetUpItems(SaveTree tree, SlotProgress progress)
    {
        SaveObject hiddenRoom = tree.FindByName(CarryItemLocation.HiddenRoomName);

        foreach (ItemDefinition item in Items.All)
        {
            if (item.PickupLocationName is not { } location || !progress.CheckedLocations.Contains(location))
                continue;

            // Its check is watched for on CarryParrot, and ItemSync delivers the Feathers item without any of this
            if (item.Name == FeathersItem.Definition.Name)
            {
                tree.FindByName(ItemTracking.CarryParrotName)
                    .SetPersistedState(new ItemPersistedState(ItemStage.None, true, ItemPulledFrom.None));
                continue;
            }

            // Delivery leaves one-directional items alone, so ItemSync moves these itself once it sees the check
            if (item.IsOneDirectional)
                continue;

            SaveObject found = tree.FindByName(item.Name);
            SaveTree.Move(found, hiddenRoom);
            found.SetPersistedState(new ItemPersistedState(ItemStage.Hidden, true, ItemPulledFrom.None));
        }
    }

    /// <summary>Puts the Fuse Box as it was stored: sockets, knobs, and the flags the knobs set on their targets. A socket's
    /// frame normally only updates on entering Titania's room, which a game starting in there has already done.</summary>
    private static void SetUpFuseBox(SaveTree tree, SlotProgress progress)
    {
        if (progress.FuseBox is not { } state)
            return;

        for (int i = 0; i < FuseBoxState.AllSockets.Length; i++)
        {
            FuseBoxSocket socket = FuseBoxState.AllSockets[i];
            string? fuse = state.Sockets[i];

            SaveObject shipSetting = tree.Find(ShipSettingClass, socket.Name);
            shipSetting.SetStringField(ShipSettingItemNameField, fuse ?? GameOffsets.ShipSettingEmpty);
            shipSetting.SetNumberField(ShipSettingCursorField, fuse is null ? GameOffsets.CursorArrow : CursorHand);

            tree.Find(BackgroundClass, socket.FrameTarget).SetNumberField(BackgroundFrameField, FuseBoxState.FrameFor(fuse));
        }

        for (int i = 0; i < FuseBoxState.Knobs.Length; i++)
        {
            SaveObject knob = tree.Find(ShipSettingButtonClass, FuseBoxState.Knobs[i]);
            knob.SetNumberField(KnobPressedField, state.Pressed[i] ? 1 : 0);
            knob.SetNumberField(KnobEnabledField, state.Enabled[i] ? 1 : 0);
        }

        SetKnobTargets(tree, state);

        foreach (string fuse in FuseBoxState.Fuses)
            SetUpFuse(tree, progress, state, fuse);
    }

    /// <summary>What CShipSettingButton::MouseButtonDownMsg's message to each target would have left behind. Knobs are in
    /// the order of <see cref="FuseBoxState.Knobs"/>.</summary>
    private static void SetKnobTargets(SaveTree tree, FuseBoxState state)
    {
        tree.FindByName(FuseBoxState.HeadSmasherName).SetNumberField(HeadSmasherEnabledField, state.Enabled[0] ? 1 : 0);

        SaveObject dispenser = tree.FindByName(FuseBoxState.ChickenDispenserName);
        dispenser.SetNumberField(DispenserDisabledField, state.Enabled[1] ? 0 : 1);
        dispenser.SetNumberField(DispenserModeField, state.DispenseMode);

        tree.FindByName(FuseBoxState.FanControllerName).SetNumberField(FanEnabledField, state.Enabled[2] ? 1 : 0);
        tree.FindByName(FuseBoxState.SeasonalAdjustName).SetNumberField(SeasonalEnabledField, state.Enabled[3] ? 1 : 0);

        // A static, written by every Succ-U-Bus and taken from whichever loads last
        long fuseboxOn = state.Enabled[FuseBoxState.SuccUBusKnobIndex] ? 1 : 0;
        foreach (SaveObject succUBus in tree.FindAllOfClass(SuccUBusClass))
            succUBus.SetNumberField(SuccUBusFuseboxOnField, fuseboxOn);
        foreach (SaveObject succUBus in tree.FindAllOfClass(BilgeSuccUBusClass))
            succUBus.SetNumberField(BilgeSuccUBusFieldsBefore + SuccUBusFuseboxOnField, fuseboxOn);
        foreach (SaveObject succUBus in tree.FindAllOfClass(ParrotSuccUBusClass))
            succUBus.SetNumberField(ParrotSuccUBusFieldsBefore + SuccUBusFuseboxOnField, fuseboxOn);
    }

    /// <summary>Puts a fuse where the state says. One in a socket lives in the hidden room, as
    /// CBridgePiece::UseWithOtherMsg leaves it, except that the two a new game starts with stay put in their own sockets.
    /// One of those two that's no longer in a socket joins the hidden room, for ItemSync to deliver. A fuse elsewhere is
    /// already handled by the rules for items.</summary>
    private static void SetUpFuse(SaveTree tree, SlotProgress progress, FuseBoxState state, string fuseName)
    {
        int socketIndex = Array.IndexOf(state.Sockets, fuseName);
        int startingSocketIndex = Array.FindIndex(FuseBoxState.AllSockets, socket => socket.StartingFuse == fuseName);
        if (socketIndex >= 0 && socketIndex == startingSocketIndex)
            return;
        if (socketIndex < 0 && startingSocketIndex < 0)
            return;

        SaveObject fuse = tree.FindByName(fuseName);
        SaveTree.Move(fuse, tree.FindByName(CarryItemLocation.HiddenRoomName));
        if (socketIndex < 0)
            return;

        fuse.SetVisible(false);

        // Without its check, it would otherwise be taken for an item that's never been found and be sent home
        if (Items.TryGet(fuseName, out ItemDefinition item) && item.PickupLocationName is { } location
            && !progress.CheckedLocations.Contains(location))
        {
            fuse.SetPersistedState(new ItemPersistedState(ItemStage.Inventory, false, ItemPulledFrom.None));
        }
    }

    /// <summary>Puts Titania's parts where they were stored: the slots, what CTitania holds of them, and the parts
    /// themselves. A repaired Titania has them all, and is awake with her first speech over, which stops them being taken
    /// out. Otherwise the speech is left to play, as it belongs to a different view from the slots.</summary>
    private static void SetUpTitaniaHead(SaveTree tree, SlotProgress progress)
    {
        bool woken = IsTitaniaRepaired(progress);
        if ((woken ? TitaniaHeadState.Repaired : progress.TitaniaHead) is not { } state)
            return;

        foreach (TitaniaHeadSlot faceSlot in TitaniaHeadState.FaceSlots)
        {
            SaveObject slot = tree.Find(HeadSlotClass, faceSlot.Name);
            slot.SetStringField(HeadSlotSenseField, state.SenseFor(faceSlot));
            if (!state.IsPartWorking(faceSlot.Part))
                continue;

            slot.SetNumberField(HeadSlotOccupiedField, 1);
            slot.SetStringField(HeadSlotTargetField, faceSlot.Part);
            slot.SetNumberField(HeadSlotCursorField, CursorHand);
        }

        if (state.Core)
        {
            SaveObject coreSlot = tree.Find(BrainSlotClass, TitaniaHeadState.CoreSlotName);
            coreSlot.SetNumberField(BrainSlotOccupiedField, 1);
            coreSlot.SetNumberField(BrainSlotCursorField, CursorHand);
        }

        for (int i = 0; i < TitaniaHeadState.BrainSlots.Length; i++)
        {
            if (state.Brain[i] is not { } card)
                continue;

            TitaniaBrainSlot cardHome = Array.Find(TitaniaHeadState.BrainSlots, slot => slot.Card == card);
            SaveObject slot = tree.Find(BrainSlotClass, TitaniaHeadState.BrainSlots[i].Name);
            slot.SetNumberField(BrainSlotOccupiedField, 1);
            slot.SetStringField(BrainSlotTargetField, card);
            slot.SetNumberField(BrainSlotFrameField, cardHome.CardFrame);
            slot.SetNumberField(BrainSlotCursorField, CursorHand);

            // Without it, a card dragged back out of its slot is never moved out of the hidden room
            tree.FindByName(card).SetNumberField(cardHome.CardPieceAddedField, 1);
        }

        // Statics, written by every slot and taken from whichever loads last
        long numAdded = (state.Core ? 1 : 0) + state.Brain.Count(card => card is not null);
        foreach (SaveObject slot in tree.FindAllOfClass(BrainSlotClass))
        {
            slot.SetNumberField(BrainSlotNumAddedField, numAdded);
            slot.SetNumberField(BrainSlotWokenField, woken ? 1 : 0);
        }
        foreach (SaveObject slot in tree.FindAllOfClass(HeadSlotClass))
            slot.SetNumberField(HeadSlotWokenField, woken ? 1 : 0);

        SaveObject titania = tree.Find(TitaniaClass, TitaniaName);
        for (int i = 0; i < TitaniaFlagParts.Length; i++)
            titania.SetNumberField(TitaniaFirstPartField + i, state.IsPartWorking(TitaniaFlagParts[i]) ? 1 : 0);
        if (woken)
            titania.SetNumberField(TitaniaShowSpeechField, 0);

        foreach (string part in state.InstalledParts())
            SetUpInstalledPart(tree, progress, part);
    }

    /// <summary>Leaves a part as CHeadPiece and CBrain's UseWithOtherMsg do, in the hidden room and not visible, and as
    /// used so it isn't delivered. Without its check it can still be restored to its home spot for that, as in a live game.
    /// </summary>
    private static void SetUpInstalledPart(SaveTree tree, SlotProgress progress, string partName)
    {
        SaveObject part = tree.FindByName(partName);
        SaveTree.Move(part, tree.FindByName(CarryItemLocation.HiddenRoomName));
        part.SetVisible(false);
        if (PartCanTakeFields.TryGetValue(partName, out int canTakeField))
            part.SetNumberField(canTakeField, 1);

        bool checkFired = Items.TryGet(partName, out ItemDefinition item) && item.PickupLocationName is { } location
            && progress.CheckedLocations.Contains(location);
        part.SetPersistedState(new ItemPersistedState(ItemStage.Inventory, checkFired, ItemPulledFrom.None));
    }

    /// <summary>The player in Titania's room, where the repair leaves them. The passenger class set in
    /// <see cref="SetUpPassengerClass"/> is what opens the way to the Bridge.</summary>
    private static void SetUpRepairedTitania(SaveTree tree)
    {
        tree.SetStateNumber(SaveTree.StateRoom, RepairedStartRnv.Room);
        tree.SetStateNumber(SaveTree.StateNode, RepairedStartRnv.Node);
        tree.SetStateNumber(SaveTree.StateView, RepairedStartRnv.View);
    }
}
