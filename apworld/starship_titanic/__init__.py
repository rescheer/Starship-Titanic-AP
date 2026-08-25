"""
Starship Titanic - Archipelago World (first pass, revision 2).

Built from a walkthrough-derived puzzle-logic model. This has NOT been
tested against a live Archipelago core -- validate with
`python Generate.py` (or the WebHost generator) against your installed AP
version before relying on it, and check API calls like Region.connect(),
place_locked_item(), and the Options dataclass pattern against that
version's BaseClasses/Options modules, since these have drifted across AP
releases.

See CHECKS_AND_ITEMS.md (shipped alongside this world, and also handed to
whoever is building the client-side mod) for the full human-readable
reference this code implements.

Changelog vs. the previous revision:
- create_items() now respects STItemData.quantity, since Progressive
  Passenger Class Upgrade needs 2 copies in the pool.
- create_items() now explicitly pads the pool with filler to match the
  non-event location count exactly, rather than assuming the AP core
  auto-generates filler for a per-player location/item surplus. It
  doesn't, at least not reliably across forks/versions -- relying on that
  produced a real FillError in practice (a "Player X had N more locations
  than items" log line followed by dozens of permanently unfilled
  locations, one per excess slot per player). Pool size and non-event
  location count are now asserted equal after padding.
"""
from typing import Any, Dict

from BaseClasses import Item, ItemClassification, Tutorial
from worlds.AutoWorld import World, WebWorld

from .items import (
    StarshipTitanicItem,
    item_table,
    item_name_to_id,
    item_name_groups,
    filler_item_names,
)
from .locations import (
    location_table,
    location_name_to_id,
)
from .regions import create_regions as _create_regions
from .rules import set_rules as _set_rules
from .options import StarshipTitanicOptions


class StarshipTitanicWeb(WebWorld):
    theme = "space"
    tutorials = [
        Tutorial(
            "Multiworld Setup Guide",
            "A guide to setting up Starship Titanic for MultiworldGG/Archipelago.",
            "English",
            "setup_en.md",
            "setup/en",
            ["coulomb"],
        )
    ]


class StarshipTitanicWorld(World):
    """
    Starship Titanic: guide a lost passenger through Mother, the Fuse Box,
    both class upgrades, and Titania's eleven scattered parts to repair the
    ship's AI and pilot her home.
    """

    game = "Starship Titanic"
    web = StarshipTitanicWeb()
    options_dataclass = StarshipTitanicOptions
    options: StarshipTitanicOptions

    item_name_to_id = item_name_to_id
    location_name_to_id = location_name_to_id
    item_name_groups = item_name_groups

    def create_item(self, name: str) -> Item:
        data = item_table[name]
        return StarshipTitanicItem(name, data.classification, data.code, self.player)

    def create_event(self, name: str) -> Item:
        return StarshipTitanicItem(name, ItemClassification.progression, None, self.player)

    def create_regions(self) -> None:
        _create_regions(self)

    def create_items(self) -> None:
        non_event_locations = sum(
            1 for data in location_table.values() if data.event_item is None
        )

        pool = []
        for name, data in item_table.items():
            if data.code is None:  # events never enter the pool
                continue
            pool.extend(self.create_item(name) for _ in range(data.quantity))

        # Do NOT rely on the core to auto-pad a per-player location/item
        # count mismatch with filler -- that behavior isn't guaranteed
        # across AP forks/versions, and silently assuming it exists causes
        # exactly the failure this comment is replacing: a
        # "Player X had N more locations than items" log line followed by
        # Fill.FillError because nothing actually filled the gap. Pad
        # explicitly, every time, so generation never depends on that.
        deficit = non_event_locations - len(pool)
        if deficit > 0:
            pool.extend(self.create_item(self.get_filler_item_name()) for _ in range(deficit))
        elif deficit < 0:
            raise Exception(
                f"Starship Titanic: item pool ({len(pool)}) exceeds non-event "
                f"location count ({non_event_locations}) even before padding; "
                f"add locations or trim items in items.py/locations.py."
            )

        assert len(pool) == non_event_locations, (
            f"Starship Titanic: padded pool ({len(pool)}) still doesn't match "
            f"non-event location count ({non_event_locations})."
        )
        self.multiworld.itempool += pool

    def set_rules(self) -> None:
        _set_rules(self)

    def get_filler_item_name(self) -> str:
        return self.random.choice(filler_item_names)

    def fill_slot_data(self) -> Dict[str, Any]:
        # Handed to the client mod at connect time. Kept small and stable
        # so the client doesn't need to know Archipelago's internal item
        # IDs -- it can just match on these readable event/location names.
        return {
            "titania_parts": sorted(item_name_groups["Titania Parts"]),
            "progressive_class_upgrade_item": "Progressive Passenger Class Upgrade",
            "second_class_tier": 1,
            "first_class_tier": 2,
        }
