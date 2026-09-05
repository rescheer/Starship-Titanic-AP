# Design Notes & Removed Comments

This document preserves the rationale, design decisions, cross-file relationships, and reverse-engineering provenance that used to live in inline comments and doc comments throughout the codebase. Code comments were pared down to short, factual descriptions; the deeper "why" behind each piece lives here instead, organized by source file.

## apworld/starship_titanic (Python) and client root


### apworld/starship_titanic/__init__.py

- This is a first-pass / revision-2 implementation built from a walkthrough-derived puzzle-logic model. It has **not** been tested against a live Archipelago core. Before relying on it, validate with `python Generate.py` (or the WebHost generator) against the installed AP version, and specifically check that API calls like `Region.connect()`, `place_locked_item()`, and the `Options` dataclass pattern still match that version's `BaseClasses`/`Options` modules — these have drifted across AP releases historically.
- `CHECKS_AND_ITEMS.md` (shipped alongside this world) is the full human-readable reference this code implements; it's also handed to whoever builds the client-side mod, so it should stay in sync with `location_table`/`item_table`.
- Changelog notes from the previous revision to this one:
  - `create_items()` now respects `STItemData.quantity`, because Progressive Passenger Class Upgrade needs 2 copies in the item pool (one per class tier).
  - `create_items()` now explicitly pads the pool with filler items to match the non-event location count exactly, rather than assuming the AP core auto-generates filler to cover a per-player location/item count surplus. That assumption is **not** reliably true across AP forks/versions — relying on it produced a real `FillError` in practice: a "Player X had N more locations than items" log line followed by dozens of permanently unfilled locations (one per excess slot per player). The pool size and non-event location count are now asserted equal after padding, as a safety net.
- In `create_items()`, items with `code is None` (i.e. events) are skipped when building the pool — events never enter the multiworld item pool.
- The deficit-padding block: do NOT rely on the AP core to auto-pad a per-player location/item count mismatch with filler. That behavior isn't guaranteed across AP forks/versions, and silently assuming it exists is exactly what caused the FillError described above. The code pads explicitly, every time, so generation never depends on core behavior that may differ by fork/version.
- `fill_slot_data()`'s returned dict is handed to the client mod at connect time. It's kept small and stable so the client doesn't need to know Archipelago's internal item IDs — it can just match on these readable event/location names (e.g. `"titania_parts"`, `"progressive_class_upgrade_item"`).

### apworld/starship_titanic/items.py

- `quantity` field on `STItemData`: how many copies of this item go into the pool (used for Progressive Passenger Class Upgrade, which needs 2).
- Titania's eleven parts are progression items that also form the `"Titania Parts"` item group specifically so `rules.py` can check `state.has_all(group, player)` once evaluating the "Titania Repaired" location/event.
- Two of Titania's parts are deliberately *not* listed as regular pool items and instead exist as events:
  - Titania's Vision Center is the reward for the Titanic Titillator puzzle chain (the Bar). Granting it early (e.g., as a random pool item) would skip a large amount of puzzle content, so it's implemented as an event tied to completing that puzzle rather than a placeable item.
    - TODO (from original author): consider making it an option whether the Vision Center is a granted item.
  - Titania's Auditory Center is held in the Maitre'D Bot's Right Arm and is released once the music puzzle is complete — also implemented as an event rather than a pool item.
- The "Useful" item classification bucket (`_useful_items`) is intentionally empty. The author's reasoning: they tried to think of anything that would qualify as a Useful (non-progression, non-filler) item, but nearly everything in the game is directly required for progression. The Red Fuse is only needed for optional content, but since checks exist on that content, it has to be classified as progression rather than useful.
- Event items (`_event_items`, code=None) are never placed in the multiworld item pool — they are always locked to their corresponding location via `regions.py` / `rules.py` (see `place_locked_item` usage in `regions.py`).
- The `PROGRESSIVE_CLASS_UPGRADE`, `SECOND_CLASS_TIER`, `FIRST_CLASS_TIER` constants at the bottom of the file exist as convenience constants shared with `rules.py` and `__init__.py`, so the passenger-class tier thresholds aren't magic numbers duplicated/scattered across files.

### apworld/starship_titanic/locations.py

- The "Bottom of the Well / Broken Elevator" section originally noted: it wasn't considered worth creating a separate AP region for the always-accessible elevator, so its locations live under the "Bottom of the Well" region instead of their own region.
- The "Visited" locations (one per region) have no in-fiction item attached — they're just a check for reaching that room at all. Access to them is governed entirely by the owning region's entrance rule in `rules.py`, so none of these individual locations need a location-level rule of their own (unlike other checks in the table that layer extra requirements on top of region access).
- The "1st Class Room - Titania's Eye (Light)" location has an inline note identifying it as the Chevron Puzzle Room (Floor 7 / Elevator 2 / Room 3) — a cross-reference to the game's internal room numbering, kept as a label in the code (not removed, since it's a short structural identifier rather than narrative rationale).

### apworld/starship_titanic/options.py

- Player options are deliberately minimal for this first pass. `items.py` intentionally has more real locations than real items (see the padding logic described in `__init__.py`'s `create_items()`, which fills the shortfall with filler items). Because of that, options which would change either the location count or the item count (e.g., an "include side content" toggle) are deferred to a follow-up revision — specifically, once the client-mod side of this project exists and can be used to test such options against real gameplay.

### apworld/starship_titanic/regions.py

- Regions are deliberately coarse — one per "area" as described in the walkthrough — rather than per individual room, because the source material's logic gates (item/class requirements) operate at that area-level granularity, not at a finer per-room level.
- Entrance rules (attached in `rules.py`) encode the class-upgrade/item gates from the logic model. Per-location rules — for checks that need something extra beyond simply reaching the region — also live in `rules.py`, but are attached directly to individual `Location` objects rather than entrances.
- `REGION_CONNECTIONS` (the `(from_region, to_region)` tuple list) is used purely for documentation and region-creation/connection order when building the region graph. The actual traversal access rules are attached separately, in `rules.py`, via `multiworld.get_entrance(name, player).access_rule = ...`.
- Entrance names follow an `"A -> B"` naming convention (e.g. `"Top of the Well -> 2nd Class Lobby"`), which is how `rules.py` looks them up via `entrance(name)`.

### apworld/starship_titanic/rules.py

Rules encode the game's puzzle-dependency logic. The rationale behind each gate, previously inline, was:

- Region entrance rules:
  - `Top of the Well -> 2nd Class Lobby` and `-> Bottom of the Well`: 2nd Class content requires one copy of the Progressive Passenger Class Upgrade.
  - `Top of the Well -> 1st Class Lobby`: 1st Class content requires both copies of the progressive upgrade (tier 2).
  - `Top of the Well -> Creator's Chamber`: needs the Red Fuse removed and turned (this is part of the "E-Mail" section of the game). Physically, Creator's Chamber is a direct child of Top of the Well (per the game's own Rooms.csv data) rather than a child of Titania's Room — the Red Fuse item itself is just fetched from Titania's Room first, which is why the dependency looks like it crosses regions.
  - `Titania's Room -> Bridge`: only opens once Titania is fully repaired (state has "Titania Repaired").

- Location rules (checks needing something beyond simply reaching their region):
  - `DeskBot - 2nd Class Upgrade`: giving DeskBot the Magazine is what triggers this check.
  - `DeskBot - 1st Class Upgrade`: DeskBot won't entertain a 1st Class request until the player is already 2nd Class.
  - `Bilge Room - Titania's Olfactory Center` / `Bilge Room - Blue Fuse`: both are Mother/Bilge Room pickups that need the Feather.
  - `Parrot Lobby - Titania's Nose`: needs the Hose.
  - `2nd Class Room - Titania's Ear (Pistachio Bowl)`: needs the 2nd Class upgrade.
  - `Broken Elevator - Titania's Eye (Elevator)`: needs the LiftBot Head installed.
  - `Promenade Deck - Hammer`: needs the Perch to press the dispenser button.
  - `SGT Class Lobby - Long Stick`: needs the Hammer to break the glass.
  - `Arboretum - Lemon`: needs the Long Stick to knock it down.
  - `Promenade Deck - Pureed Starlings`: needs the Blue Fuse, plus 2nd Class for deck access.
  - `Bar - Titania's Vision Center`: the Titillator drink needs all three ingredients (Lemon, Crushed TV, Bar Glass).
  - `Music Room - Titania's Ear (Phonograph)`: requires solving the music room puzzle, which involves visiting the 1st Class Restaurant — modeled here as requiring 1st Class tier. TODO (from original author): detect precisely when the music puzzle is solved, rather than gating on class tier as a proxy.
  - `1st Class Restaurant - Napkin` / `- Green Fuse` / `- Maitre'D Bot's Right Arm`: all three sit on Scraliontis' table, unlocked by defeating the Maitre'D Bot (modeled as requiring 1st Class tier).
  - `1st Class Restaurant - Titania's Auditory Center`: needs the Music Room puzzle solved (modeled as 2nd Class) and the Maitre'D Bot's Right Arm.
  - `Arboretum - Titania's Speech Center`: needs the Arboretum powered (Green Fuse) and the Long Stick.
  - `Arboretum - Titania's Mouth`: needs the Arboretum powered (Green Fuse) and BOTH Maitre'D Bot arms in hand simultaneously (i.e., after the Music Room puzzle has released them). TODO (from original author): detect precisely when the music room puzzle is done, similar to the Phonograph TODO above.
  - `Parrot Lobby - Titania's Core`: the Parrot's Chicken puzzle needs the Napkin and the Yellow Fuse.
  - `Titania's Room - Repair Titania`: requires all eleven Titania parts.
  - `The End - Return Home`: Titania Repaired is already required to reach the Bridge, so no further items are needed to trigger the ending puzzle itself. TODO (from original author): eventually add the Photograph as a late-game item to gate the final starcontrol puzzle at the Bridge.

### client/AppInfo.cs

- `AppInfo` is a central place for the app's display name and version so that the title bar (and anywhere else that needs it later) stays in sync with the single `<Version>` property in `StarshipTitanicAp.csproj`, instead of a hand-maintained version string duplicated in multiple places.
- `Version` is read at runtime from the assembly version, which the .NET SDK derives from the csproj's `<Version>` element. The code trims the trailing ".0" revision component that the SDK pads on by default, so the displayed version (e.g. "0.1.0") matches exactly what's written in the csproj, rather than showing a padded four-part version number (e.g. "0.1.0.0").

### client/Program.cs

No comments required cleanup — the file only contains the `Main` entry point with no rationale/design comments.

## client/Archipelago and client/Memory


### client/Archipelago/ArchipelagoConnection.cs

- **Class purpose**: Thin wrapper around an Archipelago.MultiClient.Net
  session. Owns the connect/disconnect lifecycle and reports state via
  `StateChanged`. `StateChanged` can fire from a background thread — the
  connect attempt runs via `Task.Run`, and socket callbacks arrive on the
  library's own thread — so callers on a UI thread must marshal back
  themselves (e.g. via `Control.BeginInvoke`), the same way `MainForm` does
  for everything else.

- **`GameName` constant**: Reported to the AP server as the game this client
  implements; must match the name used by the Starship Titanic AP world
  (the apworld).

- **`SendTimeoutMs` / the dead-connection detection design**: The library's
  `SocketClosed` event only fires for a socket the local machine actually
  knows is closed (a clean close handshake, or a failed send). If the
  server process dies without either side sending a close frame, the local
  socket can sit there looking "open" indefinitely — and a blocking send
  against it can hang for a long time (however long the OS takes to notice
  the peer is gone), not just fail to be detected. That hang used to happen
  on the calling thread, which for every send in this class used to be the
  UI thread (WinForms Timer ticks call straight into
  `SendLocationCheck`/`SendCommand`) — so a killed server didn't just go
  undetected, it froze the whole app.

  Fix: every actual socket write now runs on a background thread via
  `TrySendAsync`, bounded by `SendTimeoutMs`. The calling thread is never
  blocked, and a send that doesn't complete in time is itself treated as
  proof the connection is dead — which restores real dead-connection
  detection without needing a synthetic heartbeat probe at all (an earlier
  attempt at a heartbeat kept causing side effects and was abandoned; not
  worth repeating).

- **`GetPendingCheckNames`**: Snapshot of every location name currently
  queued but not yet confirmed sent, for display purposes (e.g. the
  Archipelago tab's pending-checks list) — not guaranteed to still be
  accurate by the time the caller reads it, since sends happen on
  background tasks. Returns names, not ids (see `SendLocationCheck`'s
  rationale for why the queue is keyed by name at all). Seed tags (see
  `PendingCheck`) aren't exposed here — display doesn't need them, only
  `FlushPendingChecksAsync` does.

- **`ResolveLocationId`**: Resolves an AP location name to its numeric id
  via the connected session's data package (fetched automatically by
  Archipelago.MultiClient.Net as part of `TryConnectAndLogin` — no manual
  `GetDataPackage` request needed). Returns null if there's no active
  session, or if the name doesn't exist in this game's data package
  (`GetLocationIdFromName` returns -1 for "not found", per the library's
  own docs). This — not any hardcoded id table — is the only place a
  location name ever becomes a location id in this app. See
  `LocationChecks.cs` for why: the server's own data package is
  authoritative, so a stale/renumbered `locations.py` can never cause a
  wrong id to be sent — at worst, a name resolves to nothing and the check
  simply stays queued.

- **`ResolveLocationName`**: Display-only (e.g. a message-log line
  referencing a location by id) — never used to decide what to send.

- **`GetLocationCheckSummary()`**: Checked/total counts straight from the
  session's own location helper (`AllLocationsChecked`/`AllLocations`) —
  not tracked separately by this app.

- **`GetLocationCheckSummary(names)`**: Scoped variant — Total only counts
  names that actually resolve against the session's data package. Meant for
  callers like `LocationChecks.SuccUBusStationLocationNames`.

- **`ClearPendingChecks`**: Drops every queued-but-unsent check without
  sending it — exists for stale checks surviving a deliberate server reset
  during testing (see `LocationCheckQueue.Clear`).

- **`SlotData`**: The slot data the server sent back on login
  (`fill_slot_data()` in the .apworld) — e.g. this world's
  `progressive_class_upgrade_item`, `second_class_tier`, `first_class_tier`
  keys (consumed by `ClassUpgradeTracker`). Null until a successful
  connection.

- **`SeedName`**: The server's `seed_name` (from the `RoomInfo` packet,
  exposed by the library as `session.RoomState.Seed`), captured once login
  succeeds. Uniquely identifies this multiworld generation — used by
  `SaveSeedGuard.cs` to detect a save file that belongs to a different seed
  (or was played without the client attached at all) before this app ever
  reads or writes anything on it. Null until a successful connection, and
  reset to null alongside `SlotData` on every disconnect/failure.

- **`_pendingChecksFlushedThisSession`**: Whether `FlushPendingChecksAsync`
  has already run for the current session — set by
  `NotifyGameVerifiedForSeed`, which callers (`MainForm`) are meant to
  invoke once per tick once they've confirmed it's safe. Reset to false on
  every fresh connection attempt and on every disconnect, so a later
  reconnect — even to the same seed — gets its own fresh
  verification/flush cycle.

- **`_receivedItemNames`**: Every item name received this connection, in
  order, including the replayed history of everything received in past
  sessions (the server resends all of it on every reconnect — see the
  `ItemReceived` subscription notes below). Cleared at the start of each
  `ConnectAsync`. Guarded by a lock since it's written from whatever thread
  the library's `ItemReceived` event fires on and read from
  `GetReceivedItemNames()` on the UI thread's tick loop.

- **`_seenItemLocationPairs`**: Defense-in-depth against any future resync
  replay (server-initiated desync recovery, not just the client-initiated
  Sync heartbeat that caused this exact bug once already — see the
  `SendTimeoutMs` story above for why there's no active heartbeat anymore).
  A `ReceivedItems` packet reset to index 0 re-delivers the full history
  through `ItemReceived`; without this set, every already-recorded item
  would silently get counted again. `(ItemId, LocationId)` uniquely
  identifies a specific granted item, so legitimate duplicates (the same
  item genuinely placed at two different locations) still count correctly
  — only an exact replay of the same (item, location) pair gets filtered.

- **`GetReceivedItemNames`**: Deliberately raw names, not counts or
  classifications — interpreting what they mean (e.g. how many class
  upgrades) is game-specific and belongs in something like
  `ClassUpgradeTracker`, not here.

- **`MessageReceived` event**: Fires for every server log-line the AP
  client library surfaces via `session.MessageLog` (item sends/receives,
  hints, chat, join/leave, etc.), already formatted as plain text via
  `LogMessage.ToString()`. Can fire from a background thread.

- **`ItemReceived` event**: Fires once per item as it's received (including
  the replayed history on every reconnect). Just the item's display name;
  for anything beyond "an item with this name arrived", use
  `GetReceivedItemNames()` instead of trying to accumulate state from this
  event yourself.

- **`CheckQueued` event**: Fires when a location check is newly added to
  the pending-check queue — i.e. `SendLocationCheck` was called for a name
  that wasn't already queued. Does NOT fire for a redundant re-enqueue of a
  name that's already pending.

- **`SocketClosed` handler**: Only surfaces an unexpected drop as a state
  change if this is still the active session (`Disconnect()` already nulls
  `_session` out before tearing the socket down).

- **`ItemReceived` subscription timing**: Subscribed before
  `TryConnectAndLogin` rather than after a successful login — the library
  replays the full history of already-received items through this same
  event on every (re)connect, and that replay can start as part of the
  login call itself, before it returns. Subscribing early is how you're
  meant to catch it (per the library's own docs). `DequeueItem()` returns
  an `ItemInfo` — `ItemName` is null if it couldn't be resolved (e.g.
  DataPackage not loaded yet), in which case there's nothing meaningful to
  record.

