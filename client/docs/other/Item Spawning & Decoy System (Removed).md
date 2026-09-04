# Item Spawning & Decoy Swap System (Removed)

This document records a design that was implemented, live-tested, and then
superseded before this app shipped. The code was removed from the client on
2026-08-31 in favor of the "hide in place, restore at home RNV" state
machine that `MainForm.GameLogic.cs`'s `ReconcileTrackedItems` implements
today. It's preserved here (rather than just deleted from git history)
because the reverse-engineering work behind it - confirmed spawn function
addresses, the `_resource` buffer real/copy detection, `petMoveToHiddenRoom`
- is still real, verified knowledge about the game's engine that could be
useful again later (e.g. if a future feature needs to spawn a duplicate
object for some other reason).

## What problem this was solving

Archipelago (AP) items are granted by a remote multiworld server, on its own
schedule - not necessarily in the order the player would naturally find the
corresponding physical objects in *Starship Titanic*. The core problem for
any AP client mod is reconciling "the player picked up object X in-game"
with "the multiworld server has (or hasn't) actually granted item X to this
player yet."

## The decoy/swap approach

The original design's answer: never let the player interact with a *real*
tracked item at all. Instead, at attach time, every trackable item still
sitting in its natural starting position was replaced in place with a
freshly spawned, identically-named decoy object. The real object was
relocated to a hidden room (a real, pre-existing hidden container the
engine itself uses for its own state, discovered as a side effect of the
`petMoveToHiddenRoom` game function). From that point on:

- The player only ever picks up decoys. Picking up a decoy was gameplay-only
  and didn't need to be specially detected, since the decoy carried no AP
  significance itself - it existed purely so the room wasn't left visibly
  empty.
- The real object, safely parked in the hidden room, was delivered to the
  player via the game's own mail system once (and only once) AP actually
  granted the matching item.

This decoupled "the player triggered the natural in-game pickup interaction"
from "the player is entitled to the item per AP," without needing to hook
the pickup code path itself at all - the swap happened once, up front, and
gameplay logic then only had to watch the real object's location, not
intercept any click/pickup event.

### Why it was abandoned

Extensive live testing (particularly around the Photograph item, delivered
through an unskippable intro cutscene) found that a spawned decoy could not
be made to behave identically to the real, authored object in every
situation. A spawned copy was missing load-time state the engine's own
level data supplies for a real object - position/bounds, a
visibility/registration flag, the drag-pose resource, `_canTake`,
`_fullViewName`, and possibly more (see `GameObjectResourceOffset`'s
surviving notes on the missing drag-graphic issue, which was accepted as
permanent for a decoy since it "would only ever exist briefly"). For most
static room items this didn't matter, but there was no safe window to swap
Photograph before the player could interact with it. Rather than carry two
separate, only-sometimes-safe code paths (`ItemPickupStrategy.Swap` vs.
`.Natural`), the whole system was eventually replaced with a strategy that
works uniformly for every item: never touch the real object's identity at
all. Instead, the *entire* physical object is what gets hidden/restored/
delivered - no decoy, no spawning, no swap - which is what
`ReconcileTrackedItems`/`TryRestoreItemsAtHomeRnv` do today.

## How the swap actually worked (mechanics)

### Spawning a decoy

Every `CCarry`-derived class in the ScummVM Titanic engine is registered in
`CSaveableObject`'s class list (`initClassList()` in `saveable_object.cpp`)
via `ADDFN(CHILD, PARENT)`, which the compiler turns into a zero-argument
factory function, e.g.:

```cpp
CSaveableObject *FunctionCMagazine() { return new CMagazine(); }
```

Calling one of these factory functions directly via a remote call
(`RemoteCaller.CallAndGetResult`) was far simpler than calling
`CSaveableObject::createInstance()` itself, which takes a `Common::String&`
that would otherwise need constructing correctly first.

A freshly spawned object's `_name` field is **not** set by any constructor
in the `CTreeItem -> CNamedItem -> CGameObject -> CCarry` chain -
`CNamedItem` uses the implicit default constructor, leaving `_name` as an
empty (SSO, no heap pointer) `CString`. It had to be named explicitly after
spawning, by placement-constructing a `CString(const char*)` directly over
the `_name` field (offset `+0x30`, `NamedItemNameOffset` - this offset is
still in active use elsewhere and was **not** removed) via the confirmed
`CString` constructor at module offset `0x3783980`
(`CStringCharPtrCtorFunc`, also still present - shared with conversation
logging).

