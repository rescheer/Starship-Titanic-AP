namespace StarshipTitanicAp;

/// <summary>Home-location lookups, backed by the per-item definitions in Game/Items/.</summary>
public static class ItemHomeLocations
{
    public static bool TryGetHomeRnvs(string itemName, out RoomNodeView[] rnvs)
    {
        if (Items.TryGet(itemName, out ItemDefinition item) && item.HomeRnvs is not null)
        {
            rnvs = item.HomeRnvs;
            return true;
        }

        rnvs = Array.Empty<RoomNodeView>();
        return false;
    }

    /// <summary>RNV(s) to search when resolving an item's true home parent (see ItemDefinition.HomeParentSearchRnvOverride).</summary>
    public static RoomNodeView[] GetHomeParentSearchRnvs(string itemName)
    {
        if (!Items.TryGet(itemName, out ItemDefinition item))
        {
            return Array.Empty<RoomNodeView>();
        }

        return item.HomeParentSearchRnvOverride ?? item.HomeRnvs ?? Array.Empty<RoomNodeView>();
    }

    public static bool TryGetItemsForRnv(RoomNodeView rnv, out string[] itemNames) =>
        Items.TryGetForHomeRnv(rnv, out itemNames);

    public static bool TryGetDefaultParent(string itemName, out DefaultParent parent)
    {
        if (Items.TryGet(itemName, out ItemDefinition item) && item.DefaultParent is not null)
        {
            parent = item.DefaultParent.Value;
            return true;
        }

        parent = default;
        return false;
    }

    public static bool TryGetRestoreFieldOverride(string itemName, out (short? Left, short? Top, short? Right, short? Bottom, int? CursorId, int? EnterFrame, bool? KeepVisible) fields)
    {
        if (Items.TryGet(itemName, out ItemDefinition item) && item.RestoreFieldOverride is { } o)
        {
            fields = (o.Left, o.Top, o.Right, o.Bottom, o.CursorId, o.EnterFrame, o.KeepVisible);
            return true;
        }

        fields = default;
        return false;
    }

    public static bool TryGetHideSiblingOnRestore(string itemName, out (string Name, string ClassName) sibling)
    {
        if (Items.TryGet(itemName, out ItemDefinition item) && item.HideSiblingOnRestore is { } s)
        {
            sibling = s;
            return true;
        }

        sibling = default;
        return false;
    }
}