- **Not flushing pending checks on login success**: Deliberately NOT
  flushing pending checks right after a successful login — a login
  succeeding only proves we're talking to this seed's server; it says
  nothing about whether the locally attached save actually belongs to it.
  Pending checks stay queued until a caller (`MainForm`, once its
  save/seed guard confirms Ok) explicitly says it's safe via
  `NotifyGameVerifiedForSeed`.

- **`SendCommand`**: `commandText` is sent exactly as given — no `!` is
  added, so pass it already prefixed if you want the AP server's command
  processor to treat it as a command (e.g. `"!hint"`) rather than plain
  chat. The actual send happens on a background thread (see
  `TrySendAsync`) — this returns as soon as it's handed off, not once
  delivery is confirmed, so the return value only means "there's a session
  to send through", not "the server has it".

- **`SendLocationCheck`**: Identified by AP location NAME rather than a
  numeric id — see `LocationChecks.cs` for why nothing in this app
  hardcodes ids anymore. Safe to call repeatedly for the same name — the
  server treats duplicate checks as a no-op. Always queues the name first
  (optimistically — removed from the queue only once the send actually
  confirms; tagged with the currently connected seed, null if none), then
  — if there's a session and the name resolves to an id — attempts the
  actual send on a background thread. If there's no session, the name
  doesn't resolve yet (data package not loaded), or the send fails or
  times out, it's simply left queued — retried automatically on next
  connect. Returns false whenever the send couldn't even be attempted; true
  means "handed off", not "confirmed delivered".

- **`NotifyGameVerifiedForSeed`**: Tells this connection it's now safe to
  replay pending checks against the currently connected seed — i.e. the
  caller (`MainForm`) has confirmed, via its own save/seed guard (see
  `SaveSeedGuard.cs`), that the locally attached save actually belongs to
  this seed, not just that login to some AP server succeeded. Until this
  is called, `FlushPendingChecksAsync` never runs — a queued check
  surviving from a previous, possibly different, seed must never go out
  just because *a* login happened to succeed. No-ops if there's no active
  session, or if this has already run once for the current session
  (idempotent — safe to call every tick once the guard is satisfied).
  Fire-and-forget, same as every other send path on this class.

- **`FlushPendingChecksAsync`**: Attempts to send every queued check tagged
  for the currently connected seed, now that both the connection AND the
  locally attached save have been confirmed to match it (only caller:
  `NotifyGameVerifiedForSeed`). Stops at the first failure/timeout rather
  than churning through the rest of the batch against a connection that's
  likely already gone again. A check queued under a different seed (or
  under no seed at all — queued before this app had ever connected) is
  never auto-replayed here — only the currently connected seed's own
  checks are safe, since that's the one `NotifyGameVerifiedForSeed`'s
  caller actually verified the attached save against; left queued rather
  than dropped, in case the player later reconnects to the seed it was
  meant for. Ids are resolved fresh against this session's just-loaded
  data package, not hardcoded — a name that still doesn't resolve here
  shouldn't normally happen for anything `LocationChecks.cs` actually
  produced, but could for a leftover queued name from a mistyped/old
  build; such names are left queued rather than silently dropped.

- **`TrySendAsync`**: Runs a blocking send on a background thread with a
  bounded timeout, so a socket that's silently hung (the OS hasn't yet
  noticed the peer is gone) can never freeze the calling thread — which
  for every caller in this class is the UI thread. If the send doesn't
  complete within `SendTimeoutMs`, or throws, the connection is declared
  dead: on a healthy connection a local send call completes near-instantly,
  so either outcome is good evidence something's actually wrong, not just
  slow.

### client/Archipelago/LocationCheckQueue.cs

- **`PendingCheck`**: Null `SeedName` means the check was queued before
  this app ever completed a connection (so no seed was known yet at
  enqueue time) — such an entry is never auto-flushed, since there's
  nothing to confirm it's safe to send to whatever seed we later connect
  to.

- **`LocationCheckQueue` class purpose**: Queues location checks that
  couldn't be sent (not connected, or the send itself failed) and retries
  them whenever a connection becomes available. Persisted to disk so a
  check survives the app being closed and reopened while still
  disconnected, not just a same-run reconnect.

  Keyed by the AP location NAME, not its numeric id — see
  `ArchipelagoConnection.SendLocationCheck`'s rationale for why: a location
  id can only be resolved via a connected session's data package, so a
  check made while offline (or made once, before the app has ever
  connected) has no id to queue in the first place. The name is always
  known locally (from `LocationChecks.cs`), so that's what gets queued and
  persisted — resolved to an id only at the moment of an actual send
  attempt.

  Each entry also carries the seed_name it was queued under. This queue
  isn't itself scoped to one seed/server — a leftover entry from a
  previous connection can sit here across an app restart — so the seed tag
  is what lets a caller (`ArchipelagoConnection`) tell a check that's
  actually safe to replay against the currently connected seed apart from
  a stale one queued under a different seed entirely, which must never be
  auto-sent.

  Location names are naturally deduplicated (backed by the dictionary key)
  — AP treats resending an already-completed check as a no-op anyway, so
  there's no reason to track the same name twice even locally.

  NOTE (historical): this replaces an earlier id-keyed version of this
  file, and later an unseeded name-keyed version. A leftover
  `pending_checks.json` from either earlier version will simply fail to
  deserialize as the current object shape and `Load()` will return an
  empty list — any truly-pending checks from before this change are lost,
  not corrupted or misinterpreted. Considered an acceptable one-time cost,
  same as the earlier migration.

- **`Enqueue`**: Adds a name if not already present, tagged with the seed
  it was queued under (null if not currently connected to any seed), and
  persists immediately — so a check made right before the app is killed or
  crashes isn't lost. Returns true only if newly added (an existing
  entry's stored seed tag is left untouched on a redundant enqueue), so
  callers can tell a genuinely new pending check apart from a harmless
  re-enqueue.

- **`Snapshot`**: For a caller that wants to attempt sending queued checks
  itself (e.g. asynchronously, one at a time, off the calling thread)
  rather than handing this class a synchronous send callback.

- **`Clear`**: For clearing out stale entries after a server reset during
  testing/development, or for a player who deliberately wants to discard
  checks left over from a different seed.

### client/Archipelago/LocationChecks.cs

- **Class purpose**: Maps this app's internal engine concepts (room names,
  item names, a blocked class-upgrade attempt) to the actual AP location
  NAME strings defined in the starship_titanic .apworld (`locations.py`) —
  not numeric ids. Location ids are never hardcoded here or anywhere else
  in the client: the only authoritative source for name->id (and
  id->name) resolution is the server's own data package, fetched
  automatically by Archipelago.MultiClient.Net as part of connecting and
  queried via `ArchipelagoConnection.ResolveLocationId` /
  `ResolveLocationName`.

- **`RoomToLocationName["TheEnd"]`**: Sent via the normal room-visit path;
  reaching room 18 ("TheEnd" per `RoomNames.cs`) only ever happens once, as
  the very last thing before the game ends.

- **`SuccUBusStationLocationNames`**: Every distinct Succ-U-Bus (including
  Mother Succ-U-Bus) "Visited" location name from
  `PointOfInterestLocationName`, built once since the table never changes
  at runtime. Several RNVs map to the same station name (e.g. the two
  Arboretum states), so it's deduplicated via `Distinct()` rather than
  just `Values.Count`.

- **`GetReadableRoomName`**: For UI purposes only, not used for any AP
  lookup. Reuses `RoomToLocationName`'s own display text (stripping its
  `" - Visited"` suffix) since that's already the readable name this app
  maintains for every mapped room; falls back to the raw engine name for a
  room with no mapping.

- **`TryGetClassUpgradeLocationName`**: Given the PassengerClass value
  `ClassUpgradeHook.PollAttemptedClass` reports the DeskBot tried to set.
  Returns false for anything outside 1/2 — there's no location for that.

