namespace StarshipTitanicAp;

/// <summary>One carryable item's true default (fresh-save) parent identity.</summary>
public readonly record struct DefaultParent(string? ParentName, string ParentClass);

/// <summary>Known-good _bounds/_cursorId to reapply after a generic tree re-attach. Bounds/CursorId/EnterFrame
/// are optional (null = leave whatever the engine already has); KeepVisible keeps _visible true after
/// restoration.</summary>
public readonly record struct RestoreFieldOverride(
    short? Left, short? Top, short? Right, short? Bottom, int? CursorId, int? EnterFrame, bool? KeepVisible);

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

    /// <summary>Sibling object to force-hide when this item is restored to its home parent. Shown again once the item
    /// leaves, if the game had already shown it (only Ear 2's Replacement Phonograph Ear is supported).</summary>
    public (string Name, string ClassName)? HideSiblingOnRestore { get; init; }

    /// <summary>True for an item that enters play via some other in-game mechanism rather than the player
    /// finding it in a home view.</summary>
    public bool IsOneDirectional { get; init; }

    /// <summary>True for a fuse that can be installed into a Fuse Box socket (see
    /// GameState.FindShipSettingsInstalledWith). An installed fuse's real item is parked in the hidden room.</summary>
    public bool IsFuseBoxItem { get; init; }

    /// <summary>True if this item is excluded from the home-RNV restoration flow (TryRestoreItemsAtHomeRnv) even
    /// if it has a home RNV registered.</summary>
    public bool IsRestorationExcluded { get; init; }

    /// <summary>True if this item's _canTake flag must be forced true when AP grants it, because the engine only
    /// sets it once the item's normal puzzle prerequisite is met. Reapplied on restoration to its home parent, since
    /// using the item in between can clear it again (e.g. CFruit::UseWithCharMsg on the Barbot).</summary>
    public bool RequiresCanTakeOverride { get; init; }

    /// <summary>True if this item's _canTake flag must track the restore/unrestore cycle: true while sitting in
    /// inventory (so the player can hold/use it), forced false while temporarily restored to its home parent for
    /// a re-pickup, and back to true once un-restored or actually picked up again.</summary>
    public bool RequiresCanTakeRestoreToggle { get; init; }

    /// <summary>True if RequiresCanTakeOverride's forced true is held back on restoration to its home parent until the
    /// Music Room puzzle is solved, so the pickup there still needs the puzzle. The solve is recorded on the item
    /// itself (ItemSync.RecordMusicSolved), since the engine's own unlock (CPhonographEar::CorrectMusicPlayedMsg)
    /// only reaches it while it sits in the Music Room.</summary>
    public bool RestoreCanTakeRequiresMusicSolved { get; init; }

    /// <summary>True if the generic restoration flow must NOT reorder this item to be its home parent's first
    /// child, preserving its authored draw order relative to its siblings.</summary>
    public bool SkipFirstChildReorderOnRestore { get; init; }

    /// <summary>True if this item's home parent is a CDropTarget that draws the held item as its own frame. The
    /// engine only picks that frame in EnterViewMsg, which has already run by the time the item is restored, so the
    /// restoration flow sets it directly (see GameActions.ShowDropTargetHolding).</summary>
    public bool HomeParentIsDropTargetDisplay { get; init; }

    /// <summary>True if restoration to its home parent reloads the item's own _enterFrame (its in-scene look). The
    /// engine only loads it in CCarry::EnterViewMsg, once, so an item that went through the inventory first keeps
    /// showing its glyph frame (_visibleFrame) in-world otherwise.</summary>
    public bool RestoreLoadsEnterFrame { get; init; }

    /// <summary>True if restoration to its home parent also resets _visibleFrame to _enterFrame. For items whose
    /// PETGainedObjectMsg points _visibleFrame at the glyph and which are later revealed in-world by a CVisibleMsg
    /// (e.g. the Mouth, shown by CGondolierMixer once its puzzle is solved), which loads _visibleFrame.</summary>
    public bool RestoreResetsVisibleFrame { get; init; }

    /// <summary>The item's true drawn size, applied to its _bounds on direct delivery into inventory. The engine only
    /// resizes _bounds to the surface while drawing or dragging the item, so an item that was never drawn in-world
    /// (e.g. hidden inside a CDropTarget) keeps its authored size, and the PET's dragGlyph centres it on the mouse
    /// with that size, failing checkPoint's pixel test.</summary>
    public (short Width, short Height)? InventoryBoundsSize { get; init; }
}