Confirmed spawn function offsets (module-relative), as they existed in
`GameOffsets.cs` before removal:

| Const name | Offset | Notes |
|---|---|---|
| `SpawnArmFunc` | `0x23A6E50` | |
| `SpawnAuditoryCentreFunc` | `0x23A6E80` | |
| `SpawnBowlEarFunc` | `0x23A7010` | Confirmed correct for Ear1 |
| `SpawnBrainFunc` | `0x23A7060` | OlfactoryCentre |
| `SpawnBridgePieceFunc` | `0x23A7090` | shared class - see caveats below |
| `SpawnCarryFunc` | `0x23A70C0` | |
| `SpawnCarryParrotFunc` | `0x23A70F0` | never used - see caveats |
| `SpawnCentralCoreFunc` | `0x23A6ED0` | |
| `SpawnChickenFunc` | `0x23A7120` | |
| `SpawnCrushedTVFunc` | `0x23A7150` | |
| `SpawnEarFunc` | `0x23A7180` | |
| `SpawnEyeFunc` | `0x23A71B0` | shared class - see caveats below |
| `SpawnFeathersFunc` | `0x23A71E0` | |
| `SpawnFruitFunc` | `0x23A7210` | used for Lemon |
| `SpawnGlassFunc` | `0x23A7240` | BeerGlass |
| `SpawnHammerFunc` | `0x23A7270` | BigHammer |
| `SpawnHeadPieceFunc` | `0x23A72A0` | |
| `SpawnHoseFunc` | `0x23A72D0` | |
| `SpawnHoseEndFunc` | `0x23A7300` | never independently spawned |
| `SpawnKeyFunc` | `0x23A7330` | Music System Key |
| `SpawnLiftbotHeadFunc` | `0x23A7360` | BrokenLiftbotHead |
| `SpawnLongStickFunc` | `0x23A7390` | |
| `SpawnMagazineFunc` | `0x23A73C0` | corrected from a mis-identified Napkin function - see below |
| `SpawnMaitreDLeftArmFunc` | `0x23A73F0` | |
| `SpawnMaitreDRightArmFunc` | `0x23A7440` | |
| `SpawnMouthFunc` | `0x23A7490` | |
| `SpawnNapkinFunc` | `0x23A74C0` | |
| `SpawnNoseFunc` | `0x23A74F0` | |
| `SpawnNoteFunc` | `0x23A7520` | never mapped to a real in-game item |
| `SpawnParcelFunc` | `0x23A7550` | |
| `SpawnPerchFunc` | `0x23A6F20` | |
| `SpawnPhonographCylinderFunc` | `0x23A7580` | shared class - see caveats |
| `SpawnPhonographEarFunc` | `0x23A6FC0` | Ear 2 |
| `SpawnPhotographFunc` | `0x23A75B0` | never actually used (Natural strategy) |
| `SpawnPlugInFunc` | `0x23A75E0` | |
| `SpawnSpeechCentreFunc` | `0x23A7610` | |
| `SpawnSweetsFunc` | `0x23A7680` | never mapped to a real in-game item |
| `SpawnVisionCentreFunc` | `0x23A6F70` | |

**Verification discipline**: an offset's position in the disassembly next to
a class's string reference was treated as a *lead*, never confirmation.
Every candidate had to be spawned live, then checked via the game's own
tree-dump console command to confirm the resulting object's real class name.
This caught at least one real mistake: the address originally believed to
be Napkin's spawn function (`SpawnMagazineFunc`, by position in
`initClassList()`) turned out to actually construct a `CMagazine` - left
mapped to Magazine rather than discarded, since it was a real, working
result just for a different item. Napkin's real spawn function was never
re-located.

**Items deliberately never added to the active spawn-function map** (even
where a confirmed offset existed above):

- **CarryParrot**: by design, never independently swapped/copied - Feathers
  (its child at game start) was the item actually tracked.
- **HoseEnd**: rides along with Hose automatically as part of the same
  subtree move - only Hose itself was ever independently swapped.
- **Eye1 / Eye2**: confirmed to share *one* class (`CEye`, via
  `SpawnEyeFunc`) with no known way to set a per-instance differentiator -
  spawning either would have produced an object indistinguishable from the
  other. The same problem affected the four fuse/bridge pieces below.