- **`TryGetApItemName`**: Used to check whether AP has already granted the
  item a player just picked up naturally (see
  `MainForm.GameLogic.cs`'s `ReconcileTrackedItems`). Returns false for
  anything not in the table — e.g. Photograph, which has no location
  mapping in `ItemPickupLocationName` and, per `items.py`, no
  corresponding item entry at all (yet). Callers should treat that as
  "can't currently be reconciled with AP" rather than guessing a name.

- **`ApItemNameToItem`**: Reverse of `ItemToApItemName`, built once — AP
  grants arrive keyed by AP item name, but locating the real object in the
  game's tree needs the engine name (`ItemNames.All`) instead. Small
  table, built eagerly rather than lazily since it never changes at
  runtime.

- **`TryGetEngineItemName`**: The reverse of `TryGetApItemName`. Used by
  `MainForm.GameLogic.cs`'s item-grant delivery to find the real object in
  the game's tree (`GameState.FindAllCarryItems`) by name, regardless of
  whether it's ever been swapped or picked up. Returns false for an AP
  item name with no corresponding engine item — e.g. the progressive
  class upgrade item, or anything else the table doesn't cover — callers
  should skip those rather than guess.

### client/Archipelago/ClassUpgradeTracker.cs

- **Class purpose**: Interprets received "Progressive Passenger Class
  Upgrade" items into the engine's PassengerClass value — driven entirely
  by the apworld's own slot_data (`fill_slot_data()` in `__init__.py`),
  not hardcoded here:
    - `progressive_class_upgrade_item`: the item's display name
    - `second_class_tier` / `first_class_tier`: how many copies of that
      item need to have been received to reach Second/First class

  Takes plain item-name strings (see
  `ArchipelagoConnection.GetReceivedItemNames`) rather than talking to the
  AP session/library types directly, since `ItemInfo.ItemName` is already
  resolved for us there — no need for this class to know anything about
  the AP library's types at all.

- **`ComputeClass`**: Engine enum values are 1=First, 2=Second, 3=Third,
  4=None. Returns null if either slot data doesn't have what's needed to
  compute it (e.g. connected to a different/older world version) or not
  enough upgrade items have been received yet to warrant a change. Callers
  should leave the in-game class alone whenever this returns null — null
  is not "downgrade to nothing", it's "nothing to do".

### client/Archipelago/ConnectionSettings.cs

- **Class purpose**: Persists the last-used Archipelago server/slot/
  password to a small JSON file in the user's local app data folder, so
  `MainForm` can pre-fill the Archipelago tab on next launch instead of
  starting blank every time.

  The password is stored in plain text. That's an acceptable trade-off for
  a convenience file on a single-user machine, but this is NOT a secrets
  store — don't extend it to hold anything more sensitive without adding
  real protection (e.g. DPAPI) first.

- **`Load`**: A broken settings file should never prevent the app from
  starting — corrupt file, permissions issues, etc. all just fall back to
  an empty `Data`.

- **`Save`**: Best-effort; failure to persist (e.g. no write access) is
  silently ignored rather than surfaced as an app error, since this is a
  convenience feature, not something the rest of the app depends on.

### client/Memory/ClassUpgradeHook.cs

- **Class purpose**: Intercepts `CGameObject::setPassengerClass()` so
  talking to the DeskBot can no longer change the passenger class on its
  own — class access is meant to come exclusively from receiving the
  matching item through the multiworld (see `ClassUpgradeTracker` /
  `GameActions.SetPassengerClassFull`) — AND records which class was
  attempted, so the client can still tell the AP server "this location was
  reached" (see `LocationChecks.TryGetClassUpgradeLocationName`) even
  though the actual class change never happens.

  Structurally this now matches `TextCommandHook` rather than an earlier
  bare single-byte patch: a detour to an allocated stub that captures the
  attempted class into a small mailbox (poll via `PollAttemptedClass`) and
  returns immediately, WITHOUT running any of the original function — the
  class field is still never written and `CPetControl::reset()` still
  never runs, exactly as before, but now the attempt itself is observable.

  The original disassembly (module base + `GameOffsets.SetPassengerClassFunc`):
  ```
  push r12                  ; +0
  push rbx                  ; +2
  sub rsp, 0x28              ; +3
  lea eax, [rdx-1]           ; +7  - newClass arrives in rdx/edx
  mov r12, rcx               ; +10 - this
  mov ebx, edx               ; +13 <-- hook cuts right after this, at +15
  cmp eax, 3                 ; +15 (a clean instruction boundary)
  ja ...
  ```
  rdx/edx still holds the untouched incoming newClass argument at the
  moment the detour fires, since it intercepts at byte 0, before any of the
  original prologue has run.

- **`OriginalBytesLength = 15`**: through "mov ebx,edx" — a clean
  instruction boundary.

### client/Memory/MemoryReader.cs

- **Class purpose**: Thin wrapper around the Win32
  ReadProcessMemory/WriteProcessMemory APIs. Every read method returns
  null on failure (invalid handle, unmapped address, etc.) rather than
  throwing — callers treat null the same way the original Python tooling
  treated `None`: "not ready right now", not an error to surface. Write
  methods return bool for success/failure.

- **`IsWithinModule`**: True if addr falls within the attached module's
  own mapped range `[ModuleBase, ModuleBase+ModuleSize)`. Meant as a
  safety check before ever executing/calling into a candidate address
  computed from user input (e.g. a manually-entered offset) — a mistaken
  value (most often an absolute address passed where a module-relative
  offset was expected) can resolve to something wildly out of range, and
  blindly executing whatever's there is exactly what crashed the game once
  already (see the spawn-candidate test tooling). Doesn't guarantee addr
  is valid *code*, just that it's plausible.

- **`Attach`**: Process name expected with no ".exe" suffix, matching
  `System.Diagnostics.Process` naming.

- **Bitness note** (removed from `Attach`): `MainModule` requires the
  calling process and target to match bitness — this client builds x64
  and the game is x64, so that's fine.

### client/Memory/RemoteCaller.cs

- **Class purpose**: Calls an existing function inside the target process
  by writing a small stub into it and executing that stub on a fresh
  thread via `CreateRemoteThread` — no DLL injection. Direct port of the
  approach in `remote_call.py`; that file's docstring has the fuller
  explanation and the disassembly work that identified each function's
  calling convention.

- **`BuildStub`**: Aligns the stack, sets rcx/rdx/r8/r9d and two stack
  arguments (5th/6th, at `[rsp+0x20]`/`[rsp+0x28]`), calls `funcAddr`,
  then returns cleanly.

- **`Call`**: Does not report the callee's own return value — mirrors
  `remote_call.py`'s behavior, which only surfaced the raw thread exit
  code for diagnostic purposes.

- **`BuildStubWithCapture`**: Captures rax right after the call (before
  the epilogue touches anything) into a caller-provided mailbox address,
  instead of discarding it. Needed for calls whose entire point is the
  returned value — e.g. spawning a new game object via one of the engine's
  own zero-argument `FunctionCXXX` factory functions (see
  `CSaveableObject`'s ADDFN class registry), where the whole result is the
  new object's pointer in rax.

### client/Memory/TextCommandHook.cs

- **Class purpose**: Installs a persistent inline hook on
  `CPetConversations::textLineEntered()`, confirmed live via disassembly
  (see project notes). Unlike `RemoteCaller`'s one-shot
  `CreateRemoteThread` calls, this patches the function's own entry bytes
  with a detour to a stub that stays resident for the rest of the session.

  Behavior: if the typed line starts with `!`, the stub copies it into a
  small mailbox buffer (polled via `PollCommand`), calls the confirmed
  `_textInput` clear function directly, and returns WITHOUT running any of
  the original function — so `CTextInputMsg` / TrueTalk never sees it.
  Anything not starting with `!` falls through to the original, unmodified
  function via a trampoline (re-executing the 19 bytes overwritten by the
  hook, then jumping back past them).

  Also opportunistically captures `CPetConversations`'s own live address
  (see `ConversationsAddr`) — rcx at the very start of the stub is exactly
  that object's `this`, untouched, regardless of which branch runs
  afterward, so it's captured unconditionally before the '!' check even
  happens. Confirmed live via disassembly: rcx gets used later in this
  same stub as `[rcx+TextInputFieldOffset]`, and independently, as the
  direct, unmodified `this` argument when the real `textLineEntered()`
  gets called via the trampoline path — both consistent with rcx being
  `_conversations`'s own address, not something requiring any further
  offset. This was the missing piece needed to eventually call
  `CPetConversations::addLine()` directly (still not itself located).

  IMPORTANT (experimental status note): this is genuinely experimental
  compared to the rest of this app's remote-call mechanisms. Before
  relying on it, verify the installed stub in x64dbg (Ctrl+G to the
  reported stub address) and confirm it disassembles as a sensible
  check-then-branch, not garbage. Test normal (non-'!') typing first to
  confirm the trampoline path is transparent before testing the block
  path.

- **`ConversationsAddr`**: Captured as a side effect of the hook firing
  (rcx at the moment `textLineEntered()` is called is exactly this — a
  plain member-call `this` pointer, no offset applied — confirmed live via
  disassembly, see project notes). Zero until the player has typed and
  submitted at least one line this session, since that's the only time the
  capture actually runs. Cached rather than re-read every time — this
  doesn't change for the lifetime of one attach.

- **`PollCommand`**: Opportunistically picks up the captured
  `_conversations` address, regardless of whether a command is also
  waiting this call — the two are independent pieces of info sharing one
  mailbox, and this only needs to succeed once. The leading `'!'` is
  stripped from the returned text if present (it should always be, since
  that's what the stub checks for before copying).

- **`BuildCaptureConversationsBlock`**: Unconditionally captures rcx
  (`CPetConversations`'s own `this`) into the mailbox before anything else
  runs — safe to run ahead of the '!' check since it doesn't touch
  rcx/rdx/rax, which the check and everything downstream still depend on
  being untouched.

## client/Game


### client/Game/ChevronCodes.cs

This table holds the fixed, hardcoded room-flags constants for rooms with no
natural floor/elevator/room identity - public areas (the Bar, Bridge,
Arboretum, etc.) that can't be expressed via `RoomFlags.cs`'s normal
per-stateroom encoding, so the engine special-cases them by name instead.
There are two separate real tables, both from `CRoomFlags`'s actual source
(`titanic/room_flags.cpp`): `SUCCUBUS_ROOMS` (mail stations) and
`TRANSPORT_ROOMS` (elevator/pellerator-related locations, not mail stations).

This file corrects a real bug from an earlier revision, which only had 13
SuccUBus entries taken from a *different* table -
`CChevCode::GetChevCodeFromRoomNameMsg`, a separate, smaller,
apparently-stale list used for the elevator's chevron-dial UI, not for item
routing - and had at least one outright wrong value: `"1stClassRestaurant"`
was `0x196D9` there, but `CRoomFlags`' own table (the one that actually
matters for `_roomFlags`/`_destRoomFlags`) has `0x896B9` for the same room.
Any mail previously routed to the 1st Class Restaurant using the old value
would have used the wrong code.

Also newly confirmed real (not "best-effort" guesses like the earlier
revision had to assume): `MoonEmbLobby` shares `EmbLobby`'s exact code, and
`MusicRoomLobby` shares `MusicRoom`'s - both directly confirmed by
`CRoomFlags`' own table listing them as separate name entries mapping to the
same value, not inferred from room-name confusion.

`SgtLobby` is NOT in the engine's own static tables at all, despite having a
real, fixed-in-practice SuccUBus - unlike the other rooms in the table, it
apparently has a normal, valid dynamic floor/elevator/room identity that just
always happens to fall within Third Class's valid range (see `RoomFlags.cs`),
so the engine never needed to special-case it. Its entry is kept anyway,
purely for this app's own convenience as a simple name lookup - not because
the engine treats it as one of its own named/special rooms (confirmed live:
its value has bit 0 clear, i.e. `RoomFlags.IsNamedRoom` is false for it,
unlike every other entry in this table). Its code `0xBF018` was confirmed
live via a Magazine's own `_roomFlags`, decoding to floor=30, elevator=3,
room=12, class=Third.

### client/Game/GameActions.cs

- **SetPassengerClass**: A raw write, not a call into game logic - confirmed
  to correctly gate room access immediately, but the PET's on-screen color
  does not update until a save/reload unless followed by `reset()` +
  `markAllDirty()` (see `SetPassengerClassFull`).
- **SetPassengerClassFull**: Confirmed working live.
- **MoveItemToRoom**: Confirmed to correctly update actual inventory state
  immediately; the PET's visible glyph list does not update until a
  save/reload or other incidental inventory action unless followed by
  `ItemsChanged()` + `ResetPetControl()` (see `MoveItemToInventoryFull`). rcx
  and r8 are confirmed (via live breakpoint on a real pickup) to both be the
  destination room - NOT the item.
- **HiddenRoomAddress**: Learned as a side effect of the first successful
  `MoveItemToHiddenRoom` call this session - there's no independent way to
  resolve this room by name or id, so it's read off whatever was just moved
  there. Cleared on detach (`MainForm.ResetCachedState`) since it's tied to a
  specific process instance and would be stale after a relaunch/reattach.
- **MoveItemToHiddenRoom**: Calls `CGameObject::petMoveToHiddenRoom()` - see
  the historical `GameOffsets.PetMoveToHiddenRoomFunc` derivation notes below
  for how its address was confirmed. Takes only the item itself; internally
  resolves the PET and hidden room on its own. Also opportunistically learns
  `HiddenRoomAddress` by reading back the moved item's own new `Parent` field
  right after a successful move. This is raw-move only (see
  `MoveItemToHiddenRoomFull` for the inventory-aware version) - this alone
  leaves the PET's glyph list stale: `petMoveToHiddenRoom()` only calls
  `makeDirty()` internally (a visual-redraw hint), not
  `itemsChanged()`/`setArea()`/`reset()` (which rebuild the PET's actual
  internal item list) - confirmed live, an item moved via this alone could
  still be picked back out of a PET slot that, per the tree, no longer
  actually held it.
- **MoveItemToHiddenRoomFull**: Mirrors `MoveItemOutOfInventoryFull` exactly,
  just using `petMoveToHiddenRoom()` instead of the generic detach/attach
  wrapper for the actual move. Use this (not the raw version) whenever the
  item is currently sitting in the player's inventory, which for this app's
  purposes is effectively always.
- **NotifyItemsChanged**: Calls `CPetInventory::itemsChanged()` - the real
  function that rebuilds the PET's visible glyph list from the current tree
  state. Confirmed via live disassembly of `CPetControl::addToInventory()`
  during a real item pickup. Takes `&_inventory` (petControl + 0x6D8), NOT
  petControl itself, as its argument.
- **SetPetAreaInventory**: Calls `CPetControl::setArea()` with
  area=PET_INVENTORY (0), matching the exact call captured in
  `addToInventory()`'s disassembly right after `itemsChanged()`. Likely what
  actually tells the currently visible PET tab to recompute its layout -
  `itemsChanged()` alone updates the underlying list but apparently doesn't
  force the active tab to redraw with it.
- **RefreshPetControl**: Shared by the "item entered inventory" and "item
  left inventory" flows, since both leave the same `CPetControl`'s display
  stale, just via opposite moves. `markAllDirty(gameManager)` is the last
  step and turned out to be required, not optional: `itemsChanged()` /
  `setArea()` / `reset()` alone rebuild the PET's internal list/layout state
  correctly, but without a forced full redraw the screen doesn't actually
  repaint until something else does (a node/view transition) - which showed
  up as new items not appearing immediately, and a stale
  selection-highlight rectangle being left on screen after a removal. This
  mirrors `SetPassengerClassFull`, which already paired `reset()` +
  `markAllDirty()` and was confirmed to update on screen with no transition
  needed. CRITICAL: `petControlAddr` must be an actual `CPetControl`. These
  calls dereference `CPetControl`-specific fields and will crash the game if
  run against any other `CTreeItem` (e.g. `CMailMan`) - see
  `GameActions.MoveItemToRoom`.
- **MoveItemSmart**: General-purpose move that does the right thing
  regardless of whether the item is entering the inventory, leaving it, or
  neither (e.g. mail room -> world room). Reads the item's current parent
  BEFORE moving it, then afterward refreshes whichever side - old parent or
  new destination - actually matches the known `CPetControl` address, since
  that's the only side whose display could have gone stale and the only side
  it's safe to run these calls against. If `petControlAddr` is null (not
  resolved yet) or neither side matches it, this behaves exactly like a plain
  `MoveItemToRoom`. The "nothing actually moved relative to the inventory"
  early-return avoids a pointless (though harmless) extra refresh call.
- **ResetPetControl**: Calls `CPetControl::reset()` - the real,
  source-confirmed fix for the stale PET display (found in
  `CGameObject::setPassengerClass()`, which calls this after changing the
  class). Takes only the `CPetControl` object itself as an argument. The
  "inventory room" address this app has resolved since early in the project
  (via the 3-NoName-siblings tree search) IS the `CPetControl` object itself
  - confirmed live by comparing it against `CGameObject::getPetControl()`'s
  real return value.
- **DisplayPetMessageText**: Calls `CPetControl::displayMessage(const
  CString&, int)` - the free-text overload. Confirmed via disassembly:
  `CString`'s layout is a 4-byte size + 4-byte padding, then an 8-byte
  `char*` at offset +8. We don't build a fully "real" `CString` - we fake a
  16-byte header where +8 points at the actual text bytes (written into the
  same remote allocation, right after the header), which is all the function
  actually reads. Byte layout: bytes 4-7 are padding, left zero; the pointer
  field at +8 needs to point at the text bytes, which aren't known at their
  final remote address until after allocation, so it allocates first with a
  placeholder, then patches it in.

  KEPT DELIBERATELY, despite `AddConversationLine` being the better choice
  for logging: this shows a message immediately regardless of which PET tab
  is active, but does NOT reliably persist into the conversation log the way
  `AddConversationLine` does (the original motivation for finding that
  function at all - a message shown only via this could be lost/never seen
  if the player wasn't looking at the right tab at the right moment). The
  plan is to use both together later - `AddConversationLine` for the
  permanent log entry, and this for an immediate on-screen notification
  specifically when the player isn't currently on the Conversation tab (not
  yet built).
- **ConstructCString**: Placement-constructs a `CString(const char*)`
  directly over `fieldAddr`, via the confirmed `CString` constructor (see the
  historical `GameOffsets.CStringCharPtrCtorFunc` derivation notes below).
  Used by `AddConversationLine` - only safe against a field/buffer that's
  currently empty/SSO with nothing to leak.
- **DisplayMessage**: Calls the real `CPetConversations::displayMessage(const
  CString&)` - see the historical `GameOffsets.PetConversationsDisplayMessageFunc`
  derivation notes below for how this was distinguished from the many
  inlined call sites. Unlike `AddConversationLine`, `conversationsAddr` is
  passed as-is (this function applies `ConversationSubObjectOffset` itself),
  and no separate `MarkAllDirty` call is needed - the function's own tail
  (conditional notify + a flag byte at `[this+0x540]`) is expected to cover
  the same redraw concern, though that's not yet confirmed live across all
  four conversation/tab-state combinations the way `AddConversationLine` has
  been.
- **DisplayMessageSmart**: Picks how to show a message based on which PET tab
  is currently visible (`GameState.GetCurrentPetArea`): on the Conversation
  tab, `DisplayMessage` alone is enough (it already logs AND is visible
  there). On any other tab, `DisplayMessage` still logs the message but isn't
  seen immediately, so `DisplayPetMessageText` (the `CPetControl` overload,
  which shows at the bottom of whichever tab is active but doesn't reliably
  persist to the log) is also called, so the player sees it right away
  without losing the permanent log entry.
- **MarkAllDirty**: Calls `CGameManager::markAllDirty()` - the function
  ScummVM's own "pet on" debug command calls to force a redraw. Confirmed
  sufficient on its own for PET visibility toggling; not sufficient alone for
  inventory or class color (those need the fuller sequences above).
- **SetItemMailDestination**: Mimics the state left by a real, completed
  delivery (`CMailMan::setMailDest` / SuccUBus receive flow): `_roomFlags`
  set to the destination, `_isPendingMail` cleared. `_destRoomFlags` is left
  untouched since it isn't checked by `findMailByFlags`.
- **MarkItemAsToolPlaced**: Only meaningful for an already-delivered item
  (`_roomFlags != 0`) - that's the state where `findMailByFlags` stops
  consulting `_destRoomFlags` at all, so the sentinel can't affect real
  mail-retrieval logic. Always call this AFTER `SetItemMailDestination` has
  already set a real `_roomFlags` value, never before.
- **UnmarkItemAsToolPlaced**: Used when an item leaves the mail system via
  this app, so a stale marker can't resurface if the same item is later
  routed there again by normal gameplay. Callers should only invoke this
  after confirming the item's current `_destRoomFlags` actually IS the
  sentinel, so an organically-mailed item's real pending-destination value is
  never touched.
- **SetItemVisible**: Used around the item restoration cycle (see
  `MainForm.GameLogic.cs`'s `TryRestoreItemsAtHomeRnv`/`RevertRestoration`):
  forced false while an item sits Restored at its home parent, back to true
  once it reverts (or is picked up for real).
- **SetItemBounds**: Same packed-int16-pair layout `ItemFieldsForm.cs`
  already decodes for display. Needed for items whose true click region
  isn't recreated by a generic tree re-attach (see
  `GameObjectRestoreOverrides` - `BrokenLiftbotHead` specifically loses its
  authored bounds once naturally picked up, since the vanilla game never
  needs to show it in-world again after that).
- **CallLoadFrame**: See the historical `GameOffsets.LoadFrameFunc`
  derivation notes below for what it actually does (seeks `_surface` to the
  given frame; does NOT touch `_bounds` despite reading it). This is the same
  call `CCarry::EnterViewMsg` makes (`loadFrame(_enterFrame)`, gated by
  `_enterFrameSet`) the one time the engine itself ever puts a carryable back
  into a view - the generic `MoveItemSmart` re-attach skips it entirely,
  leaving `_surface` parked on whatever frame it was last set to (e.g. its
  last inventory/hidden appearance), which is what actually made a restored
  `BrokenLiftbotHead` unclickable - `checkPoint()` pixel-tests the current
  frame's content at `_bounds`, not just `_bounds`/`_visible`/`_cursorId`.
- **MoveToFirstChild**: Raw sibling-list surgery. The engine's own generic
  `attach()` (both the 0x242AA30 wrapper and `CGameObject::moveToView()`)
  appends to the TAIL of the sibling list, but a fresh-save
  `BrokenLiftbotHead` is confirmed live to be its parent's FIRST child - one
  of the only remaining observed differences between a working and a bugged
  instance, so worth forcing to match even though nothing in the engine
  source reviewed so far explains why it'd matter (no
  list-order-dependent hit-testing found in `checkPoint()`/`scan()` so far).
  `itemAddr` must already be a child of `parentAddr`.
- **ReadItemPersistedState**: Reads from `GameOffsets.GameObjectUnused4Offset`
  - a real object field, so this survives reattach/restart/save-reload
  exactly like every other field on the object. A failed read (e.g. address
  invalid) returns `ItemPersistedState.None`, same as a genuinely
  never-touched item - both cases mean "nothing this app needs to know about
  is going on here".

### client/Game/GameOffsets.cs

All offsets were found and verified via manual reverse engineering (Cheat
Engine + x64dbg + ScummVM Titanic engine source), and confirmed stable across
multiple relaunches. See the original Python prototypes (`track_final.py`,
`list_inventory.py`) for the full derivation history.

- **RoomFromGameManager/NodeFromGameManager/ViewFromGameManager**: The
  persistent room/node/view holder is a nested object at
  `gameManager+0xE728`, not `gameManager` itself.
- **PassengerClass**: Confirmed via 4-way save-file diff: 1=First, 2=Second,
  3=Third, 4=None.
- **PetActive**: Confirmed via before/after byte diff toggling ScummVM's "pet
  on"/"pet off" debug console commands: clean 0/1 flip, no other candidates.
  Stored as a single byte; `GameState.ReadPetActive` reads via `ReadBytes`
  rather than `ReadInt32` since only the flip at this exact byte offset was
  confirmed, not that the surrounding 3 bytes are meaningfully part of the
  same field.
- **LoadFrameFunc**: Found by setting a write breakpoint on `_frameNumber`
  (`GameObjectFrameNumberOffset`, +0xB0) and confirming the hit function's
  disassembly against the known source: writes `_frameNumber=-1`, reads
  `_surface` (+0xD0, `GameObjectSurfaceOffset`), virtual-calls
  `_surface->setMovieFrame(frameNumber)` (vtable+0xF8) if non-null, then
  virtual-calls `this->makeDirty()` (vtable+0x48) and tail-calls into a
  shared redraw-invalidation routine passing `&_bounds` (+0x104,
  `GameObjectBoundsOffset`) - confirms all three of those offsets
  independently as a side effect. That tail call reuses the CURRENT
  `_bounds` value for a dirty-rect registration, not a recompute - `loadFrame`
  does NOT fix stale `_bounds` on its own, a manual `SetItemBounds` is still
  needed alongside it. Absolute address captured live: 0x7FF713A6EBE0,
  module base that session: 0x7FF7116D0000 -> offset 0x239EBE0.
- **PetControlResetFunc**: Confirmed via `setPassengerClass()`.
- **InventoryItemsChangedFunc**: Confirmed via `addToInventory()`.
- **PetConversationsFieldOffset**: Confirmed by direct subtraction
  (`ConversationsAddr - CPetControl` address, both read from the same
  attach) rather than the disassembly-scan technique used for most other
  offsets in this file. Cross-checked against `pet_control.h`'s real
  declaration order: `_conversations` is declared BEFORE `_inventory`, so
  this offset should be - and is - smaller than `PetInventoryFieldOffset`,
  with the gap between them (0x570) landing on a plausible
  `sizeof(CPetConversations)`. This makes `TextCommandHook`'s live capture of
  `ConversationsAddr` no longer load-bearing for `AddConversationLine`
  specifically - that can now resolve `_conversations` directly from
  `CPetControl`'s address on every attach, without needing the player to
  have typed anything first. The hook is still how `addLine()` itself was
  originally found, and `TextCommandHook.ConversationsAddr` is kept as a live
  cross-check (see `GameState.ResolveConversationsAddr`) rather than removed.
- **SetAreaFunc**: Confirmed via `addToInventory()`, called right after
  `itemsChanged()`.
- **DisplayMessageFunc**: Confirmed via class-restriction message trace.
- **SetPassengerClassFunc**: The DeskBot's own vanilla class-upgrade trigger
  (see `Memory/ClassUpgradeHook.cs`). Confirmed live via disassembly: calls
  `CGameObject::getPetControl()` (0x23A1E30) then tail-jumps into
  `CPetControl::reset()` (0x2429D10) - both known addresses resolve to the
  same module base from this function's own address, cross-confirming it.
- **PreviousPassengerClass**: Written by `setPassengerClass()` right before
  it overwrites `PassengerClass` with the new one. Not currently used for
  anything, but found in the same disassembly pass as `SetPassengerClassFunc`,
  so recorded in case it's useful later (like detecting what class a blocked
  upgrade attempt would have set).
- **NamedItemNameOffset**: `CNamedItem::_name` field offset, relative to the
  object's own base (any CNamedItem-or-deeper object, i.e. anything findable
  via `TryReadName`). Derived from confirmed source layout: `CTreeItem` is
  0x30 bytes (vtable + 4 pointers + int + padding, matching the
  already-verified Parent/NextSibling/FirstChild offsets exactly), and
  `CNamedItem` adds nothing before `_name`.
