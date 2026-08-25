# Starship Titanic — Archipelago World (first pass)

Generated from a walkthrough-derived puzzle-logic model. This is **untested
against a live Archipelago core** — I don't have network/AP-source access in
the environment that produced this, so treat it as a structurally-complete
draft, not a validated release.

## What's here
- `items.py` — 40 real items (19 key items, 1 progressive item with 2
  copies, 11 Titania parts, 1 useful fuse, 5 filler, 2 traps) + 5 event
  items.
- `locations.py` — 59 real checks (including one "Visited" check per
  region) + 5 event checks. Real locations now outnumber
  real items on purpose — the surplus gets padded with filler via
  `get_filler_item_name`, which is normal AP behavior, not a bug.
- `regions.py` — 20 regions (including the "Top of the Well" hub, added
  after it turned out to be missing from the first draft), connected per
  the class-upgrade/item gates in the logic model.
- `rules.py` — every access rule, each commented with the walkthrough
  section it encodes.
- `options.py` — intentionally empty for now (see comment inside).

## Before you trust this in a real generation
1. **Check the AP API version.** `Region.connect()`, `place_locked_item()`,
   `PerGameCommonOptions`, and `worlds.generic.Rules.set_rule` have all
   changed shape across Archipelago releases. Diff against another working
   apworld from the same core version you're running.
2. **Run `python Generate.py` with only this world enabled** and read the
   spoiler log's playthrough — that's the fastest way to catch a rule typo
   (e.g. an entrance name mismatch between `regions.py` and `rules.py`).
3. **Class upgrades are a Progressive item now.** "Progressive Passenger
   Class Upgrade" has 2 copies in the pool; holding 1 grants 2nd Class,
   holding 2 grants 1st Class (`state.count(...)` in `rules.py`). Softening
   the DeskBot via the Sculpture Room still costs no items — it's modeled
   as always-available flavor, not a gate.
4. **No client exists yet.** This package only defines multiworld logic. See
   `CHECKS_AND_ITEMS.md` (shipped alongside this world's zip, not inside it)
   for what a client mod needs to detect and report -- note that doc has
   **not** been re-synced with this revision's renames yet, so treat its
   exact check/item names as stale until it's regenerated.

## A real fill failure this design already ran into
An earlier revision had 59 real locations against 40 real items per player
and assumed the AP core would auto-generate filler to cover the 19-item
gap. It doesn't, at least not reliably — actual generation produced
`Player X had N more locations than items` followed by
`Fill.FillError: Unable to fill all locations`, with exactly 19 unfilled
locations per affected player. `create_items()` now pads the pool with
filler explicitly (via `get_filler_item_name()`) until it matches the
non-event location count exactly, rather than leaving that to chance. If
you change the item or location counts later, re-run the dry-run harness
(a mock-BaseClasses script, not included here) to confirm
`len(itempool) == non_event_location_count` per player before trusting a
real generation.

## Consistency pass (this revision)
A round of renaming (SGT Class Floor -> SGT Class Lobby, Second/First Class
Floor -> 2nd/1st Class Lobby, Bilge -> Bilge Room, "Arrive for the First
Time" -> "Visited", several shortened check names, etc.) had been applied
directly to `locations.py` without propagating everywhere else. `regions.py`
had already been kept in sync; `rules.py` had not, and would have raised
`KeyError` at generation time on four checks:
- `"2nd Class Room - Titania's Ear"` -> `"2nd Class Room - Titania's Ear (Pistachio Bowl)"`
- `"Music Room - Titania's Ear"` -> `"Music Room - Titania's Ear (Phonograph)"`
- `"Arboretum (Autumn) - Titania's Speech Center"` -> `"Arboretum - Titania's Speech Center"`
- `"Arboretum (Winter) - Titania's Mouth"` -> `"Arboretum - Titania's Mouth"`

`rules.py` also referenced an item that never existed under that name --
`state.has("Perch", player)` -- the real item is `"Perch (Luggage Tool)"`
in `items.py`; that's fixed too.

Separately, `locations.py` itself had one internal inconsistency: every
other 1st/2nd Class check and region uses the numeral form
("2nd Class Room", "1st Class Lobby", "1st Class Restaurant"), except
`"DeskBot - First Class Upgrade"`, which still spelled it out. Renamed to
`"DeskBot - 1st Class Upgrade"` and propagated everywhere it's referenced
(`rules.py`, comments in `items.py`).

Verified with the same mock-BaseClasses dry-run harness used throughout
this project: `set_rules()` now runs clean for multiple simulated players
with no `KeyError`, and a full-item-collection playthrough still reaches
Victory.

## Known simplifications (see the logic-model doc for full reasoning)
- SGT-class floors (28–38) are modeled as one generic region, not 11
  separate ones — the walkthrough doesn't distinguish between them
  logically.
- The Bomb, the three e-mails, and the BarBot personality tweak are filler
  locations with no downstream logic dependency, matching the source
  material (they're genuinely optional).
- The "Long Stick required for Speech Center" and "both Maitre'D arms free
  required for Mouth" dependencies are enforced even though the original
  walkthrough's section headers don't call them out — see the logic model's
  section 5 for why.