- **SeasonBridge / FanBridge / BeamBridge / ChickenBridge** (the four
  fuses): all share `CBridgePiece`, differentiated in the real engine only
  by an internal `_string6` this app never found a way to read/set.
- **Phonograph Cylinder / Phonograph Cylinder 1/2/3**: likely all four share
  `SpawnPhonographCylinderFunc` too (same shared-class problem) - never
  confirmed either way, deferred out of caution.
- **NoseSpare, DeadHoseSpare, DeadHoseEndSpare**: no dedicated spawn
  function was ever identified, and separately confirmed (via live tree
  dump) to never actually appear anywhere in a normal playthrough.
- **CNote / CSweets / CHeadPiece**: searched for directly in a live tree
  dump and not found anywhere - not just excluded from the tracked list,
  apparently unused content.

### Distinguishing a real item from a spawned decoy (`IsRealItem`)

A spawned copy's `_resource` field (a `CString` at object offset `0xD8`,
`GameObjectResourceOffset` - **this offset is still present in
`GameOffsets.cs`**, since it's also referenced by the read-only field
viewer) is left completely empty, since nothing ever populates it for a
spawned object. A real, naturally-loaded item's `_resource` inline SSO
character buffer (struct start `+0x10` within that field, i.e. object
offset `0xE8` absolute - this was `GameObjectResourceBufferOffset`, now
removed) holds real filename text on a fresh load (e.g. Magazine's is a
`"z"`-prefixed `"z411.avi"`, later cleared to `"411.avi"` the first time
it's picked up, which is why the check read the full 8 bytes and checked
for *any* nonzero content, not just the first byte).

```csharp
// Former GameOffsets.cs constant (removed):
// public const long GameObjectResourceBufferOffset = 0xE8;

public static bool IsRealItem(MemoryReader mem, long itemAddr)
{
    long? bufferBytes = mem.ReadInt64(itemAddr + GameOffsets.GameObjectResourceBufferOffset);
    return bufferBytes is not null and not 0;
}
```

This field is **strictly read-only** - a live test that tried
placement-constructing a filename into it (to fix a spawned copy's missing
drag graphic) crashed the game on pickup. Whatever the engine actually does
with this field involves more than "is there a filename here."

### Moving the real item to safety (`petMoveToHiddenRoom`)

```cpp
void CGameObject::petMoveToHiddenRoom() {
    CPetControl *pet = getPetControl();
    if (pet) {
        makeDirty();
        pet->moveToHiddenRoom(this);
    }
}
```

Module offset `0x23A31D0` (`PetMoveToHiddenRoomFunc`), still present in
`GameOffsets.cs` and still in active use today by `GameActions.
MoveItemToHiddenRoom`/`MoveItemToHiddenRoomFull` for the current
hide-in-place design and for `ItemTracking.HideUngrantedFuses`. This
function itself was never decoy-specific - only its *use* as "where the
real object goes once a decoy has taken its place" was part of the removed
design.

### The swap itself (`ItemSwapping.SwapItemForDecoy`)

```csharp
public static long? SwapItemForDecoy(MemoryReader mem, CarryItemLocation item)
{
    if (item.ParentAddress is null)
        return null; // no known parent to place the decoy under

    long? decoyAddr = ItemSpawning.SpawnNamedCopy(mem, item.Name);
    if (decoyAddr is null)
        return null;

    bool movedRealAway = GameActions.MoveItemToHiddenRoom(mem, item.Address);
    if (!movedRealAway)
        return null; // decoy exists but is unplaced/unparented - leaked but harmless

    bool placed = GameActions.MoveItemToRoom(mem, decoyAddr.Value, item.ParentAddress.Value);
    return placed ? decoyAddr : null;
}
```

Ordering mattered: spawn the decoy first (touches nothing in the tree yet),
move the real object away, *then* place the decoy exactly where the real
one used to be - so the two objects were never both sitting under the same
parent simultaneously.

### Attach-time orchestration (`ItemSwapping.PerformInitialSwap`)