- **CStringCharPtrCtorFunc**: `CString(const char*)` constructor (really
  `BaseString<char>`'s). Confirmed live via disassembly AND matches the
  layout `DisplayPetMessageText` already assumed: +0x0 int32 size, +0x8
  char* data pointer (self-pointing into +0x10 for short/SSO strings), +0x10
  the SSO buffer itself. Safe to call as a placement-construction directly
  over a field that's currently empty/SSO (nothing to leak) - NOT safe to
  call over a field that might already hold a real heap allocation, since
  this function doesn't check or free whatever was there first.
- **ConversationAddLineFunc**: Found by stepping through
  `CPetConversations::textLineEntered()` live (module offset
  `TextLineEnteredFunc`) and tracing which call both the "NPC conversation
  active" and "no one to talk to" branches converge on - a function called
  identically from both paths, targeting the same sub-object, is exactly the
  signature an unconditional history-logging function should have. Confirmed
  via extensive live testing: appending a message this way is correctly
  logged in ALL FOUR combinations of in/out-of-conversation and on/off the
  Conversation tab - unlike `DisplayPetMessageText`, which only reliably
  displays when the Conversation tab already happens to be active.

  Call shape: `rcx = (ConversationsAddr + ConversationSubObjectOffset)`,
  `rdx` = a real CString (by reference, not by value - see
  `GameActions.ConstructCString`), `r8` = `ConversationAddLineKnownGoodR8`.
  `ConversationsAddr` itself is NOT a static/computable offset - it's only
  known once captured live via `TextCommandHook`'s hook, since there's no
  independent way to resolve `CPetConversations`' own address otherwise.
- **ConversationSubObjectOffset**: Offset from `CPetConversations`' own base
  to the specific sub-object `addLine()` actually operates on (likely the
  line-history storage itself, not `CPetConversations` directly) - confirmed
  live via the same trace as `ConversationAddLineFunc`.
- **ConversationAddLineKnownGoodR8**: r8 at the addLine call site is
  `CPetConversations::getColor(1)`'s return value - confirmed live via
  disassembly of `displayMessage()` itself: its call to addLine loads r8d
  directly from `getColor(1)`'s eax result, and that result is this exact
  constant.
- **PetConversationsDisplayMessageFunc**: Found via xrefs to
  `ConversationAddLineFunc`: most call sites turned out to be
  `displayMessage()` inlined into a larger caller (no standalone prologue, a
  temporary CString destructor call in place of scrollToBottom()), but this
  one is the real, non-inlined function - standard push/push/push/sub rsp
  prologue, rcx/rdx exactly matching the source signature (rcx = this, rdx =
  msg CString&), and its own body confirms addLine's r8 meaning above. Body
  (module offsets, all relative to this function's own base): `getColor(1)`
  -> `addLine(_log suboffset, msg, color)` -> scrollToBottom-equivalent at
  +0x240D970 (called on the `_log` suboffset, not `this`) -> a conditional
  notify at +0x239F710 (guarded on `[this+8]` being non-null) -> sets a flag
  byte at `[this+0x540]`. Unlike `AddConversationLine` (which calls addLine
  directly and needs the caller to add `ConversationSubObjectOffset` itself),
  this function takes the plain `CPetConversations` `this` and applies that
  offset internally.
- **GameObjectResourceOffset**: `CGameObject::_resource` field offset (a
  CString), relative to the object's own base. Triangulated from three
  independent sources that all agree exactly: `game_object.h`'s real field
  order ("CVideoSurface *_surface; CString _resource;" - back to back,
  nothing between them), the disassembly-confirmed CString layout (+0x0
  size, +0x8 data ptr, +0x10 SSO buffer), and a live address (a real
  Magazine's `_resource` data-ptr field and SSO buffer both landed exactly
  where this offset predicts). STRICTLY READ-ONLY: a live test tried
  placement-constructing a filename into this field (same safe technique
  already used for `_name`) - and crashed the game on pickup. Whatever the
  engine does with this field involves more than "is there a filename here",
  so don't write to it. `_surface` itself (the `CVideoSurface*` actually
  driving the drag graphic) is the 8 bytes immediately before this, at
  object+0xD0 - also don't write to that either: it's a raw, non-refcounted
  pointer with no ownership safety, and a live test sharing one between two
  objects also crashed the game.
- **Full CGameObject field layout table**: Derived directly from
  disassembly of the object's constructor (the same trace
  `GameObjectResourceOffset` came from), not from engine source - every field
  was actually seen being initialized. `CTreeItem` (0x00-0x2F: vtable +
  tree-linkage pointers) and `CNamedItem`'s `_name` (0x30, a CString) come
  first and aren't repeated in the table.
- **_unused3 / GameObjectUnused3Offset**: Now TEMPORARILY reused on
  BeamBridge only, as a save/seed guard tag - see `SaveSeedGuard.cs`.
- **_unused1 / GameObjectUnused1Offset**: Now reused on every mailed item as
  this app's own tool-placed/game-placed mail marker - see
  `ToolPlacedSentinel` below and `GameActions.MarkItemAsToolPlaced`.
- **GameObjectUnused4Offset**: This app's own per-item state storage (see
  `ItemPersistedState.cs`, `GameActions.ReadItemPersistedState`/
  `WriteItemPersistedState`) - a single packed int is plenty, since
  `ResolveHomeParent` (see `GameState.cs`) resolves every address this app
  ever needs live, fresh, from the item's own hardcoded home RNV rather than
  needing to persist any raw address at all (which wouldn't survive a game
  restart - see `ItemHomeLocations.cs`'s derivation notes on why addresses
  are never stable across relaunches). Unlike `_destRoomFlags`/
  `ToolPlacedSentinel` (already in active use for the Debug/Items tabs' own
  manual mail-placement tooling, and the direct cause of two separate
  live-tested bugs when this app's own automatic tracking was overloaded onto
  it too), this field is confirmed genuinely unused by the engine itself - no
  collision risk with anything else that reads or writes it.
