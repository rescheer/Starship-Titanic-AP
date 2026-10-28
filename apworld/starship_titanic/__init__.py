"""
Starship Titanic - Archipelago World.
"""
from typing import Any, Dict, List

from BaseClasses import Item, ItemClassification, Tutorial # pyright: ignore[reportMissingImports]
from worlds.AutoWorld import World, WebWorld # pyright: ignore[reportMissingImports]

from .items import (
    StarshipTitanicItem,
    item_table,
    item_name_to_id,
    item_name_groups,
    filler_item_names,
    PROGRESSIVE_CLASS_UPGRADE,
    PROGRESSIVE_STATEROOM,
)
from .locations import (
    location_name_to_id,
)
from .regions import create_regions as _create_regions
from .rules import set_rules as _set_rules
from .options import (
    StarshipTitanicOptions,
    PassengerClassUpgrades,
    StateroomAssignment,
    enabled_access_cards,
    starship_titanic_option_groups,
)


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
    option_groups = starship_titanic_option_groups


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
    access_cards: List[str]  # the Access Cards this slot requires (options.py)

    item_name_to_id = item_name_to_id
    location_name_to_id = location_name_to_id
    item_name_groups = item_name_groups

    def create_item(self, name: str) -> Item:
        data = item_table[name]
        return StarshipTitanicItem(name, data.classification, data.code, self.player)

    def create_event(self, name: str) -> Item:
        return StarshipTitanicItem(name, ItemClassification.progression, None, self.player)

    def generate_early(self) -> None:
        # Universal Tracker regenerates locally; reuse the seed's cards instead of re-rolling Randomized mode
        re_gen_passthrough = getattr(self.multiworld, "re_gen_passthrough", {})
        if self.game in re_gen_passthrough:
            self.access_cards = list(re_gen_passthrough[self.game]["room_access_cards"])
        else:
            self.access_cards = enabled_access_cards(self.options, self.random)

    @staticmethod
    def interpret_slot_data(slot_data: Dict[str, Any]) -> Dict[str, Any]:
        # Universal Tracker hook: the returned dict becomes multiworld.re_gen_passthrough[game]
        return slot_data

    def create_regions(self) -> None:
        _create_regions(self)

    def is_vanilla_class(self) -> bool:
        return self.options.passenger_class_upgrades == PassengerClassUpgrades.option_vanilla

    def is_vanilla_staterooms(self) -> bool:
        return self.options.stateroom_assignment == StateroomAssignment.option_vanilla

    def create_items(self) -> None:
        # Event locations are already filled; this also excludes locations the goal skipped (regions.py)
        non_event_locations = len(self.multiworld.get_unfilled_locations(self.player))

        pool = []
        for name, data in item_table.items():
            if data.code is None:
                continue
            if data.group == "Access Card" and name not in self.access_cards:
                continue
            # The vanilla options leave these to the DeskBot
            if name == PROGRESSIVE_CLASS_UPGRADE and self.is_vanilla_class():
                continue
            if name == PROGRESSIVE_STATEROOM and self.is_vanilla_staterooms():
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
            "goal": self.options.goal.current_key,  # "return_home" or "repair_titania"
            "titania_parts": sorted(item_name_groups["Titania Parts"]),
            "vanilla_passenger_class": self.is_vanilla_class(),
            "vanilla_staterooms": self.is_vanilla_staterooms(),
            "progressive_class_upgrade_item": "Progressive Passenger Class Upgrade",
            "second_class_tier": 1,
            "first_class_tier": 2,
            "progressive_stateroom_item": "Progressive Stateroom",
            "sgt_stateroom_tier": 1,
            "second_stateroom_tier": 2,
            "first_stateroom_tier": 3,
            "room_access_cards": self.access_cards,  # the client only locks the rooms of these cards
            "item_delivery_method": self.options.item_delivery_method.current_key,  # "succubus" or "direct_to_inventory"
            "easy_music_room_puzzle": bool(self.options.easy_music_room_puzzle),
        }