Ran once per attach, gated on `IsFreshGameState` (nothing in inventory yet,
`PassengerClass` still `None`) to avoid ever swapping a mid-playthrough
save. For every item name with both a confirmed spawn function and the
`Swap` pickup strategy, it grouped all objects sharing that name and checked
whether a decoy for that name already existed *anywhere* (not just its
original spot) - the real fix for a once-live bug where reattaching to an
already-swapped game (real item long since relocated into the mail system
mid-puzzle) couldn't tell that apart from "never swapped" using only
parent-address checks, and would swap it a second time, leaking a duplicate
decoy into the hidden room.

### Two pickup strategies (`ItemPickupStrategy.cs`)

```csharp
public enum ItemPickupStrategy
{
    Swap,     // default - decoy substituted at attach time
    Natural,  // real object left completely untouched
}
```

`Natural` existed for exactly one item, Photograph, for the missing-cutscene
-state reason described above. `Chicken`, `BeerGlass`, and `Music System
Key` were noted as plausible future `Natural` candidates once their AP
location mappings were confirmed - that never happened before the whole
strategy split was superseded by today's uniform design, where *every*
tracked item is handled the "Natural" way (untouched until naturally found,
then hidden/mailed/restored - no swap ever).

### Debug/testing tooling that existed around this system

- **Items tab "Copy?" column**: showed `IsRealItem` per row so a decoy could
  be visually distinguished from a real object while testing.
- **Items tab "Spawn Item" button**: spawned a same-named copy of the
  selected item and moved it into inventory, for manual testing of
  `ItemSpawning.SpawnNamedCopy` outside the automatic attach-time swap.
- **Debug tab "Spawn Test Item [experimental]"**: a lower-level tool that
  called an arbitrary module-relative offset as a zero-argument factory
  function, named the result, and moved it into inventory - used to test
  *candidate* spawn function addresses (before they were confirmed and
  promoted into `ItemSpawning.SpawnFuncOffsets`) without needing to edit
  code and rebuild each time. Replaced an earlier standalone
  `test_spawn_candidate.ps1` script. Included a safety check
  (`MemoryReader.IsWithinModule`) refusing to call anything outside the
  module's own mapped range, added after passing an absolute address here
  by mistake once crashed the game outright.

## What was kept

- `PetMoveToHiddenRoomFunc` / `GameActions.MoveItemToHiddenRoom(Full)` - the
  hide-in-place design still needs to stash a real item pending its AP
  grant, and `ItemTracking.HideUngrantedFuses` still needs to hide
  server-granted fuses the player shouldn't find naturally.
- `NamedItemNameOffset`, `CStringCharPtrCtorFunc`, `GameActions.
  ConstructCString` - still used for naming/text-construction elsewhere
  (conversation logging, the field viewer's display), unrelated to
  spawning specifically.
- `GameObjectResourceOffset` (`0xD8`) - still read by the field viewer
  (`ItemFieldsForm.cs`) as a general diagnostic field, even though its
  *use* for real/decoy detection (`GameObjectResourceBufferOffset`, `0xE8`)
  was removed.
- `ItemTracking.ServerGrantedFuseNames` / `HideUngrantedFuses` - moved from
  the now-deleted `ItemSwapping.cs` into `ItemTracking.cs` (its logic never
  depended on spawning or decoys, just on hiding an object that already
  exists).

## What was removed

- `client/Game/ItemSpawning.cs` (entire file) - `SpawnFuncOffsets`,
  `TryGetSpawnFuncOffset`, `CanSpawn`, `IsRealItem`, `SpawnNamedCopy`.
- `client/Game/ItemSwapping.cs` (entire file) - `IsFreshGameState`,
  `SwapItemForDecoy`, `PerformInitialSwap` (`HideUngrantedFuses` moved to
  `ItemTracking.cs` first, since it's still called from
  `MainForm.Attach.cs`).
- `client/Game/ItemPickupStrategy.cs` (entire file) - `ItemPickupStrategy`
  enum and `ItemPickupStrategies.GetStrategy`; its only caller was
  `ItemSwapping.PerformInitialSwap`.
- All `Spawn*Func` constants in `GameOffsets.cs` (see table above) and
  `GameObjectResourceBufferOffset`.
- Items tab: "Spawn Item" button, its `DoSpawnItem` handler, and the "Copy?"
  list column.
- Debug tab: "Spawn Test Item [experimental]" section, its input fields
  (`_txtSpawnOffset`, `_txtSpawnName`), button, and `DoSpawnTestItem`
  handler.
