namespace StarshipTitanicAp;

/// <summary>Registry of every carryable item's definition.</summary>
public static class Items
{
    public static readonly IReadOnlyList<ItemDefinition> All = new[]
    {
        MaitreDLeftArmItem.Definition,
        MaitreDRightArmItem.Definition,
        OlfactoryCentreItem.Definition,
        AuditoryCentreItem.Definition,
        SpeechCentreItem.Definition,
        VisionCentreItem.Definition,
        CentralCoreItem.Definition,
        PerchItem.Definition,
        SeasonBridgeItem.Definition,
        FanBridgeItem.Definition,
        BeamBridgeItem.Definition,
        ChickenBridgeItem.Definition,
        CarryParrotItem.Definition,
        ChickenItem.Definition,
        CrushedTVItem.Definition,
        FeathersItem.Definition,
        LemonItem.Definition,
        BeerGlassItem.Definition,
        BigHammerItem.Definition,
        Ear1Item.Definition,
        Ear2Item.Definition,
        Eye1Item.Definition,
        Eye2Item.Definition,
        MouthItem.Definition,
        NoseItem.Definition,
        HoseItem.Definition,
        HoseEndItem.Definition,
        BrokenLiftbotHeadItem.Definition,
        LongStickItem.Definition,
        MagazineItem.Definition,
        NapkinItem.Definition,
        PhonographCylinder1Item.Definition,
        PhonographCylinder2Item.Definition,
        PhonographCylinder3Item.Definition,
        PhotographItem.Definition,
        MusicSystemKeyItem.Definition,
    };

    private static readonly Dictionary<string, ItemDefinition> ByName =
        All.ToDictionary(d => d.Name, d => d, StringComparer.OrdinalIgnoreCase);

    /// <summary>Looks up an item's definition by its engine tree-node name.</summary>
    public static bool TryGet(string name, out ItemDefinition item) => ByName.TryGetValue(name, out item!);

    private static readonly Dictionary<RoomNodeView, string[]> ByHomeRnv = All
        .Where(d => d.HomeRnvs is not null)
        .SelectMany(d => d.HomeRnvs!.Select(rnv => (rnv, d.Name)))
        .GroupBy(x => x.rnv)
        .ToDictionary(g => g.Key, g => g.Select(x => x.Name).ToArray());

    /// <summary>Looks up the item(s) whose home RNV is the given (Room, Node, View).</summary>
    public static bool TryGetForHomeRnv(RoomNodeView rnv, out string[] names) =>
        ByHomeRnv.TryGetValue(rnv, out names!);

    private static readonly Dictionary<string, string> ApItemNameToItemName = All
        .Where(d => d.ApItemName is not null)
        .ToDictionary(d => d.ApItemName!, d => d.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Looks up the engine item name for a granted AP item name.</summary>
    public static bool TryGetForApItemName(string apItemName, out string itemName) =>
        ApItemNameToItemName.TryGetValue(apItemName, out itemName!);
}