- **Removed offsets (historical note)**: This file used to also carry
  `FullViewNameOffset` (0x1F0), `CompareViewNameToFunc` (0x23A0F60), and
  `FullViewNameSelfPointerOffset` (0x1F8) - an attempt to detect "is the
  player standing in this item's home view" natively, via a
  `CGameObject::compareViewNameTo` call and a read of the item's own
  `_fullViewName` CString. Two rounds of live testing falsified this: the
  native call restored every tracked item at once regardless of the player's
  actual room, and a follow-up fix (dereferencing what was assumed to be the
  string's self/heap pointer) still found nothing readable at all for a
  fresh-save Perch - meaning the field's true layout was never actually
  understood, not just imprecisely offset. Removed rather than left around
  under a false-confidence offset. Item-home-view detection is now driven
  entirely by the hardcoded RNV table in `ItemHomeLocations.cs`, sourced from
  `docs/Item Reference.md`'s manually recorded Home RNV per item - see
  `MainForm.GameLogic.cs`'s `UpdateItemViewRestoration`.
- **PetMoveToHiddenRoomFunc**: `CGameObject::petMoveToHiddenRoom()` -
  stashes an item under the hidden room via
  `CPetControl::moveToHiddenRoom(this)`, used as the "safe storage"
  destination for a naturally-picked-up item pending its AP grant (see
  `MainForm.GameLogic.cs`'s `ReconcileTrackedItems`) and for server-granted
  fuses awaiting pickup prevention (see `ItemTracking.HideUngrantedFuses`).
  Source:
  ```
  void CGameObject::petMoveToHiddenRoom() {
      CPetControl *pet = getPetControl();
      if (pet) {
          makeDirty();
          pet->moveToHiddenRoom(this);
      }
  }
  ```
  Cross-confirmed live: the `getPetControl()` call inside this function
  resolves to module offset 0x23A1E30 - an exact match against the
  already-independently-confirmed `GetPetControlFunc`, not just a
  similar-looking address. The trailing tail-call's argument setup
  (rcx=pet, rdx=this) also matches `pet->moveToHiddenRoom(this)`'s calling
  convention exactly. Takes only `this` (rcx) - no other arguments needed.
  Still worth a live behavioral check before relying on it further (move a
  real, trackable item, then dump the tree and confirm it's actually gone
  from where it was) - the structural match is strong, but nothing in this
  project gets trusted purely from disassembly.
- **PetControlMoveToHiddenRoomFunc**: `CPetControl::moveToHiddenRoom()`
  itself - the tail-call target inside `PetMoveToHiddenRoomFunc`. Not
  currently called directly (`PetMoveToHiddenRoomFunc` already does
  everything needed in one call), but recorded since it was seen live and
  may be useful later.
- **Mail-related fields (ItemIsPendingMail/ItemDestRoomFlags/ItemRoomFlags)**:
  Confirmed live via chevron code round-trip (Napkin sent to "Bar", 0xB3D97
  found at +0x114).
- **ToolPlacedSentinel**: Sentinel written into `_unused1`
  (`GameObjectUnused1Offset`) for items this app has delivered to the mail
  system. Originally lived in `_destRoomFlags` instead, on the theory that
  once an item is actually delivered (`_roomFlags != 0`) the real game's own
  `findMailByFlags()` never consults `_destRoomFlags` again, making it dead
  weight safe to reuse. Moved to `_unused1` because `_destRoomFlags` is live
  engine state while an item is still in transit (not yet delivered), and
  nothing this app's own field-diagnostics/mail-tab code did with it
  accounted for that - `_unused1` is genuinely engine-unused (unlike
  `_unused3`, reserved for `SaveSeedGuard.cs`'s BeamBridge-only seed tag,
  which would collide since BeamBridge is mailed like any other tracked
  item), so it carries no such risk. All legitimate chevron/room-flags
  values are packed from a handful of small bitfields (ELEVATOR/
  PASSENGER_CLASS/FLOOR/ROOM - see the engine's own `room_flags.cpp`) and the
  known SuccUBus codes in `ChevronCodes`, none of which come anywhere near
  the top of the 32-bit range, so a full-width sentinel like this can never
  collide with a real value there either. Being part of the object's own
  serialized fields, this survives detach/reattach, game restarts, and
  save/reload exactly like the item's real location does - no external
  bookkeeping needed. NOTE: a second sentinel,
  `AwaitingRestorationSentinel` (0xFFFFFFFE), used to live here - reusing
  this same `_destRoomFlags` field for this app's own "granted before ever
  being found naturally" tracking, alongside `ToolPlacedSentinel`'s
  unrelated manual-tooling meaning. That overload was the direct cause of
  two separate live-tested bugs (an item auto-delivered by
  `MainForm.MailTab.cs`'s `DeliverQueuedMailAtStation` the moment it shared
  this field with a real destination-bearing mail item; then a second bug
  once that was fixed, from `ResetCachedState` losing track of which
  sentinel meant what across a reattach). Removed in favor of a dedicated,
  genuinely engine-unused field with no such collision risk at all - see
  `GameObjectUnused4Offset` and `ItemPersistedState.cs`.
- **PetControlCurrentRoomFlags**: `CPetControl`'s own current room-flags
  value (see `RoomFlags.cs` for the encoding), relative to the PET control's
  own base address - NOT one of `CGameObject`'s confirmed fields, this is
  inside `CPetControl`/`CPetRooms` territory. Confirmed live via a targeted
  memory scan: computed the expected combined roomFlags value from the
  in-game PET Rooms panel's displayed Floor/Elevator/Room numbers (matching
  `RoomFlags.Compute` exactly), then searched a wide region of the PET
  control's own memory for that exact 32-bit value - found a single match
  here, no ambiguity from a small-number coincidence the way a
  3-separate-ints search would risk. Not yet cross-checked from a second
  location/room - worth confirming it updates correctly when the player
  actually moves, not just that it matched once.

### client/Game/GameState.cs

- **SpecialPassengerClassValues.BridgeAccessClassValue**: The value
  `CGameObject::setPassengerClass()` is called with once, from the game's
  own internal code (not the DeskBot dialogue), as part of granting Bridge
  access after Titania's repair - see `MainForm.Tick.cs`'s
  class-upgrade-hook handling for how this was found live. Numerically
  identical to `PassengerClass.None` (both 4), but NOT the same thing: None
  was derived from a fresh, pre-upgrade save's passenger-class field and
  means "no class chosen yet", while this is a distinct, later, one-shot
  engine event that happens to reuse the same field value - kept as its own
  named constant rather than `PassengerClass.None` to avoid conflating the
  two.
- **GameState class**: Resolves live game state from the process's memory,
  using the confirmed offset chain in `GameOffsets`. Every method returns
  null (or an empty result) rather than throwing when a hop isn't currently
  readable - e.g. the player is at a menu, mid-load, or between states.
- **ReadCurrentRoomFlags**: Returns null if the read fails; a genuine 0
  (e.g. before ever being set) is a valid return value, not an error.
- **TryReadName**: Mirrors the heuristic used throughout the original
  Python tooling - `CNamedItem`'s exact CString layout was never pinned down
  precisely, so this scans a range instead.
- **FindNoNameSiblings**: Index 0 is confirmed (via extensive live testing)
  to be CPetControl/inventory. Index 2 has been observed (via mail testing)
  to be CMailMan. Index 1 is CStarControl - confirmed via a full tree dump
  ("dump" debug console command), which shows all three as siblings under
  the same CDontSaveFileItem: CPetControl NoName, CStarControl NoName,
  CMailMan NoName, in that order. Not currently used by this app.
- **FindInventoryRoom**: Confirmed live against real pickups/drops - see
  `list_inventory.py` for the original derivation of why index 0 is the
  right one to trust.
- **ResolveConversationsAddr**: Computed directly from the PET control's own
  address via the confirmed static offset (see
  `GameOffsets.PetConversationsFieldOffset` - confirmed by direct
  subtraction against a live capture, cross-checked against
  `pet_control.h`'s real declaration order). Available immediately on
  attach, unlike `TextCommandHook.ConversationsAddr`, which needs the player
  to have typed something first.
- **GetCurrentPetArea**: See `GameOffsets.PetControlCurrentAreaOffset`'s
  historical derivation notes above for how this field was found, and the
  `GameOffsets.PetArea*` constants for the known values. Null on a failed
  read (e.g. not attached).
- **FindMailManRoom**: See `FindNoNameSiblings` for the caveat on index
  reliability.
- **FindAllCarryItems**: A full-game-tree walk visits far more nodes than
  the shallow NoName-siblings search, so it gets its own, much higher node
  budget (200,000 vs. 20,000). Mirrors what the game's own debug console
  does (`Debugger::cmdItem` -> `findByName`), just done as a single sweep
  instead of many separate by-name searches, using only the same
  FirstChild/NextSibling/Parent primitives already confirmed elsewhere - no
  new offsets required. This is a much heavier walk than anything else in
  this file - call it sparingly (manual refresh, or a slow polling
  interval), not on every tick.
- **ClassNameCache**: Class-name resolution results, keyed by vtable pointer
  value (not object address) - see `TryGetClassName`. Cleared on
  detach/reattach in `MainForm.Attach.cs`'s `ResetCachedState`, since ASLR
  means the same vtable address could belong to a different class after a
  fresh launch.
- **TryGetClassName**: Resolves an object's C++ class name via a genuine
  virtual call: reads the object's vtable pointer, calls vtable slot 0
  (which takes no arguments beyond the implicit `this`) to get a
  class-descriptor address, then reads the descriptor's string-literal
  pointer at +0x08 and the ASCII text it points to. Confirmed live against
  CPerch/CNose/CMagazine and others - see this feature's task doc for the
  full derivation. The remote call goes through `RemoteCaller`
  (CreateRemoteThread under the hood), which is expensive - results are
  cached by vtable pointer, since every instance of a class shares the same
  vtable and therefore the same class name.
- **MaxRoomSearchNodes**: Rooms sit shallow under `_project` (CProjectItem
  -> several CFileItem siblings -> CRoomItem children each - see the tree
  dump structure referenced in `ItemHomeLocations.cs`'s derivation notes),
  so this needs nowhere near `FindAllCarryItems`' 200k-node budget - a room
  is never itself nested inside another room's own Node/View subtree, so
  this stops descending the moment it finds ANY CRoomItem, matching or not,
  keeping the walk cheap even on a mismatch.
- **FindRoomByName**: Never descends into a room's own children, matching or
  not - a room's Node/View subtree can't contain another room. Part of
  `GameState.ResolveHomeParent`'s Room/Node/View resolution - see
  `ItemHomeLocations.cs`'s derivation notes for how this was confirmed
  reliable.
- **NthChildOfClass**: Other sibling types are skipped, not counted (e.g.
  SgtLobby has a CPETMonitor before its first CNodeItem child). Part of
  `GameState.ResolveHomeParent`'s Room/Node/View resolution (a room's Nth
  CNodeItem child, then that node's Mth CViewItem child) - see
  `ItemHomeLocations.cs`'s derivation notes for how the ordinal-position rule
  was confirmed reliable for both.
- **MaxDescendantSearchNodes**: A view's own subtree (everything from
  CLinkItems to CMusicalInstruments, per the tree dump) is typically a few
  dozen nodes - several orders of magnitude cheaper than
  `FindAllCarryItems`' 200k-node budget, but given a generous cap anyway
  since this is still a real (if small) tree walk with a remote
  `TryGetClassName` call per node.
- **FindDescendant**: Bounded search of rootAddr's OWN subtree (its children
  and their descendants - never rootAddr itself, and never rootAddr's
  siblings). Part of `GameState.ResolveHomeParent` - used when an item's true
  default parent (see `ItemHomeLocations.TryGetDefaultParent`) isn't the
  view itself but a further-nested holder object under it (a CSearchPoint, a
  CDropTarget, a CNoseHolder, etc.).
- **ResolveHomeParent**: The whole point of this project's
  Room/Node/View-ordinal-position research (see `ItemHomeLocations.cs`'s
  derivation notes) - resolves a full-state-machine item's home parent
  address, live, without a full `FindAllCarryItems` tree walk. Combines
  `ItemHomeLocations.TryGetHomeRnvs` (which RNV(s) count as home) with
  `TryGetDefaultParent` (whether the item's true parent is the home view
  itself, or a further-nested holder object under it):

  Room -> Nth CNodeItem child -> Mth CViewItem child -> (if the item's
  default parent class isn't "CViewItem") the matching named descendant
  within that view's own subtree.

  An item with more than one home RNV (e.g. Hose, obtainable in either
  Arboretum state) tries each in order and returns the first that resolves -
  only one will actually exist in a given save (the other Arboretum state's
  view simply won't contain the matching holder). Returns null if the item
  has no HomeRnvs/DefaultParent entry at all (e.g. Magazine - mail-delivered,
  no resolvable in-world home parent), or if any resolution step fails.
- **ReadMailItems**: Reads every item currently parented under CMailMan,
  along with their mail-routing fields.

### client/Game/ItemHomeLocations.cs

- **DefaultParent**: See `ItemHomeLocations.TryGetDefaultParent`'s full
  derivation notes below for how it's meant to be used.
- **HomeRnvs table**: Each full-state-machine item's authored "home" (Room,
  Node, View) triple, hand-recorded in `docs/Item Reference.md` and
  transcribed here verbatim - not derived from any in-memory field. Replaces
  an earlier attempt to detect this natively via
  `CGameObject::compareViewNameTo` and the item's own `_fullViewName`
  CString, which two rounds of live testing showed was never actually
  understood correctly (see `GameOffsets.cs`'s historical note on those
  removed offsets, above). Keyed by the exact same engine names used in
  `LocationChecks.ItemToApItemName` - every full-state-machine item (see
  `ItemTracking.IsFullStateMachineItem`) has an entry here. Most items have
  exactly one home RNV; a couple (e.g. Hose, obtainable in either Arboretum
  state) have two - either is treated as "home".
- **ItemsByRnv**: Reverse of HomeRnvs, built once - used to cheaply check
  "does anything call this RNV home" on every RNV change without scanning
  every item's RNV list each time.
- **DefaultParents table**: Each carryable item's true default (fresh-save)
  parent, identified by name + class - NOT address, since addresses are
  heap-allocated and do not survive a relaunch (confirmed via two
  independent fresh-save captures with 0/38 matching raw addresses, but
  38/38 matching ParentName+ParentClass). Captured via
  `MainForm.ItemsTab.cs`'s "Export Parent Snapshot" tool on a genuinely
  fresh save - see `snapshot_20260829_142136.csv` at the repo root for the
  full raw capture (ancestor chains, addresses, etc.) this table was
  transcribed from.

  A null ParentName means the item's direct parent IS an unnamed CViewItem
  itself (ParentClass == "CViewItem"), not a further-nested holder object -
  the common case for an item just sitting in a view with nothing else
  holding it.

  Broader than HomeRnvs - covers 34 of the 38 carryable items this project's
  export tool successfully resolved (one-directional items, CarryParrot, the
  four bridge fuses, and all three Phonograph Cylinders included, none of
  which have a HomeRnvs entry), not just the 19 full-state-machine items.
  NoseSpare, the un-suffixed "Phonograph Cylinder", DeadHoseSpare, and
  DeadHoseEndSpare are confirmed (not just absent-from-capture) to never
  appear anywhere in the actual game - excluded entirely rather than given
  dead entries.

  Used for validation/self-healing (a cheap, address-independent identity
  check against whatever `FindAllCarryItems` already returns) AND as
  `GameState.ResolveHomeParent`'s second stage: Room/Node/View resolution
  via ordinal sibling position gets you to the right CViewItem, but an item
  whose ParentClass here isn't "CViewItem" is actually held by some
  further-nested holder object under that view (a CSearchPoint, a
  CDropTarget, a CNoseHolder, etc.) - this table is what lets
  `ResolveHomeParent` find that exact holder instead of stopping one hop
  short. Both the View's ordinal-position assumption and this two-stage
  resolution approach are now confirmed against live data (cross-referenced
  against two independent fresh-save captures, `snapshot_20260829_142136.csv`
  and `snapshot_20260829_144746.csv`, 19 of 20 items with a known RNV
  matching exactly - the 20th, Magazine, is mail-delivered and genuinely
  doesn't have a resolvable in-world home parent, not a resolution failure).
- **RestoreFieldOverrides**: Needed for items that lose their authored click
  region once naturally picked up once - a raw `MoveItemSmart` re-attach
  re-parents the object correctly, but the engine doesn't recompute
  `_bounds`/`_cursorId` on attach, so an item that's been picked up before
  (state D's whole premise - see `ItemPersistedState.cs`) comes back with
  whatever leftover values the engine left on it after that first pickup,
  which for BrokenLiftbotHead is a zeroed rect, not the rect from its
  original in-room placement.

  Values captured live off a correctly-behaving instance: L245 T258 R321
  B334, cursorId 8 (not CURSOR_ARROW - this item apparently uses a
  non-default cursor even when correctly interactable, so
  `GameObjectCursorIdOffset`'s documented "1 by default" fresh-construct
  value would be wrong here too, not just a safe fallback).

  EnterFrame replicates what `CCarry::EnterViewMsg` does the one time the
  engine itself puts an item back into a view (`loadFrame(_enterFrame)`) -
  see `GameActions.CallLoadFrame`'s historical notes above.
  BrokenLiftbotHead's value (-1) was given directly rather than derived,
  since `CCarry`'s own `_enterFrame` field offset (+0x1B0 off CGameObject,
  per `carry.h`'s declaration order) is still an unconfirmed candidate - see
  `ItemFieldsForm.cs`. Once that offset is confirmed live, this could be
  read directly off the item instead of hardcoded per name.

  KeepVisible is the confirmed actual fix for BrokenLiftbotHead - the
  generic restoration flow (`TryRestoreItemsAtHomeRnv`) forces `_visible`
  false on every restored item on the theory that the room's own baked
  background art already shows it, so the live sprite would be redundant;
  that assumption is wrong for this item specifically, and leaving
  `_visible` false is what actually made it unclickable, not
  bounds/cursorId/frame (those may still be needed too - untested in
  isolation, left in place rather than unwound).

- **SkipFirstChildReorderOnRestore / render order and occlusion (Lemon)**:
  A second, distinct failure mode from the bounds/cursorId/visible class of
  bugs above - an item can be drawn with every one of its own fields
  correct (`_visible` true, `_bounds` matching vanilla's post-drop position
  exactly, `_surface` a valid non-null pointer with real pixel data) and
  still be completely invisible and un-hit-testable, because something
  drawn after it in its parent's sibling list paints over it every frame.
  `CGameObject::draw()`/`checkPoint()` don't gate on anything exotic
  (see `engines/titanic/core/game_object.cpp` upstream) - the missing
  variable is sibling draw order, which lives in the `_firstChild`/
  `_nextSibling` linked list itself, not in any per-object field.

  Diagnosed on Lemon (`CFruit`), granted early via AP mail so
  `TryRestoreItemsAtHomeRnv` re-parents it back into the Arboretum's
  `CViewItem` for a real natural re-pickup. That flow unconditionally ends
  with `GameActions.MoveToFirstChild`, which exists so simple sprite-swap
  items (BrokenLiftbotHead etc.) paint over the room's baked background
  art. For Lemon this was actively harmful: a live tree dump (this app's
  own scene-tree debug listing) showed vanilla's authored order as `_PANL,
  _PANR, SeasonBackground, Lemon, SeasonalAdjust, ...` - Lemon painting
  after (on top of) `SeasonBackground`. Forcing it to be the first child
  instead put it *before* `SeasonBackground` in draw order, so the
  seasonal background art painted over it on every frame - fully rendered
  per its own state, just occluded underneath. Confirmed by the one detail
  that gave it away: the invisible Lemon rendered correctly (same art as
  the settled state) the instant it was picked up and dragged, because the
  cursor-drag "carried item" overlay is a wholly separate draw path from
  the in-scene sibling list, so it bypassed the occlusion entirely.

  Ruled out first, each requiring its own live comparison against a
  vanilla save before landing on this: `_bounds`/`_cursorId`/`EnterFrame`
  overrides (all read identical to vanilla post-drop - see
  `RestoreFieldOverride`'s own history above for why those looked like the
  obvious suspects), the fall-triggering puzzle logic itself
  (`CLemonDispensor::FrameMsg`/`CFruit::LemonFallsFromTreeMsg`/
  `CFruit::FrameMsg` in `fruit.cpp`/`lemon_dispensor.cpp` - confirmed
  firing correctly, animation landing at the correct final position),
  `CFruit`'s own private saved fields (`_field12C`/`_field130`/`_field134`/
  `_field138`, offsets derived from `carry.h`/`game_object.h`'s actual
  declaration order and cross-checked against the already-confirmed
  `CarryCanTakeOffset` - all matched vanilla exactly), parent identity
  (same address, not a different same-classed decoy), and stale rendering
  (survived a full save/load, still invisible - so not a dirty-rect/redraw
  caching issue either).

  The fix: `ItemDefinition.SkipFirstChildReorderOnRestore` opts an item out
  of the `MoveToFirstChild` call, leaving it wherever the engine's own
  `MoveItemSmart`/attach naturally placed it instead of forcing front
  position. Any future item whose restoration leaves it "correctly stated
  but invisible" - especially one with siblings that are large background/
  overlay art (a `*Background`/`*Adjustment`-style class) - should compare
  a live sibling-order tree dump (vanilla vs. restored) before assuming
  it's another bounds/cursorId/frame case.

### client/Game/ItemNames.cs

Canonical list of the 40 carryable item names. This is a fixed part of the
game's own data (`g_vm->_itemIds`, loaded once at startup from
TEXT/ITEM_IDS). Obtained via the ScummVM debug console's "item" command (no
arguments) run in-game, which prints this exact list - no memory reading
needed to get these; they never change at runtime.

### client/Game/ItemPersistedState.cs

- **ItemStage**: Persisted directly on the object itself (see
  `GameActions.ReadItemPersistedState`/`WriteItemPersistedState`,
  `GameOffsets.GameObjectUnused4Offset`), not in any local C# collection.
  Being a real (if otherwise-unused) object field, this value survives
  detach/reattach, app restarts, and save/reload exactly like every other
  field on the object - eliminating the whole class of "lost track of an
  item mid-flight" bugs this project hit repeatedly while using local
  session-only sets for the same purpose.

  - `None` - untouched (state A), or CarryParrot before its one-time
    pickup. The zero value, so a never-touched item needs no explicit
    initialization at all.
  - `Hidden` - sitting in the hidden room, naturally picked up but not yet
    granted (state C). CheckFired is always true here - the only way an
    item reaches Hidden is via a natural pickup, which fires the check
    immediately.
  - `Mail` - sitting in the mail system, in transit. CheckFired
    distinguishes state B (granted before ever being found naturally -
    check not yet fired) from state C's mailed phase (was Hidden, now
    granted - check already fired back when it was first picked up).
  - `Inventory` - sitting in the player's inventory. CheckFired false means
    state D (granted and collected from mail, but never naturally found -
    eligible for restoration at its home RNV); CheckFired true is terminal -
    this app's job for this item is done.
  - `Restored` - temporarily pulled out of PulledFrom and placed at the
    item's real home parent, giving the player a genuine chance to pick it
    up for real (state D's restoration - see `MainForm.GameLogic.cs`'s
    `TryRestoreItemsAtHomeRnv`/`TryUnrestoreItemsLeavingRnv`). Reverts to
    PulledFrom if the player leaves without picking it up.

- **ItemPulledFrom**: Where a Restored item should go back to if the player
  leaves its home RNV without picking it up - "the item should always be put
  back to where it was pulled from in the first place" (hidden room,
  inventory, or mail system). Inventory and Mail are both reachable under
  the current design: state D's restoration (item already collected from
  mail) produces Inventory, and state B's item still sitting uncollected in
  mail also gets restored at its home RNV now (see `MainForm.GameLogic.cs`'s
  `TryRestoreItemsAtHomeRnv`), producing Mail. Hidden is implemented for
  completeness/future use only - nothing currently produces it.

- **ItemPersistedState struct**: Packed into a single 4-byte int (see
  `GameOffsets.GameObjectUnused4Offset`) - one byte per field, two bytes left
  spare for future use.

### client/Game/ItemTracking.cs

- **ItemTracking class**: Reuses the same conservative gate established for
  this project's earlier (now-superseded) hide-everything design: an item
  only enters the state machine at all if it has a confirmed, cross-checked
  AP item mapping (`LocationChecks.ItemToApItemName` / `TryGetApItemName`) -
  hiding something this app can never also reliably recognize as granted
  risks a permanent, unrecoverable softlock (the object would sit in the
  hidden room forever). That reasoning is unchanged by this design: it was
  always about which physical object corresponds to which AP grant, never
  about the decoy/spawning problem this design fixes. Excluded for that
  reason, same as before: the four CBridgePiece fuses
  (SeasonBridge/FanBridge/BeamBridge/ChickenBridge - ambiguous which is
  which, and "Yellow Fuse Removed" is a locked event item with no real grant
  at all), the four Phonograph Cylinders (four objects, one matching AP
  item, "Recorded Cylinder"), Eye1/Eye2 (identifiable, but which one is
  "Titania's Eye (Elevator)" vs "(Chevron)" is unconfirmed with no semantic
  hint to guess from), and Chicken/BeerGlass/Photograph (no items.py entry
  identified for any of them at all).
- **OneDirectionalItemNames**: Feathers ("Feather" in AP terms), Music
  System Key, and AuditoryCentre enter play via some other in-game mechanism
  (the parrot escaping, completing the Phonograph puzzle) rather than the
  player finding them in a home view - so they skip Part 1's original-parent
  capture and Part 2's RNV-driven restore/re-hide entirely, and are only
  ever detected via directly appearing in inventory (see
  `MainForm.GameLogic.cs`'s `DetectOneDirectionalPickups`). Music System Key
  has no confirmed items.py entry at all yet (see `LocationChecks.cs`'s own
  notes on the ambiguity) - it'll still be tracked and reach PendingMail if
  picked up before being granted, but `DeliverGrantsForTrackedItems` can
  never match it against a real AP grant until that mapping exists. A
  documented, accepted gap - not a bug, and not something to guess a mapping
  for here.
- **CarryParrotName**: CarryParrot never enters the state machine at all -
  Feathers (its child at game start) is the item actually tracked, not
  CarryParrot itself. This design keeps that exactly the same: detect its
  natural pickup and send its location check once, full stop - no capture,
  no hide, no restore, no mail. See `MainForm.GameLogic.cs`'s
  `DetectOneDirectionalPickups`.
- **IsFullStateMachineItem**: True for an item that goes through the FULL
  state machine (captured + hidden at game start, restored to its original
  parent while the player stands in its home view, re-hidden on leaving it)
  - every confirmed-mappable engine name except the one-directional
  exceptions above and CarryParrot.
- **ServerGrantedFuseNames**: Yellow Fuse and Red Fuse (engine names
  ChickenBridge and BeamBridge respectively - see `docs/Item Reference.md`'s
  per-fuse Misc Notes for the color mapping) are granted directly by the
  multiworld server rather than via a natural-pickup location check, unlike
  every other fuse/bridge piece above, which this app leaves completely
  untouched. They don't join the full state machine (no AP item mapping is
  registered for them in LocationChecks, and none should be - there's
  nothing to reconcile a pickup against). The only handling they need is a
  one-time attach-time check: if one is still sitting wherever the game
  originally put it - not in the player's inventory, not already in the mail
  system - it gets moved to the hidden room so the player can never find and
  pick it up naturally (see HideUngrantedFuses, called from
  `MainForm.Attach.cs`'s `AttemptAttach`).
- **HideUngrantedFuses**: One-time attach-time handling for Yellow Fuse and
  Red Fuse - both are granted directly by the multiworld server with no
  natural-pickup location check, so unlike a full-state-machine item there's
  no ongoing reconciliation loop for them and nothing to restore/re-hide
  later. If one is found still sitting at its default home position - not in
  the player's inventory and not already in the mail system, i.e. never
  granted or interacted with, this session or a past one - it's moved to the
  hidden room so the player can never pick it up naturally. Already being in
  inventory or mail means it's already been handed over some other way, so
  it's left alone. Safe to call repeatedly (an item already in the hidden
  room is neither in inventory nor mail, so this just moves it to the hidden
  room again, a no-op).

### client/Game/RoomFlags.cs

Bit-level encode/decode for the room-flags scheme actually used by
`CGameObject::_roomFlags`/`_destRoomFlags` on items - i.e. exactly what this
app reads/writes for mail routing. Directly transcribed from `CRoomFlags`'s
real source (`titanic/room_flags.h`/`.cpp`), not inferred.

IMPORTANT: this is a DIFFERENT encoding from `CChevCode` (the elevator's
physical chevron-dial interface, `titanic/game/chev_code.cpp`), despite
sharing bit positions for elevator/class/room. An earlier version of this
file (then called `ChevronCode.cs`, since deleted) implemented `CChevCode`'s
version instead of this one - wrong for our purposes. Confirmed by direct
comparison of both real source files:
- Elevator (bits 18-19), class (bits 16-17), and room (bits 1-7) use
  IDENTICAL encode/decode logic in both classes.
- Floor (bits 8-15) does NOT: `CChevCode`'s `SetChevFloorBits` secretly
  encodes `(floorNum + 4)`, while `CRoomFlags::setFloorNum` encodes the
  floor number directly, with no offset. The deleted `ChevronCode.cs`
  "corrected" `CChevCode`'s own internal asymmetry by subtracting 4 back
  off on decode - which made IT round-trip cleanly, but gave the WRONG
  floor number for real item `_roomFlags` values, since those are
  CRoomFlags-encoded and never had a +4 to begin with. Confirmed live: a
  Magazine's real `_roomFlags` targeted at the SGT Class Lobby SuccUBus
  decodes to floor=30 via the correct formula - the deleted file's formula
  said 26.

Bit layout (a 32-bit value, only the low 20 bits used):
- bit 0: set for one of the fixed named-room constants (see
  `ChevronCodes.cs`'s SuccUBus/Transport tables), clear for a dynamically
  computed per-stateroom code. Just a property of those specific constants -
  nothing here ever sets it, matching the real setters, none of which touch
  bit 0 either.
- bits 1-7: room number (0-127), via `(flags >> 1) & 0x7F`
- bits 8-15: floor, encoded as a base-plus-digit byte (see
  EncodeFloor/DecodeFloor) - not the raw floor number directly, and NOT
  CChevCode's +4-shifted version
- bits 16-17: passenger class (1=First, 2=Second, 3=Third), via
  `(flags >> 16) & 3`
- bits 18-19: elevator number (1-4), via `((flags >> 18) & 3) + 1`

Valid ranges per `CRoomFlags::getRoomArea` (what the game itself considers a
real, reachable location, not just anything the bit math happens to accept):
- Class 1 (First): floor 2-19, room 1-3, elevator 1-4 (any)
- Class 2 (Second): floor 20-27, room 1-3 if elevator odd / 1-4 if elevator
  even, elevator 1-4 (any)
- Class 3 (Third): floor 28-38, room 1-18, elevator must be ODD

(`CRoomFlags::whatPassengerClass(floorNum)` maps a bare floor number to its
class using these same floor boundaries - see `WhatPassengerClass`.)

`_data == 0x59706` is special-cased in the original as "the player's own 1st
class stateroom" (`CRoomFlags::isFirstClassSuite`) - not derivable from the
class ranges above, just a specific constant - see `FirstClassSuite`.

- **EncodeFloor**: `CRoomFlags::setFloorNum`, transcribed exactly - no +4
  offset. See the class-level note above for why that matters.
- **DecodeFloor**: `CRoomFlags::decodeFloorBits`, transcribed exactly.

### client/Game/RoomNames.cs

Display name, read directly from the game's own tree objects
(`CRoomItem+0x78`). IDs not present in the table are either unused or one of
the "NoName" internal containers (inventory, etc.), not real rooms.

### client/Game/SaveSeedGuard.cs

TEMPORARY guard against attaching to a save file that belongs to a different
Archipelago seed than the one currently connected - or that was played with
progress made while the client wasn't attached at all. Either case means
this app's own bookkeeping (`ItemPersistedState.cs`, received-items history,
etc.) has no relationship to what's actually in the save, so acting on it -
sending location checks for things that weren't actually earned in this
seed, granting/moving items - risks corrupting the save file.

Mechanism: a 64-bit tag derived from the server's `seed_name` (see
`ArchipelagoConnection.SeedName`) is written into the BeamBridge (Red Fuse)
item's `_unused3` field (`GameOffsets.GameObjectUnused3Offset`) - a real,
engine-unused object field, so it's persisted and read back the same way as
every other field on the object (survives save/reload, restart, etc. - the
same property already relied on for `_unused4`). The first time this app
ever attaches under a given seed to a save with no tag yet, it writes one -
but ONLY if the save is a genuinely fresh game (PET still off); a save with
existing progress and no tag means it was played without the client, which
is exactly the risk this guard exists to catch, so that case is blocked
rather than silently trusted and tagged.

This is a stopgap reusing a spare field for a single hardcoded item, not a
proper long-term save-tagging design - hence "temporary". See
`MainForm.GameLogic.cs`'s `EvaluateSaveSeedGuard` for how this is actually
invoked, and `MainForm.Tick.cs` for where a Blocked result short-circuits the
rest of a tick.

- **ComputeSeedTag**: FNV-1a, chosen only for being simple and
  dependency-free, not for any cryptographic property. A collision between
  two different seed_names is a non-issue here (worst case: this guard fails
  open on astronomically unlikely odds, not a security boundary).
- **FindBeamBridgeAddress**: Same cost as `GameState.FindAllCarryItems`
  itself, since that's all this does. Callers should only invoke this once
  per attach/connect (see `EvaluateSaveSeedGuard`), not every tick.

## client/UI

### client/UI/ConnectDialog.cs

Small modal for entering Archipelago server connection info. It's used by the normal (non-debug) UI's Connect button (see `MainForm.NormalUI.cs`). The debug UI keeps its own inline connection fields instead, in `MainForm.ArchipelagoTab.cs`, rather than using this dialog.

### client/UI/ItemFieldsForm.cs

Standalone live viewer for every known `CGameObject`/`CTreeItem` field offset on one item, opened from the Items tab's "View Fields" button. It is purely diagnostic — reads only, never writes.

The field list/kinds are hand-derived from the "Full CGameObject field layout" table in `GameOffsets.cs`, plus the `CTreeItem` header fields documented next to `GameOffsets.Parent`/`NextSibling`/`FirstChild`. This list is kept in sync manually if that table in `GameOffsets.cs` ever grows — there's no automatic linkage.

The offsets/order/notes in the `Fields` array match `GameOffsets.cs`'s own "Full CGameObject field layout" table exactly (see that table for how each offset was derived). `_priorSibling` (+0x18) has no `GameOffsets` constant of its own (it's only referenced in the `CTreeItem` layout comment next to `Parent`/`NextSibling`/`FirstChild`), but it's included here anyway since it's just as readable as its sibling fields.

The block of fields starting at +0x124 (`_unused5` through `_fullViewName`) are "CCarry candidates" — UNCONFIRMED offsets derived by hand from `carry.h`'s declaration order (CString = 0x28, Point packed to 4 bytes, standard 4-byte alignment), laid out after `CGameObject`'s own confirmed fields which end at `_visible` (+0x120). These should be cross-checked against a live CCarry-derived object (e.g. BrokenLiftbotHead) before being trusted for real reads/writes.

In the `FieldKind.CString` read case: the CString layout is confirmed in `GameOffsets.cs` (see `CStringCharPtrCtorFunc`'s doc comment) — `+0x0` is an int32 size, `+0x8` is a char* data pointer (which self-points into `+0x10` for short/SSO strings). Always dereferencing the data pointer works uniformly for both the SSO and heap-allocated cases, so no branching is needed based on string length.

### client/UI/MainForm.ArchipelagoTab.cs

`OnApStateChanged` handles `ArchipelagoConnection.StateChanged`, which can fire from a background thread — it always hops back to the UI thread before touching any control.

In the `Connected` case, several pieces of state are reset/reevaluated for the new server session:
- `_sentRoomVisitChecks` and `_sentPointOfInterestChecks` are cleared because this is a new server session, so previously-sent visit/point-of-interest checks need to be resent since the new session hasn't seen them.
- `_saveSeedGuardState` is reset to `Unverified` to re-check the attached save against this (possibly different) seed — see `SaveSeedGuard.cs`.
- `_lastItemsReceivedCount` is set to -1 to force a class-upgrade resync on the next tick, even if the count happens to match.
- If already standing in a room (`_currentRoomName is not null`), `TrySendRoomVisitCheck` is called directly because the player won't get a "room changed" tick to trigger it naturally.
- Similarly, if already standing at a specific spot (`_lastRoomNodeView is not null`), `TrySendPointOfInterestCheck` is called directly since there's no "rnv changed" tick to catch it.

`UpdateApTopStatus` refreshes the condensed AP status line shown in the top bar next to the Attach button (see `MainForm.cs`'s `BuildLayout`). Its state/color mirrors `_lblApStatus` exactly (set immediately in every branch of `OnApStateChanged`). The server/slot detail only appears once actually connected — while disconnected/connecting/failed, showing whatever happens to be typed into the form would be misleading (it may not reflect who/where the client is actually talking to, or there may be no connection at all), so the detail is left off in every state but Connected. This method is also called once at startup (from the constructor, after loading saved connection settings) so the label starts in its correct disconnected form.

`OnApMessageReceived` handles `ArchipelagoConnection.MessageReceived` (item sends/receives, hints, chat, join/leave, etc. — anything the AP client library surfaces via `session.MessageLog`). Like `OnApStateChanged`, it can fire from a background thread. When `_mem` isn't attached, the message is simply dropped since there's nothing to display it to.

`DisplayMessageSmart` (called from here) logs the message via the real `CPetConversations::displayMessage`, and additionally — if the Conversation tab isn't the one currently visible — shows it immediately via `DisplayPetMessageText` so the player doesn't miss it (see that method's own doc comment). It only needs `_currentInventoryRoom` (the PET control address), which is available from early in a session.

In `UpdatePendingChecksLabel`: the pending-checks queue is name-keyed (see `LocationCheckQueue.cs`) so the name itself is the display string and no id lookup is needed for that. If connected, the resolved id is additionally shown in parentheses for debugging; if not connected (or the id doesn't resolve yet), just the name is shown alone, rather than a misleading/stale id.

### client/UI/MainForm.Attach.cs

In `AttemptAttach`: `DoInstallHook()` auto-installs the PET talk command hook on attach. `DoInstallClassLockHook()` auto-installs the class upgrade lock on attach — it no-ops until `GameOffsets.SetPassengerClassFunc` is filled in.

The project chain (`gameManager`/`_currentProject`) is resolved synchronously right after attach, rather than waiting for the next tick, so the Items tab can refresh right away. If the game is still at a menu/loading screen this won't resolve yet — that's fine, normal tick polling picks it up once the game is ready, same as any other cold attach.

`EvaluateSaveSeedGuard(gameManager.Value)` is called here to cover the case where AP was already connected before the Attach button was pressed (i.e. `SeedName` is already known) — see `SaveSeedGuard.cs`. This call is usually a no-op since attach typically happens before connect, in which case `OnTick`'s own call to the same guard picks this up once a connection exists.

The Yellow Fuse / Red Fuse items have no natural-pickup location check — AP grants them directly — so they just need hiding once, up front, if attach finds either one still sitting untouched (see `ItemTracking.ServerGrantedFuseNames`). This hiding is skipped while the save/AP-seed guard hasn't verified this save — see `SaveSeedGuard.cs`.

In `DoDetach`: both `TextCommandHook.Uninstall` and `ClassUpgradeHook.Uninstall` restore original bytes before the process handle is lost.

In `ResetCachedState`: `_saveSeedGuardState` is reset to `Unverified` to re-check the newly (re)attached save — see `SaveSeedGuard.cs`. A comment previously noted that item-state tracking no longer needs anything cleared here at all — it now lives entirely in each item's own persisted state (see `ItemPersistedState.cs`, `GameActions.ReadItemPersistedState`/`WriteItemPersistedState`) and the object's own tool-placed sentinel (`GameActions.MarkItemAsToolPlaced`), so there's no local cache that can go stale across detach/reattach or a game restart.

### client/UI/MainForm.DebugTab.cs

`_btnDetach` (the Detach button) lives on the Debug tab now, not the top bar. There's very little reason for a player to ever detach mid-session (closing the app cleanly detaches on its own — see `OnFormClosed`), so it's been demoted to a Debug-tab tool rather than sharing the top bar's limited space with the game/AP connection info. Its behavior is unchanged from before — still wired to `DoDetach` in `MainForm.Attach.cs`.

`_chkEnforceSaveSeedGuard` — see `SaveSeedGuard.cs`. Default on. Unchecking it is a debug/testing escape hatch for working across save/seed mismatches deliberately (e.g. replaying the same save against several test seeds); it's not something a normal player session should ever need to touch.

`_chkTakeUngrantedItems` — see `MainForm.GameLogic.cs`'s `ReconcileTrackedItems`. Controls whether an ungranted item the player naturally picks up gets hidden away (pending the real AP grant, per this app's normal design) or is left alone in the player's inventory. Default on (normal behavior); unchecking is a debug/testing escape hatch, not something a normal player session should ever need to touch.

`_btnForceReconcileItems` is a manual, on-demand trigger for `MainForm.GameLogic.cs`'s `ReconcileTrackedItems` — it already runs automatically every `InventoryIntervalTicks` (~1s); this button just skips the wait during testing. `DoForceReconcileItems` (the handler) is safe to mash repeatedly, same as the automatic ~1s cadence it shares.

`DoForceTagSaveSeed` unconditionally overwrites BeamBridge's save/seed guard tag with the currently connected AP seed — see `SaveSeedGuard.cs`. Unlike `EvaluateSaveSeedGuard`'s own auto-tagging (which only ever binds an untagged, genuinely fresh save), this is a deliberate manual override: it writes over ANY existing tag, including one recorded for a different seed, and doesn't care whether the save has existing progress. It's meant for a player/tester who has independently confirmed this save and this seed really do belong together (e.g. recovering from a guard false-positive) — not something to reach for casually, since it's exactly the check this guard exists to enforce.

### client/UI/MainForm.GameLogic.cs

This file is the core of the app's item/save/seed reconciliation logic. It had by far the densest rationale comments in the UI layer.

### EvaluateSaveSeedGuard

Computes and caches (in `_saveSeedGuardState`) whether it's safe to act on the currently attached save under the currently connected AP seed — see `SaveSeedGuard.cs` for the full mechanism and rationale. It no-ops once already `Ok` or `Blocked` for this attach+connection (the underlying check involves a full carry-item tree walk, so it isn't meant to run every tick); it stays `Unverified` — and callers should treat that exactly like `Blocked`, i.e. do nothing — until both a resolved project and a connected AP seed are available.

If `BeamBridge`'s address can't be found: this is normally transient (menu/loading) and resolves within a tick or two. But if it NEVER resolves — e.g. this save's BeamBridge was already consumed/moved out of the carry-item tree by an earlier playthrough under a different seed — silently retrying forever would leave the guard stuck at `Unverified` (still treated as `Blocked` by callers) with no log or PET message ever printed, since `BlockSaveSeedGuard` would never be reached. So it's given a grace period (`_saveSeedGuardBeamBridgeMisses` vs `SaveSeedGuardBeamBridgeMissLimit`), then surfaced explicitly.

When the stored tag is 0 (untagged): it's only safe to bind to this seed if the save is genuinely fresh (PET still off) — an untagged save with existing progress means it was played without the client attached, which is exactly the corruption risk this guard exists to catch, so that case is blocked instead of silently trusted and tagged.

After a successful fresh-save tagging, the code resolves `_currentInventoryRoom` freshly rather than trusting the cached field, mirroring `BlockSaveSeedGuard`'s own fresh resolve: this runs before `UpdateInventory` has had a chance to set `_currentInventoryRoom` for the first time (see `MainForm.Tick.cs`'s ordering), so it can't be relied on here either.

### BlockSaveSeedGuard

Common landing spot for every way `EvaluateSaveSeedGuard` can end up `Blocked`. Beyond the usual `ShowActionResult` (Debug tab's Log/action label), it also bolds the same message into the normal UI's server log (see `MainForm.NormalUI.cs`'s `AppendServerLog`) — Debug mode's Log tab isn't even present in the normal layout, so without this a blocked guard would be effectively invisible to a real player — and, if the PET control address is known yet, prints a line into the in-game PET itself pointing at `!force_seed` (see `HandleForceSeedCommand`) as the way to bypass it.

It can't rely on `_currentInventoryRoom` here — that's only ever set by `UpdateInventory`, which runs from `OnTick` AFTER the guard-check gate that just returned early because the guard is now `Blocked`. So it resolves the inventory room fresh instead, since `_currentProject` is already guaranteed to be set (`EvaluateSaveSeedGuard` requires it before it ever gets here) — without this, a blocked guard would never be able to print into the PET at all, since `OnTick`'s own inventory resolution can never run once blocked.

### HandleForceSeedCommand

Handles the `!force_seed` command — a deliberate, local-only override for the save/AP-seed guard (see `SaveSeedGuard.cs`, `BlockSaveSeedGuard`). Reachable two ways, both funnelling here rather than ever reaching `ArchipelagoConnection.SendCommand`: typed into the in-game PET conversation box (captured by `TextCommandHook`, see `MainForm.Tick.cs`) or typed into the normal UI's chat box (see `MainForm.NormalUI.cs`'s `DoSendChatMessage`). Reuses the same unconditional tag-write as the Debug tab's "Force-Tag Save With Current Seed" button (`DoForceTagSaveSeed`) — writes over any existing tag and unconditionally marks the guard `Ok`.

### SyncPassengerClassFromItems

Applies the class upgrade implied by AP items received so far, if any (see `ClassUpgradeTracker`). It cheap-checks the received-item count first so it only does real work on ticks where something's actually new — safe to call unconditionally every tick otherwise. Requires the class-upgrade lock hook to already be blocking the vanilla DeskBot trigger, or this and vanilla gameplay will fight over the same field.

### TrySendRoomVisitCheck / TrySendPointOfInterestCheck

`TrySendRoomVisitCheck` sends the AP location check for a room's "Arrive for the First Time" location, if the room has a known mapping (see `LocationChecks.cs`); skips silently for an unmapped room. If not connected (or the send fails), `ArchipelagoConnection` queues it automatically and retries on next connect — so this always marks the room as handled either way, it just reports whether it went out immediately or got queued.

`TrySendPointOfInterestCheck` does the same for visiting an exact (Room, Node, View) point of interest (e.g. a Succ-U-Bus terminal), keyed on the full triple instead of the room alone, so a point of interest still gets its own check even if the room itself was already visited.

### UpdateInventory

The Conversations address is a static-offset computation (see `GameState.ResolveConversationsAddr`) — available immediately, unlike `TextCommandHook.ConversationsAddr`, which only updates once the player has typed something. That hook capture is still cross-checked against this elsewhere in `OnTick`, not displayed independently anymore.

### ReconcileTrackedItems — the item state machine (this was the file's biggest doc comment)

The whole item design was rebuilt from scratch after several prior generations proved more complicated than necessary and each had real live-tested bugs. Every fact this app needs to remember about an item is now persisted directly on the object itself (see `ItemPersistedState.cs`, `GameActions.ReadItemPersistedState`/`WriteItemPersistedState`) rather than in any local C# collection — eliminating the whole class of "lost track of an item across a reattach" bugs earlier versions hit repeatedly. Addresses this app ever needs (an item's true home parent) are resolved live, fresh, via `GameState.ResolveHomeParent` — never persisted, since raw addresses don't survive a game restart (see `ItemHomeLocations.cs`'s derivation notes).

Four states per full-state-machine item:

- **A. Not granted, not naturally picked up** — untouched (`ItemStage.None`). Stays exactly where the game/save naturally left it. This app never touches it at all in this state.
- **B. Granted, not yet naturally picked up** — the physical object is just sitting at its authored spot (state A) or in the hidden room (having been picked up before being granted — see C below), so it's handed to the player via mail right away (`ItemStage.Mail`, `CheckFired=false`) rather than making them go find/wait for it. Deliberately does NOT fire the pickup location check here, and does NOT stop there either — collecting it from mail does not count as a natural pickup (see D). If the player visits the item's home RNV before ever collecting it from mail, `TryRestoreItemsAtHomeRnv` pulls it out of the mail system the same way it does for D (`PulledFrom=Mail` instead of `Inventory`), so a player who never bothers with a mail terminal still gets a real shot at the natural pickup.
- **C. Not granted, naturally picked up** — the check fires immediately (this IS "naturally picked up"), then the item is hidden (`ItemStage.Hidden`, `CheckFired=true`) until AP grants it, then mailed exactly like B (`ItemStage.Mail`, `CheckFired=true` — no second check, already fired).
- **D. Granted, collected from the mail system before ever being naturally picked up** — explicit correction: this does NOT count as a natural pickup and must NOT fire the check. The item stays right where it is, in the player's own inventory (`ItemStage.Inventory`, `CheckFired=false`) — not yanked away anywhere. Only once the player visits its home RNV does it get temporarily pulled out and placed at its real home parent (`ItemStage.Restored`, `PulledFrom=Inventory` — see `TryRestoreItemsAtHomeRnv`), giving them a genuine chance at a real second natural pickup, which is the only thing allowed to fire the check. If they leave without picking it up (`TryUnrestoreItemsLeavingRnv`), it goes back to inventory, unchanged, to try again next visit.

C's item, once mailed, is collected from mail exactly like B's — both converge on the same `inInventory` branch, distinguished by `CheckFired` (C already fired its check before ever being hidden; B/D never did).

One-directional items (Feathers/Music System Key/AuditoryCentre) follow the exact same Hidden/Mail/Inventory mechanics as a normal natural pickup (state C) — they just never pass through state A/B, since the physical object doesn't exist as an independent carryable until the game's own special mechanism creates it (naturally handled: `ItemHomeLocations` has no entry for them, so they're simply never eligible for the "elsewhere"/restoration handling that states A/B/D depend on). CarryParrot is the one true special case — see `ItemTracking.CarryParrotName` — detected once, its check sent once, never captured/moved/tracked further.

`ReconcileTrackedItems` itself runs every `InventoryIntervalTicks` (see `MainForm.Tick.cs`) — a single `GameState.FindAllCarryItems` tree walk (this project's established "heavier, run less often" cadence) that handles every state transition described above in one pass, for every tracked item category (CarryParrot, the one-directional items, and every full-state-machine item).

Notes on individual branches inside the loop:
- Items with no confirmed AP mapping at all are never touched (see `ItemTracking.cs`).
- CarryParrot is never captured, moved, or tracked further (see `ItemTracking.CarryParrotName`); `SendItemPickupCheck` already no-ops silently for CarryParrot's unconfirmed location mapping.
- `persisted.Stage == ItemStage.Inventory && persisted.CheckFired` is terminal — this app's job for this item is done.
- In the `inInventory` branch: `persisted.Stage == ItemStage.Mail` means the item was collected from mail. `CheckFired` tells apart C's mail phase (already fired, this just completes it) from B (never fired, collecting it does NOT count as a natural pickup — state D begins, staying right here in inventory).
- `persisted.Stage == ItemStage.Restored` completing is the genuine second natural pickup — the only thing state D's restoration was ever for. `_visible` was forced false while `Restored` (see `TryRestoreItemsAtHomeRnv`) and is left to the game to restore now that it's genuinely in the player's hands (there's deliberately no manual `SetItemVisible(..., true)` call here — "since this is a natural pickup, let the game handle setting the `_visible` field" — same as `RevertRestoration` does for the "left without picking it up" case).
- `persisted.Stage == ItemStage.Inventory` (without CheckFired terminal case above) is already-known state D, waiting on a future RNV visit — nothing new to do.
- Stage `None` or `Hidden` found in inventory is defensive — `Hidden` shouldn't really co-occur with "found in inventory", since the hidden-room branch always moves a granted Hidden item to Mail first, never straight to inventory — and represents a genuine natural pickup straight from wherever it was sitting (state C's start).
- When `granted || !_chkTakeUngrantedItems.Checked`: the item is already exactly where it should be — AP granted this one right as the player found it naturally. This is also taken when the "take ungranted items away" debug checkbox is off: leave the item right where it is instead of hiding it pending the real grant.
- In the `inHiddenRoom` branch: `Stage == Hidden && granted` means the item is delivered to mail now, but its check already fired back when it was first naturally picked up — the player still has to actually collect it from mail (handled in the `inInventory` branch) before this app's job here is done. Otherwise: still waiting, or hidden for a reason unrelated to this app (`Stage == None`).
- `inMail` alone means the item is in transit (state B or C) — nothing to do until the player collects it themselves (handled in `inInventory`).
- "Elsewhere" (not in inventory, hidden room, or mail) is either genuinely untouched (state A), or currently Restored and sitting un-picked-up at its home parent (nothing to do here either way — `TryUnrestoreItemsLeavingRnv` owns that case). Within that, `Stage == None && granted` is state B: granted before the player ever found it naturally. It's handed over via mail now rather than making the player travel there first — but, same as the hidden-room branch, does NOT send the pickup check yet. Only actually collecting it from mail does that (`inInventory` branch).
- After the loop, `_lastMailItems` is nulled out if anything changed, to force the Mail tab to refresh too.

### FindByName

Small helper shared by `TryRestoreItemsAtHomeRnv`/`TryUnrestoreItemsLeavingRnv`, which only ever need one or two specific items out of a full tree walk, to find a single named item within an already-fetched `FindAllCarryItems` result.

### ScheduleDirtyReassert

Arms a second `GameActions.MarkAllDirty()` call a few ticks in the future (see `MainForm.cs`'s `_pendingDirtyReassertTick` doc comment) — called after every restore/un-restore move alongside whatever immediate dirty call already happens as a side effect of the move itself (`MoveItemSmart`'s `RefreshPetControl`). The immediate call alone is confirmed live to sometimes not be enough to make a restored background-rendered item visually reappear when the player skipped the transitional video (shift-click) to reach the RNV — only a later, unrelated redraw (e.g. an NPC's own idle animation) reliably fixes it, which points at the dirty mark being discarded or the paint pass not being pumped again, not at wrong item state. Re-issuing the call once the skip-video transition's own cleanup has had time to settle tests both theories cheaply. It overwrites any earlier pending reassert rather than stacking multiple — one re-issue per burst of moves is enough, and `OnTick` only ever needs to fire it once.

### TryRestoreItemsAtHomeRnv

Runs on every RNV change (see `MainForm.Tick.cs`), for whichever full-state-machine items call the just-arrived-at RNV home (see `ItemHomeLocations.cs`). Handles two distinct things:

1. **State D restoration**: an item sitting in the player's inventory with its check not yet fired (granted before ever being found naturally) gets temporarily pulled out and placed at its real home parent — resolved live via `GameState.ResolveHomeParent`, not any captured/cached address — giving the player a genuine chance at the real natural pickup that's the only thing allowed to fire its check. Expected to actually happen for a granted-before-found item, not a rare recovery path.

   Same treatment applies to state B's item while it's still sitting uncollected in the mail system (`Stage.Mail`, `CheckFired=false`) — without this, a player who never visits a mail terminal for a granted-but-never-found item would never get a shot at the real natural pickup, even after walking right up to its home RNV. `PulledFrom` is set to `Mail` instead of `Inventory` in this case, so `TryUnrestoreItemsLeavingRnv` puts it back where it actually came from (see `RevertToMail`) rather than incorrectly dropping it into the player's inventory. A Mail-stage item with `CheckFired=true` (state C's mail phase, already naturally picked up once before) is deliberately left alone here — its check already fired, so it only needs to be collected from mail, not restored anywhere.

2. **Stray-item self-heal**: should be a no-op almost always — an untouched item (`ItemStage.None`) that isn't exactly at its resolved home parent despite this app never having moved it (e.g. a leftover effect of an earlier session under this project's now-abandoned hide-everything design). Also resolved live via `GameState.ResolveHomeParent` — no captured address needed here either.

Inside the restoration branch: it's confirmed live that a fresh-save BrokenLiftbotHead is its parent's first child, while a generic re-attach appends it as the last — `MoveToFirstChild` puts it back to match, on top of the frame/bounds fixes that follow. Re-attach alone doesn't recompute `_bounds`/`_cursorId`, and doesn't reseat `_surface` onto the right frame either — some items (BrokenLiftbotHead) lose their authored click region the first time they're picked up and never get it back from a generic tree move (see `ItemHomeLocations.RestoreFieldOverrides`). Bounds are set first so `loadFrame`'s own internal dirty-rect registration (see `GameActions.CallLoadFrame`) uses the corrected rect, not whatever was there before.

### TryUnrestoreItemsLeavingRnv

Runs on every RNV change, for the RNV just being LEFT (the counterpart to `TryRestoreItemsAtHomeRnv`, which runs for the RNV just being ARRIVED at — see `MainForm.Tick.cs` for how both get the right RNV). Reverts any item still sitting Restored-but-unpicked at this RNV back to wherever it was pulled from (`PulledFrom`) — "the item should always be put back to where it was pulled from in the first place", generalized across all three possible origins even though only `Inventory` is actually reachable under this design's current triggers (see `ItemPulledFrom`'s own doc comment).

### RevertRestoration

The actual move-back for `TryUnrestoreItemsLeavingRnv`, per `ItemPulledFrom`. Only the `Inventory` case is reachable today (state D's restoration is the only thing that ever sets `PulledFrom`) — `Hidden`/`Mail` are implemented for completeness per the stated general principle, not because anything currently produces them. `_visible` is forced false while Restored (see `TryRestoreItemsAtHomeRnv`) and is set back to its normal default the moment the item is no longer sitting out at its home parent, regardless of which container it went back to.

### SendItemPickupCheck

Sends the AP location check for physically finding a tracked item (`LocationChecks.TryGetItemPickupLocationName`), if one is mapped — skips silently otherwise (e.g. CarryParrot, or Photograph/Chicken/BeerGlass, which have no mapping at all — see `LocationChecks.cs`).

### DeliverToMail

Delivers a tracked item via the mail system — real destination set via `SetItemMailDestination` (current room's station, falling back to EmbLobby, same pattern as `MainForm.ItemsTab.cs`'s `MoveItemAndReport`), then `MarkItemAsToolPlaced`, in that order (mark-after-destination is required — see `MarkItemAsToolPlaced`'s own doc comment). This `ToolPlacedSentinel` marking is purely for the Debug/Items/Mail tabs' own pre-existing manual-tooling purposes (e.g. `DeliverQueuedMailAtStation`'s per-station retargeting) — it is NOT this app's own state tracking anymore (see `ItemPersistedState.cs`), so there's no risk of the two conflicting the way they used to.

**KNOWN GAP (Magazine specifically, not solved here)**: completing the 3rd Class Room puzzle triggers the game's own script to move the Magazine object into the mail system automatically, regardless of its current state. If Magazine has already been delivered and is sitting in the player's inventory, that puzzle completion would suddenly and confusingly pull it back into mail — this app has no way to detect or block that script (separate, not-yet-started RE work), so this is left as an accepted limitation for this version.

### client/UI/MainForm.ItemsTab.cs

`DoRefreshAllItems` preserves selection and scroll position across the rebuild — item addresses are stable across a move (only the parent pointer changes), so matching by address (the row's `Tag`) works even when the refresh was triggered by moving the selected item itself. The Location subitem's own `Tag` holds the parent address, so `DoCopyParentAddress` can retrieve it from a selected row independently of the item's own address (stored on the row's `Tag`).

`DoExportParentSnapshot` writes the current full item/parent listing to a timestamped CSV under `%AppData%\StarshipTitanicAp\ParentSnapshots`, alongside each address's own delta from `ModuleBase`.

**Purpose of the snapshot feature**: on a genuinely fresh save (before the player has moved anything), every item is still parented to its true default container. Capturing that snapshot's raw addresses is NOT the same as finding a "stable offset" for them the way `GameOffsets`' Step1/Step2/GameManager chain is stable — that chain is stable because `CGameManager` itself sits behind two genuinely static module-relative pointers. Every item's parent here (a `CRoomItem` or one of the three `NoName` containers) is reached only by walking live tree pointers from `_project` (itself a heap value read off `CGameManager+0xE718`) — there's no known static pointer INTO any individual room, so nothing here is expected to reproduce at the same address, or even the same `ModuleBase` delta, on a different launch.

What the snapshot is actually for is testing that expectation empirically, the same "never trust it until it's been checked twice" way every other offset in this project was confirmed: export one snapshot per fresh-save relaunch (2-3 is enough), then diff them. If a given item's `ParentAddrDelta` comes back IDENTICAL across every relaunch, that's a real, promotable candidate for a hardcoded static offset in `GameOffsets.cs`. If it doesn't — which, given everything already confirmed about how `_project` is reached, is the expected outcome — that's the confirmation that a parent's NAME (already captured here, and already how `ItemHomeLocations.cs` and `LocationLabel` identify a container) is the only thing worth treating as "stable" for these, not its address.

`WalkToRoom` walks `Parent` repeatedly starting from an item's immediate parent until it reaches an object whose resolved class name is exactly "CRoomItem" — unlike the old fixed 3-hop (View -> Node -> Room) walk this replaced, chain depth genuinely varies per item (e.g. Phonograph Cylinder 1/2/3 nest an extra `CMusicRoomPhonograph`/`CRestaurantPhonograph` hop, and possibly a `CNodeItem`/`CViewItem` pair, before ever reaching their room — see the snapshot data this replaced, where the fixed-depth version stopped one hop short at a `CViewItem` for exactly these items).

It stops — returning whatever room (if any) was found so far, plus a reason — on: reaching `CRoomItem` (success); a null/zero `Parent` pointer (ran off the top of the tree without finding a room — shouldn't normally happen for a real carryable item, worth investigating if it does); a revisited address (cycle guard — tree traversal elsewhere in this file never expects cycles, but nothing stops a misread pointer from producing one); or a depth cap (generous — deep enough for every chain seen so far plus real headroom, without letting a bad read spin unbounded).

Inside the per-hop loop, both name sources are reported, not just the scan-based one — the scan (`TryReadNameSafe`) is known to usually miss `CViewItem` names specifically, while the direct `+0x40` read is confirmed 100% reliable for `CRoomItem` across every item captured so far but was never checked at this per-hop level before. Surfacing both here in one export — rather than a separate manual per-address check — is how the View-ordinal-position hypothesis actually gets confirmed or falsified, the same way Room already was.

`DescribeNameAt0x40` is a best-effort read of the hypothesized inline `_name` text at `object+0x40` (struct-start-relative `+0x10` within a CString field starting at `+0x30`, matching `NamedItemNameOffset`) — the exact spot live-confirmed for `CViewItem`. It reads 24 raw bytes so longer names aren't truncated to 8, and stops at the first non-printable byte (SSO buffers aren't null-padded beyond the string's own length in every case, so a hard null-terminator search alone isn't reliable here).

A garbled/non-ASCII result at this exact offset is itself useful data, not just noise — it usually means either (a) this level doesn't share `CViewItem`'s inline-text layout, or (b) the real name here is long enough to have spilled onto the heap, in which case what's actually sitting at `+0x40` is unrelated bytes rather than a text buffer at all (the heap-allocated case stores its own data pointer at `+0x38` instead — cross-check against that offset's value directly in Cheat Engine/x64dbg if this comes back empty).

`LocationLabel`: the cached `CPetControl`/`CMailMan` addresses get their known names, an actual named container (a room/node) shows its name, a resolved class name (see `GameState.TryGetClassName`) is the next fallback, and only if even that fails does it show the raw address — which is now also always shown unconditionally in its own "Parent Address" column, so this final fallback is more of a last resort than a primary display.

`DoMoveSelectedToHiddenRoom` handles the HiddenRoom case separately from `MoveItemAndReport` — `GameActions.MoveItemToHiddenRoom` resolves the PET and hidden room internally via `petMoveToHiddenRoom()`, so there's no room address to pass, and none of `MoveItemSmart`'s inventory-vs-mail refresh logic applies here. When there's no PET to refresh yet, it falls back to a raw move only. The mail-sentinel-clearing logic afterward mirrors `MoveItemAndReport`'s non-mail branch — the item may be leaving `CMailMan` via this move.

`MoveItemAndReport` is the shared move+refresh+report logic for all three "move item" entry points (to inventory, to mail, to a custom address — the last of which lives on the Debug tab, see `MainForm.DebugTab.cs`'s `DoMoveToCustomAddress`). `MoveItemSmart` refreshes whichever side of the move — source or destination — is actually the inventory, so this stays correct whether the item is entering, leaving, or neither. When the destination is the mail system, it also pairs the move with a real `SetItemMailDestination()` call and marks the item as tool-placed via the `_destRoomFlags` sentinel (see `GameActions.MarkItemAsToolPlaced`) — part of the item's own serialized state, so it survives detach/reattach and game restarts/save-loads with no external bookkeeping needed. On success, it re-runs the full item list (`DoRefreshAllItems` already preserves selection/scroll position) so the Items tab reflects the move immediately instead of waiting for the next manual refresh.

A plain tree move into `CMailMan` leaves `_roomFlags`/`_isPendingMail` as garbage — that's what produced the stuck, non-interactive "In Tray" capsule. This is why the destination-mail branch pairs the move with a real destination: the current room's station if it has one, else EmbLobby as a safe fallback. The tool-placed mark is applied AFTER the real destination is set — `MarkItemAsToolPlaced` is only safe once `_roomFlags` holds a real value, since that's the point `findMailByFlags` stops consulting `_destRoomFlags`.

In the non-mail-destination branch: if the item left the mail system (or never entered it) via this move, the sentinel is cleared only if the app itself actually set it — never touching `_destRoomFlags` on an item it didn't mark, so an organically-mailed item's real pending-destination value is left alone.

### client/UI/MainForm.LiveTab.cs

`FormatCurrentLocation` formats a decoded `roomFlags` value for the Live tab: a static named room (`RoomFlags.IsNamedRoom`) shows its name via `ChevronCodes`; anything else is a dynamic per-stateroom code, shown as its elevator/floor/room breakdown via `RoomFlags.Decode`.

### client/UI/MainForm.LogTab.cs

`_txtLog` accumulates the session's history (action results, captured PET commands, AP connection state changes, AP messages) — see `MainForm.cs`'s `AppendLog`, the single place that writes into `_txtLog`.

### client/UI/MainForm.PetTab.cs

The hook install/uninstall and class-lock install/uninstall controls (`_btnInstallHook`, `_btnUninstallHook`, `_btnInstallClassLockHook`, `_btnUninstallClassLockHook`) are also used on the PET tab, but are declared alongside the Debug tab controls since that's where their own dedicated Debug-tab actions live — see `MainForm.DebugTab.cs`.

In `DoSetClass`: combo items are 1-indexed in display order, hence `_cmbClass.SelectedIndex + 1`.

In `DoDisplayMessageText`: `DisplayMessageSmart` logs the message via the real `CPetConversations::displayMessage` AND, if the Conversation tab isn't the one currently visible, also shows it immediately via `DisplayPetMessageText` (see its own doc comment). It needs only `_currentInventoryRoom` (the PET control address), which is available from early in a session.

### client/UI/MainForm.MailTab.cs

`DeliverQueuedMailAtStation` is called on every real room change (not node/view movement within a room). If the new room has a SuccUBus station, every mail item this app itself placed into the mail system — identified live by the `ToolPlacedSentinel` in `_destRoomFlags` (see `GameActions.MarkItemAsToolPlaced`), regardless of its current delivered/queued status — gets its `_roomFlags` retargeted to this room's chevron code. `SetItemMailDestination` never touches `_destRoomFlags`, so the sentinel survives the retarget and the item stays recognized as ours. Items placed there by normal gameplay (the real SuccUBus flow) are never touched, since there's no way to tell the game's own routing was "wrong" — only the app's own placements are its to move.

In `DoSetMailDestToCurrentRoom`: the `MarkItemAsToolPlaced` call after setting the destination is an explicit tool write, not gameplay. `_lastMailItems` is nulled to force a refresh next tick rather than waiting up to a second.

### client/UI/MainForm.NormalUI.cs

The information area (`_lblInfoRoom`, `_lblInfoMail`, `_lblInfoChecks`, `_lblInfoStations`), shown above the server log, is fed by `UpdateInfoRoom`/`UpdateInfoMailCount`/`UpdateInfoChecks`, which are in turn called from `OnTick`/`UpdateMail`/`OnApStateChanged` respectively.

`_readyToPlayLogged` tracks whether "Ready to Play!" has already been logged for the current attach+connect pairing, so it's announced once per time both become simultaneously true rather than on every tick/state change while they stay true. It's reset to false whenever either attach or the AP connection drops, so it can fire again once both recover.

In `DoSendChatMessage`: `!force_seed` is a local client override (see `MainForm.GameLogic.cs`'s `HandleForceSeedCommand`) — deliberately intercepted here, before `SendCommand`, so it never reaches the AP server as chat.

`AppendServerLog` is safe to call unconditionally regardless of which UI is showing (the control just won't be visible in Debug mode), and safe to call from a background thread, same as `AppendLog`.

`CheckReadyToPlay` is safe/cheap to call from any spot where either condition (attach or AP connection) just became true; it's a no-op if it already fired for this pairing or either condition isn't met yet.

`UpdateInfoRoom` gets its readable room name via `LocationChecks.GetReadableRoomName`, called from `MainForm.Tick.cs`'s `OnTick` whenever the room actually changes.

`UpdateInfoMailCount` is called from `MainForm.MailTab.cs`'s `UpdateMail` every time it re-reads the mail system (`MailIntervalTicks`).

`UpdateInfoChecks` pulls from the connected session's own location data (see `ArchipelagoConnection.GetLocationCheckSummary`) — shows "-/-" while not connected, same as the labels' initial text. It's called every tick (cheap — small collection lookups only) and right after an AP connect/disconnect for an immediate update.

### client/UI/MainForm.Tick.cs

This is the main per-tick loop (`OnTick`). Original section-header comments explained the cadence and ordering rationale for each block:

- **Delayed dirty re-assert** (see `ScheduleDirtyReassert`): cheap, checked every tick regardless of the save/AP-seed guard below, since it's a pure local redraw nudge with nothing AP-facing about it and no reason to wait on that gate.

- **PET command hook**: polled every tick, cheap (2 small reads). Deliberately runs BEFORE the save/AP-seed guard gate below, not after — `!force_seed` (see `HandleForceSeedCommand`) is the player's only escape hatch out of a Blocked guard, so it and every other typed command must still be captured while blocked. Polling this only after the gate would mean a blocked guard could never be unblocked from in-game at all, since `OnTick` would already have returned before ever reaching this block.

  `PollCommand` also opportunistically captures `_conversations`' address as a side effect (see `TextCommandHook.ConversationsAddr`). The Addresses tab row itself is now populated independently, via the static-offset computation in `UpdateInventory` — the `_conversationsAddrShown` block is purely a cross-check once the hook capture becomes available too, since the two were derived independently and agreement between them is a real confirmation, not decoration.

  When the captured command is `force_seed`: it's handled entirely locally (see `MainForm.GameLogic.cs`'s `HandleForceSeedCommand`). Deliberately never reaches `_apConnection.SendCommand`; this is a client-side override, not something the AP server needs to know about.

  For a normal command while connected: `SendCommand` hands the send off to a background task (see `ArchipelagoConnection.TrySendAsync`) — this confirms it was dispatched, not that the server has it yet. A genuinely dead connection surfaces via the AP status label going to Disconnected, not here.

- **Save/AP-seed guard**: must run before any of the reads/writes below that act on the save (item state, location checks, item grants, mail delivery, etc.) — see `SaveSeedGuard.cs`. `Unverified` is deliberately treated the same as `Blocked` here: with no AP connection yet there's nothing to compare the save against, so there's no basis for letting AP-facing logic run either.

- Past the guard: either it's confirmed `Ok`, or enforcement is deliberately off (debug escape hatch). Either way it's now safe to replay any pending checks left over from disk — see `ArchipelagoConnection.NotifyGameVerifiedForSeed`'s doc comment for why this can't just happen automatically on AP login. Idempotent per session, so calling it every tick here is cheap.

- **Room / node / view** updates run every tick. Room-visit checks and mail delivery only fire on an actual room change (not just node/view movement within the same room), and only once per arrival. Point-of-interest checks (e.g. a Succ-U-Bus terminal) are keyed on the exact (Room, Node, View) triple, not just the room — so `TrySendPointOfInterestCheck` fires on every rnv change, not only a room change, since the point of interest might be node/view movement within an already-visited room.

  State D restoration/un-restoration (see `MainForm.GameLogic.cs`'s `TryRestoreItemsAtHomeRnv`/`TryUnrestoreItemsLeavingRnv`) is keyed on the full triple, not just the room, since a home view is a specific Node/View within a room, not the whole room. Un-restore runs first, for the RNV just left, so an item pulled from inventory at one home RNV doesn't linger visible if the player leaves before the arrival-side restore call (for a different item's home RNV) would otherwise touch it.

- **Current location** (roomFlags-derived): every tick, cheap single read once the PET control address is known. See `GameOffsets.PetControlCurrentRoomFlags` for how this offset was confirmed — a static room (`RoomFlags.IsNamedRoom`) shows its name via `ChevronCodes`; anything else is a dynamic per-stateroom code, decoded via `RoomFlags.Decode`.

- **Passenger class**: every tick, cheap single read. **Class upgrade from received AP items**: cheap count check first, only does real work when something's actually changed.

- **Pending-checks list**: every tick, cheap (small in-memory collection, no game-memory reads at all). Needed because `SendLocationCheck`'s actual queue removal happens on a background task (see `ArchipelagoConnection.TrySendAsync`) — the synchronous refresh right after handing off a send happens BEFORE that removal completes, so without this the label/list goes stale showing "still pending" long after a check has actually gone through.

- **Information area's "Checks: x/y" / "Visited Succ-U-Bus Stations (x/y)" lines**: every tick, cheap (small collection lookups against the session's own already-loaded location data). See `MainForm.NormalUI.cs`'s `UpdateInfoChecks`.

- **Inventory / Mail**: only every N ticks (heavier tree walks). The inventory-interval block does item pickup detection, hidden-awaiting-grant delivery, and eager grant-before-pickup delivery, all in one tree walk — see `MainForm.GameLogic.cs`'s `ReconcileTrackedItems`.

- **Class upgrade lock hook**: polled every tick, cheap (1 small read). A blocked DeskBot upgrade attempt still needs to be reported to AP as its own location check — `SendLocationCheck` queues automatically if not connected.

  The `SpecialPassengerClassValues.BridgeAccessClassValue` branch: this is not a DeskBot dialogue choice at all — it's the game's OWN internal call, made once as part of completing Titania's repair, granting Bridge access by setting the passenger class to this otherwise-unused value (confirmed live: the log line this produced — "unrecognized class 4" — appeared at the exact moment the last Titania repair piece was placed, well before this handling existed to make sense of it). Since `ClassUpgradeHook` intercepts `setPassengerClass()` unconditionally regardless of caller, leaving this in the "unrecognized -> blocked" bucket would have silently prevented the game's own Bridge-access grant from ever actually applying (the real function body, and the class value it writes, never runs while the hook is installed) — so this needs to explicitly apply it itself, the same way the None -> Third bypass does, rather than just reporting it.

  The AP location check for this ("Titania's Room - Repair Titania") is sent the same way the normal DeskBot upgrades are — a direct, undeduped `SendLocationCheck` — trusting the AP server's own idempotency rather than tracking "already sent" locally, since this attempt can only genuinely happen once per playthrough anyway.

  The `PassengerClass.Third` branch (gated on `_chkAllowInitialUpgrade.Checked`): the None -> Third transition has no AP location/item yet (see `_chkAllowInitialUpgrade`'s doc comment on the Debug tab) — blocking it unconditionally like every other attempt would strand the player in the Embarkation Lobby with no way to ever leave. It's applied directly via the same path AP-granted upgrades use, bypassing AP entirely since there's nothing for it to grant yet. Once the apworld adds a real item for this tier, this path should be retired in favor of that.

### client/UI/MainForm.cs

The class doc comment described the file-splitting scheme for the whole `MainForm` type: it's split across multiple partial-class files, one per tab (plus this core file for shared plumbing) — see `MainForm.*.cs`. Each tab file owns its own control fields, its `BuildXTab()` method, and its button-click handlers; this file owns the window shell (top bar, tab strip assembly), cross-cutting game-state fields used by more than one tab, and small generic helpers (`SectionLabel`, `ShowActionResult`, etc.) shared by every tab file.

Field-level notes that were removed:
- `SaveSeedGuardBeamBridgeMissLimit` is the grace period (in ticks) `EvaluateSaveSeedGuard` gives BeamBridge resolution before giving up and surfacing a block message instead of retrying silently forever — see `_saveSeedGuardBeamBridgeMisses`.
- `_sentRoomVisitChecks`: rooms whose "Arrive for the First Time" check has already been sent this run — purely to avoid spamming redundant sends on repeat visits; the server itself is fine with duplicates, so this is just tidiness.
- `_sentPointOfInterestChecks`: exact (Room, Node, View) spots whose point-of-interest check (see `LocationChecks.TryGetPointOfInterestLocationName`) has already been sent this run — same "avoid spamming redundant sends" purpose as `_sentRoomVisitChecks`, just keyed on the full triple instead of the room alone, since a point of interest can be one specific spot within an otherwise-already-visited room.
- `_lastItemsReceivedCount`: last-seen length of `_apConnection.GetReceivedItemNames()`, so the class-upgrade sync only does real work when something's actually changed instead of recomputing on every tick.
- `_currentGameManager`/`_currentProject`/`_currentInventoryRoom`/`_currentMailManRoom`: cached "current" values, refreshed every tick, used by other tabs so the user isn't stuck re-running lookups.
- A comment previously noted that per-item state for `MainForm.GameLogic.cs`'s `ReconcileTrackedItems`/`TryRestoreItemsAtHomeRnv`/`TryUnrestoreItemsLeavingRnv` is no longer tracked here at all — it's persisted directly on each item's own object (see `ItemPersistedState.cs`, `GameActions.ReadItemPersistedState`/`WriteItemPersistedState`), which survives reattach/restart/save-reload on its own with no local bookkeeping needed.
- `_pendingDirtyReassertTick`: tick count at which to re-issue `GameActions.MarkAllDirty()` after a restore/un-restore touches an item (see `TryRestoreItemsAtHomeRnv`/`TryUnrestoreItemsLeavingRnv`'s `ScheduleDirtyReassert` calls) — null when nothing's pending. It exists because `MarkAllDirty` called at the moment of restore is already confirmed to fire, yet a restored item can still fail to visually reappear until some unrelated later redraw (e.g. an NPC's own idle animation) happens to paint over it — suggesting either the dirty mark is being discarded by the shift-click "skip video" transition's own cleanup right after it's set, or the paint pass just isn't pumped again until something else drives it. A second `MarkAllDirty` call a few ticks later, once that transition has settled, tests both theories cheaply: it either survives the wipe or just supplies the missing pump. See `MainForm.Tick.cs` for where this is consumed.
- `DirtyReassertDelayTicks`: how many ticks (see `RoomNodeViewIntervalMs`) to wait before the re-assert above — long enough for the skip-video transition's own internal cleanup to finish first.
- `_saveSeedGuardState`: computed lazily once both the game and an AP connection are available, then cached here so the check (a full carry-item tree walk) doesn't repeat every tick. Reset to `Unverified` on detach/reattach (`ResetCachedState`) and on every fresh AP connection (`OnApStateChanged`), so a save swap or a reconnect to a different seed is always re-checked.
- `_saveSeedGuardBeamBridgeMisses`: consecutive ticks `EvaluateSaveSeedGuard` has had a resolved game/project/AP connection but still couldn't locate the BeamBridge item to check its guard tag against. A save briefly failing to resolve this while loading is normal and retried silently, but a save that NEVER resolves it (e.g. BeamBridge was already consumed/moved out of the carry-item tree by a prior playthrough) would otherwise leave the guard stuck at `Unverified` forever — blocking AP actions with zero log/PET feedback, since `BlockSaveSeedGuard` is never reached.
- The top bar's Detach button was intentionally removed from here — see `MainForm.DebugTab.cs`'s doc comment on `_btnDetach` for why it moved.
- `_isDebug`: whether the full tab-based Debug UI (every tab, including tools that can be used to cheat in a real multiworld seed) should be shown instead of the normal player-facing UI — see `MainForm.NormalUI.cs`. Set once from the `--debug` command-line flag (`Program.cs`) and never changed afterward.

In the constructor: `UpdateApTopStatus()` is called early to reflect the just-loaded server/slot in the top bar even before connecting. The final `AttemptAttach()` call silently tries to attach to an already-running game on launch — if scummvm isn't running yet, `AttemptAttach` just leaves `_lblStatus` showing its normal "could not find/attach" text; there's no dialog or other interruption either way, so this is safe to always attempt.

In `BuildLayout`'s debug top panel: the info panel holds connection info that used to live in the Detach button's old spot — game-attach status (PID + module base, via `_lblStatus`) and Archipelago connection status (state + server/slot, via `_lblApTopStatus` — see `MainForm.ArchipelagoTab.cs`'s `UpdateApTopStatus`).

`ShowCapturedCommand` surfaces a PET talk command captured by the hook in the same top-level feedback line as `ShowActionResult`, rather than a separate log list — there's no pass/fail here, just "here's what came through", so it gets its own neutral-colored format.

`AppendLog` is the one place session history accumulates — `ShowActionResult`, `ShowCapturedCommand`, and the AP connection/message handlers all funnel through here so the Log tab captures everything they already surface elsewhere. Safe to call from a background thread. It caps growth for long sessions, trimming the oldest half once it gets large.
