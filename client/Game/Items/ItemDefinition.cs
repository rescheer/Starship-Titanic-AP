namespace StarshipTitanicAp;

/// <summary>One carryable item's true default (fresh-save) parent identity.</summary>
public readonly record struct DefaultParent(string? ParentName, string ParentClass);

/// <summary>Known-good _bounds/_cursorId to reapply after a generic tree re-attach. Bounds/CursorId/EnterFrame
/// are optional (null = leave whatever the engine already has) since KeepVisible alone is the confirmed fix
/// for the generic restoration flow's "force _visible false, room art already shows it" assumption being
/// wrong for a given item - the other fields are only needed when leftover values from a prior natural
/// pickup also need correcting (see BrokenLiftbotHead).</summary>
public readonly record struct RestoreFieldOverride(
    short? Left, short? Top, short? Right, short? Bottom, int? CursorId, int? EnterFrame, bool KeepVisible);

/// <summary>Everything this app knows about one carryable item.</summary>
public sealed record ItemDefinition
{
    /// <summary>The engine tree-node name (ItemNames.All).</summary>
    public required string Name { get; init; }

    /// <summary>AP item name, per items.py's item_table. Null for items never granted via a location check
    /// (e.g. server-granted-only fuses, or items with no AP mapping at all).</summary>
    public string? ApItemName { get; init; }

    /// <summary>AP location name for this item's natural pickup check.</summary>
    public string? PickupLocationName { get; init; }

    /// <summary>AP item name for an item granted directly by the multiworld server rather than via a natural-pickup
    /// check (see GameState.FindShipSettingsInstalledWith).</summary>
    public string? ServerGrantedApItemName { get; init; }

    /// <summary>Full-state-machine items' authored "home" (Room, Node, View) triple(s).</summary>
    public RoomNodeView[]? HomeRnvs { get; init; }

    /// <summary>Override RNV(s) to search when resolving this item's true home parent, for the rare item whose
    /// true home parent lives at a different RNV than the one that triggers restoration. Falls back to
    /// <see cref="HomeRnvs"/> when null. E.g. Magazine is restored on arrival at the SGT TV [27,4,1], but its
    /// actual fresh-save parent is the hidden CViewItem at [27,1,2].</summary>
    public RoomNodeView[]? HomeParentSearchRnvOverride { get; init; }

    /// <summary>This item's true default (fresh-save) parent, identified by name + class.</summary>
    public DefaultParent? DefaultParent { get; init; }

    /// <summary>Field overrides to reapply after a generic tree re-attach restoration.</summary>
    public RestoreFieldOverride? RestoreFieldOverride { get; init; }

    /// <summary>Sibling object to force-hide when this item is restored to its home parent.</summary>
    public (string Name, string ClassName)? HideSiblingOnRestore { get; init; }

    /// <summary>True for an item that enters play via some other in-game mechanism rather than the player
    /// finding it in a home view.</summary>
    public bool IsOneDirectional { get; init; }

    /// <summary>True for a fuse that can be installed into a Fuse Box socket (see
    /// GameState.FindShipSettingsInstalledWith). Installing one relocates the real item via the same
    /// CGameObject::petMoveToHiddenRoom() the AP hidden-room stash uses, so any code that reacts to a tracked
    /// item landing in the hidden room needs to check whether it's actually an installed fuse - not a
    /// mail-delivery/pending-grant case.</summary>
    public bool IsFuseBoxItem { get; init; }

    /// <summary>True if this item is excluded from the home-RNV restoration flow (TryRestoreItemsAtHomeRnv) even
    /// if it has a home RNV registered - e.g. because moving it back for a re-pickup isn't safe/meaningful for
    /// how it naturally enters play.</summary>
    public bool IsRestorationExcluded { get; init; }

    /// <summary>True if this item's _canTake flag must be forced true when AP grants it, because the engine only
    /// sets it once the item's normal puzzle prerequisite is met.</summary>
    public bool RequiresCanTakeOverride { get; init; }

    /// <summary>True if this item's _canTake flag must track the restore/unrestore cycle: true while sitting in
    /// inventory (so the player can hold/use it), forced false while temporarily restored to its home parent for
    /// a re-pickup, and back to true once un-restored or actually picked up again.</summary>
    public bool RequiresCanTakeRestoreToggle { get; init; }

    /// <summary>True if the generic restoration flow must NOT reorder this item to be its home parent's first
    /// child. That reorder exists to make simple sprite-swap items paint over baked room art, but for an item
    /// whose own draw order relative to a specific sibling is load-bearing (e.g. Lemon needs to stay after
    /// SeasonBackground so the seasonal art doesn't paint over it), forcing it to the front instead makes it
    /// invisible while every one of its own fields (bounds/visible/surface) still checks out correct.</summary>
    public bool SkipFirstChildReorderOnRestore { get; init; }
}
