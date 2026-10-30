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
    PROGRESSIVE_CLUE,
)
from .locations import (
    location_name_to_id,
)
from .logic_export import export_logic
from .parrot_hunt import CLUE_PARTS, choose_room, clue_thresholds
from .regions import create_regions as _create_regions
from .rules import set_rules as _set_rules
from .options import (
    StarshipTitanicOptions,
    Goal,
    ParrotHuntStateroomClass,
    PassengerClassUpgrades,
    StateroomAssignment,
    ItemDeliveryMethod,
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
    access_cards: List[str]  # the Access Cards this slot requires
    # Whether the DeskBot's upgrade checks hold the upgrades themselves: vanilla class upgrades only,
    # and only on seeds generated since that arrangement was added
    class_upgrades_locked: bool
    # Whether the Auditory Center is a real check holding a record of itself: only on seeds generated since
    # that arrangement was added
    auditory_center_locked: bool
    # Every rule set on this slot, by location or entrance name, for export_logic
    logic_rules: Dict[str, Any]
    # Parrot Hunt goal only: the parrot's stateroom ("elevator", "floor", "room", "class"), and the
    # order its clues reveal those parts
    parrot_room: Dict[str, int]
    clue_order: List[str]
    ut_can_gen_without_yaml = True

    item_name_to_id = item_name_to_id
    location_name_to_id = location_name_to_id
    item_name_groups = item_name_groups

    def create_item(self, name: str) -> Item:
        data = item_table[name]
        return StarshipTitanicItem(name, data.classification, data.code, self.player)

    def create_event(self, name: str) -> Item:
        return StarshipTitanicItem(name, ItemClassification.progression, None, self.player)

    def generate_early(self) -> None:
        self.logic_rules = {}
        re_gen_passthrough = getattr(self.multiworld, "re_gen_passthrough", {})
        if self.game in re_gen_passthrough:
            self.apply_slot_data(re_gen_passthrough[self.game])
        else:
            self.access_cards = enabled_access_cards(self.options, self.random)
            self.class_upgrades_locked = self.is_vanilla_class()
            self.auditory_center_locked = True
            # Only rolled under this goal, so other goals' seeds keep the same random stream
            if self.is_parrot_hunt():
                classes = {
                    ParrotHuntStateroomClass.option_first_class: [1],
                    ParrotHuntStateroomClass.option_second_class: [2],
                }.get(self.options.parrot_hunt_stateroom_class.value, [1, 2])
                self.parrot_room = choose_room(classes, self.random)
                self.clue_order = list(CLUE_PARTS)
                self.random.shuffle(self.clue_order)

    def apply_slot_data(self, slot_data: Dict[str, Any]) -> None:
        """Universal Tracker regenerates locally, possibly without a YAML; restore the seed's options from
        slot data instead (and reuse its cards rather than re-rolling Randomized mode)."""
        options = self.options
        options.goal.value = Goal.options[slot_data["goal"]]
        # Keys added after the first release; older seeds fall back to the defaults
        options.passenger_class_upgrades.value = (PassengerClassUpgrades.option_vanilla
            if slot_data.get("vanilla_passenger_class", False) else PassengerClassUpgrades.option_multiworld)
        options.stateroom_assignment.value = (StateroomAssignment.option_vanilla
            if slot_data.get("vanilla_staterooms", False) else StateroomAssignment.option_multiworld)
        if "item_delivery_method" in slot_data:
            options.item_delivery_method.value = ItemDeliveryMethod.options[slot_data["item_delivery_method"]]
        options.easy_music_room_puzzle.value = int(slot_data.get("easy_music_room_puzzle", False))
        self.access_cards = list(slot_data.get("room_access_cards", []))
        self.class_upgrades_locked = slot_data.get("class_upgrades_locked", False)
        self.auditory_center_locked = slot_data.get("auditory_center_locked", False)
        if self.is_parrot_hunt():
            options.parrot_hunt_stateroom_class.value = ParrotHuntStateroomClass.options[
                slot_data["parrot_hunt_stateroom_class"]]
            options.parrot_hunt_clues.value = slot_data["parrot_hunt_clues"]
            self.parrot_room = dict(slot_data["parrot_room"])
            self.clue_order = list(slot_data["clue_order"])

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

    def is_parrot_hunt(self) -> bool:
        return self.options.goal == Goal.option_parrot_hunt

    def create_items(self) -> None:
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

        if self.is_parrot_hunt():
            pool.extend(self.create_item(PROGRESSIVE_CLUE)
                        for _ in range(self.options.parrot_hunt_clues.value))

        # The one-of-each filler and trap copies are only padding, so they give way when the pool runs out of room
        # Filler goes first, then traps.
        for classification in (ItemClassification.filler, ItemClassification.trap):
            while len(pool) > non_event_locations:
                spare = next((item for item in reversed(pool) if item.classification == classification), None)
                if spare is None:
                    break
                pool.remove(spare)

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

    def set_rule(self, spot: Any, rule: Any) -> None:
        self.logic_rules[spot.name] = rule
        super().set_rule(spot, rule)

    def get_filler_item_name(self) -> str:
        return self.random.choice(filler_item_names)

    def fill_slot_data(self) -> Dict[str, Any]:
        slot_data = {
            "goal": self.options.goal.current_key,  # "return_home", "repair_titania" or "parrot_hunt"
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
            "class_upgrades_locked": self.class_upgrades_locked,
            "auditory_center_locked": self.auditory_center_locked,
            "logic": export_logic(self),  # the client's tracker reads this
        }
        if self.is_parrot_hunt():
            clues = self.options.parrot_hunt_clues.value
            slot_data.update({
                "parrot_hunt_stateroom_class": self.options.parrot_hunt_stateroom_class.current_key,
                "parrot_hunt_clues": clues,
                "clue_item": PROGRESSIVE_CLUE,
                "parrot_room": self.parrot_room,  # "elevator", "floor", "room" (PET numbering) and "class" (1 or 2)
                "clue_order": self.clue_order,  # which part each clue reveals, in reveal order
                # Sent rather than recomputed by the client, so the formula only lives in parrot_hunt.py
                "clue_thresholds": clue_thresholds(clues),
            })
        return slot_data
