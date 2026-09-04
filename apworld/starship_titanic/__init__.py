"""
Starship Titanic - Archipelago World.
"""
from typing import Any, Dict

from BaseClasses import Item, ItemClassification, Tutorial # pyright: ignore[reportMissingImports]
from worlds.AutoWorld import World, WebWorld # pyright: ignore[reportMissingImports]

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
    Starship Titanic: wander through a wayward starship full of barely-
    functioning robots, solving puzzles and colleecting eleven
    scattered parts to repair the ship's AI and make it back home.
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
            if data.code is None:
                continue
            pool.extend(self.create_item(name) for _ in range(data.quantity))

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
        return {
            "titania_parts": sorted(item_name_groups["Titania Parts"]),
            "progressive_class_upgrade_item": "Progressive Passenger Class Upgrade",
            "second_class_tier": 1,
            "first_class_tier": 2,
            "progressive_stateroom_item": "Progressive Stateroom",
            "sgt_stateroom_tier": 1,
            "second_stateroom_tier": 2,
            "first_stateroom_tier": 3,
        }
