namespace StarshipTitanicAp;

/// <summary>What the AP server knows about a slot's progress, which a rebuilt game is generated from.</summary>
/// <param name="CheckedLocations">Locations checked on the server or queued to be.</param>
/// <param name="ReceivedItems">AP item names received, with a repeat for each copy.</param>
internal sealed record SlotProgress(
    IReadOnlySet<string> CheckedLocations,
    IReadOnlyCollection<string> ReceivedItems,
    IReadOnlyDictionary<string, object>? SlotData);

/// <summary>Works out a game world from <see cref="SlotProgress"/>, so a new game picks up where the slot left off. The
/// result only has to be consistent and leave the remaining checks doable, not match the game that was lost: every item
/// the slot has received comes back to the player, so any puzzle left undone can be done again.</summary>
internal static class RebuiltGame
{
    private const string TitaniaName = "TitaniaControl";
    private const string TitaniaClass = "CTitania";
    private const string BridgeVisitedLocation = "Bridge - Visited";

    /// <summary>CTitania::save writes its version, then eleven flags for its parts, then _showSpeech.</summary>
    private const int TitaniaFirstPartField = 1;
    private const int TitaniaPartCount = 11;
    private const int TitaniaShowSpeechField = TitaniaFirstPartField + TitaniaPartCount;

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
    public static bool HasProgress(SlotProgress progress) => progress.CheckedLocations.Count > 0;

    /// <summary>Applies <paramref name="progress"/> to a save that's at the start of the game. Throws
    /// InvalidDataException if the save doesn't have the objects it should.</summary>
    public static string Apply(string save, SlotProgress progress)
    {
        SaveTree tree = SaveTree.Parse(save);
        SetUpItems(tree, progress);
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

        // In Titania's head for good. Marked as used items are, so they aren't delivered back.
        if (IsTitaniaRepaired(progress))
        {
            foreach (TitaniaPart titaniaPart in TitaniaParts.All)
            {
                SaveObject part = tree.FindByName(titaniaPart.ItemName);
                SaveTree.Move(part, hiddenRoom);
                part.SetPersistedState(new ItemPersistedState(ItemStage.Inventory, true, ItemPulledFrom.None));
            }
        }
    }

    /// <summary>Titania with every part in and the first speech over, and the player in Titania's room, where the repair
    /// leaves them. The passenger class set in <see cref="SetUpPassengerClass"/> is what opens the way to the Bridge.
    /// </summary>
    private static void SetUpRepairedTitania(SaveTree tree)
    {
        SaveObject titania = tree.Find(TitaniaClass, TitaniaName);
        for (int i = 0; i < TitaniaPartCount; i++)
            titania.SetNumberField(TitaniaFirstPartField + i, 1);
        titania.SetNumberField(TitaniaShowSpeechField, 0);

        tree.SetStateNumber(SaveTree.StateRoom, RepairedStartRnv.Room);
        tree.SetStateNumber(SaveTree.StateNode, RepairedStartRnv.Node);
        tree.SetStateNumber(SaveTree.StateView, RepairedStartRnv.View);
    }
}
