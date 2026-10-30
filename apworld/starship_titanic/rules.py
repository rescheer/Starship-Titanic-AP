"""
Starship Titanic - Access rules
"""
from __future__ import annotations

from typing import TYPE_CHECKING

from rule_builder.rules import CanReachRegion, Has, HasAll, Rule, True_ # type: ignore

from .options import Goal
from .regions import REGION_CONNECTIONS
from .items import (
    item_name_groups,
    REGION_ACCESS_CARDS,
    PROGRESSIVE_CLASS_UPGRADE,
    SECOND_CLASS_TIER,
    FIRST_CLASS_TIER,
    SECOND_CLASS_UPGRADE_EVENT,
    FIRST_CLASS_UPGRADE_EVENT,
    PROGRESSIVE_STATEROOM,
    SGT_STATEROOM_TIER,
    SECOND_STATEROOM_TIER,
    FIRST_STATEROOM_TIER,
    PROGRESSIVE_CLUE,
)
from .parrot_hunt import PARROT_HUNT_GOAL_LOCATION, PARROT_RELEASED_EVENT

if TYPE_CHECKING:
    from . import StarshipTitanicWorld

TITANIA_PARTS = item_name_groups["Titania Parts"]



def has_second_class(world: StarshipTitanicWorld) -> Rule:
    if world.is_vanilla_class():
        return Has(SECOND_CLASS_UPGRADE_EVENT)
    return Has(PROGRESSIVE_CLASS_UPGRADE, count=SECOND_CLASS_TIER)


def has_first_class(world: StarshipTitanicWorld) -> Rule:
    if world.is_vanilla_class():
        return Has(FIRST_CLASS_UPGRADE_EVENT)
    return Has(PROGRESSIVE_CLASS_UPGRADE, count=FIRST_CLASS_TIER)


# Vanilla staterooms: the DeskBot assigns the SGT room at check-in and each class's room along with its upgrade
def has_sgt_room(world: StarshipTitanicWorld) -> Rule:
    if world.is_vanilla_staterooms():
        return True_()
    return Has(PROGRESSIVE_STATEROOM, count=SGT_STATEROOM_TIER)


def has_second_room(world: StarshipTitanicWorld) -> Rule:
    if world.is_vanilla_staterooms():
        return Has(SECOND_CLASS_UPGRADE_EVENT)
    return Has(PROGRESSIVE_STATEROOM, count=SECOND_STATEROOM_TIER)


def has_first_room(world: StarshipTitanicWorld) -> Rule:
    if world.is_vanilla_staterooms():
        return Has(FIRST_CLASS_UPGRADE_EVENT)
    return Has(PROGRESSIVE_STATEROOM, count=FIRST_STATEROOM_TIER)


def set_rules(world: StarshipTitanicWorld) -> None:
    # In order for AP to generate an item layout that is actually possible for the player to complete,
    # we need to define rules for our Entrances and Locations.
    # Note: Regions do not have rules, the Entrances connecting them do!
    # We'll do entrances first, then locations, and then finally set our victory condition.
    set_all_entrance_rules(world)
    set_all_location_rules(world)
    set_completion_condition(world)


def set_all_entrance_rules(world: StarshipTitanicWorld) -> None:
    # Collected per entrance first, since set_rule replaces rather than adds: an entrance can need both a
    # passenger class and an Access Card.
    entrance_rules: dict[str, Rule] = {}

    def add_rule(entrance_name: str, rule: Rule) -> None:
        existing = entrance_rules.get(entrance_name)
        entrance_rules[entrance_name] = rule if existing is None else existing & rule

    # First Class
    first_class_entrances = (
        "Top of the Well -> Arboretum",
        "Top of the Well -> 1st Class Restaurant",
        "Top of the Well -> 1st Class Lobby",
        "1st Class Lobby -> 1st Class Stateroom",
    )
    for entrance_name in first_class_entrances:
        add_rule(entrance_name, has_first_class(world))

    # Second Class
    second_class_entrances = (
        "Top of the Well -> Creator's Chamber",
        "Top of the Well -> Bar",
        "Top of the Well -> Music Room",
        "Top of the Well -> Sculpture Chamber",
        "Top of the Well -> Promenade Deck",
        "Top of the Well -> 2nd Class Lobby",
        "2nd Class Lobby -> 2nd Class Stateroom",
    )
    for entrance_name in second_class_entrances:
        add_rule(entrance_name, has_second_class(world))

    # The Bridge only unlocks once Titania has been repaired
    add_rule("Titania's Room -> Bridge", Has("Titania Repaired"))

    # Access Cards - every entrance into a card-locked region needs its card
    for from_region, to_region in REGION_CONNECTIONS:
        card = REGION_ACCESS_CARDS.get(to_region)
        if card in world.access_cards:
            add_rule(f"{from_region} -> {to_region}", Has(card))

    for entrance_name, rule in entrance_rules.items():
        world.set_rule(world.get_entrance(entrance_name), rule)


def set_all_location_rules(world: StarshipTitanicWorld) -> None:
    # Not created under this goal
    parrot_hunt = world.options.goal == Goal.option_parrot_hunt

    # ---------------------------------------------------------------- #
    # Embarkation Lobby
    # ---------------------------------------------------------------- #
    world.set_rule(
        world.get_location("DeskBot - 2nd Class Upgrade"),
        Has("Magazine")
        & CanReachRegion("Embarkation Lobby")
    )
    if not world.class_upgrades_locked:  # not created when locked (regions.py)
        world.set_rule(
            world.get_location("DeskBot - 2nd Class Upgrade (Event)"),
            Has("Magazine")
            & CanReachRegion("Embarkation Lobby")
        )
    world.set_rule(
        world.get_location("DeskBot - 1st Class Upgrade"),
        Has(SECOND_CLASS_UPGRADE_EVENT)
        & CanReachRegion("Sculpture Chamber")
        & CanReachRegion("Embarkation Lobby")
    )
    if not world.class_upgrades_locked:
        world.set_rule(
            world.get_location("DeskBot - 1st Class Upgrade (Event)"),
            Has(SECOND_CLASS_UPGRADE_EVENT)
            & CanReachRegion("Sculpture Chamber")
            & CanReachRegion("Embarkation Lobby")
        )

    # ---------------------------------------------------------------- #
    # Top of the Well
    # ---------------------------------------------------------------- #
    # (no location rules)

    # ---------------------------------------------------------------- #
    # Broken ELevator
    # ---------------------------------------------------------------- #
    # The broken elevator (4) can only be called from the floor it's stuck on - a 2nd Class floor, shown on the
    # TV's channel 5 - so it's boarded from the 2nd Class Lobby, after riding elevator 3 there from the Top or
    # Bottom of the Well.
    world.set_rule(
        world.get_location("Broken Elevator - Titania's Eye (Elevator)"),
        Has("LiftBot Head")
        & (CanReachRegion("Top of the Well") | CanReachRegion("Bottom of the Well"))
    )
    
    # ---------------------------------------------------------------- #
    # Bottom of the Well
    # ---------------------------------------------------------------- #
    world.set_rule(
        world.get_location("Bottom of the Well - Crushed Television"),
        CanReachRegion("Parrot Lobby")
        & CanReachRegion("Bottom of the Well")
    )

    # ---------------------------------------------------------------- #
    # Parrot Lobby
    # ---------------------------------------------------------------- #
    if not parrot_hunt:
        world.set_rule(
            world.get_location("Parrot Lobby - Feather"),
            CanReachRegion("Parrot Lobby")
            & CanReachRegion("Top of the Well")
        )

    world.set_rule(
        world.get_location("Parrot Lobby - Titania's Nose"),
        Has("Hose")
        & CanReachRegion("Parrot Lobby")
    )

    if not parrot_hunt:
        world.set_rule(
            world.get_location("Parrot Lobby - Titania's Core"),
            HasAll("Napkin", "Yellow Fuse")
            & CanReachRegion("Titania's Room")
            & CanReachRegion("SGT Class Lobby")
            & CanReachRegion("Parrot Lobby")
        )

    # ---------------------------------------------------------------- #
    # Bilge Room
    # ---------------------------------------------------------------- #
    world.set_rule(
        world.get_location("Bilge Room - Titania's Olfactory Center"),
        Has("Feather")
        & CanReachRegion("Bilge Room")
    )
    world.set_rule(
        world.get_location("Bilge Room - Blue Fuse"),
        Has("Feather")
        & CanReachRegion("Bilge Room")
    )

    # ---------------------------------------------------------------- #
    # Titania's Room
    # ---------------------------------------------------------------- #
    world.set_rule(
        world.get_location("Titania's Room - Repair Titania"),
        HasAll(*TITANIA_PARTS, "Titania's Auditory Center")
        & CanReachRegion("Titania's Room")
    )
    world.set_rule(
        # Event
        world.get_location("Titania's Room - Repair Titania (Event)"),
        HasAll(*TITANIA_PARTS, "Titania's Auditory Center")
        & CanReachRegion("Titania's Room")
    )

    # ---------------------------------------------------------------- #
    # Creator's Chamber
    # ---------------------------------------------------------------- #
    # (no location rules)

    # ---------------------------------------------------------------- #
    # Sculpture Chamber
    # ---------------------------------------------------------------- #
    # (no location rules)

    # ---------------------------------------------------------------- #
    # Promenade Deck
    # ---------------------------------------------------------------- #
    world.set_rule(
        world.get_location("Promenade Deck - Hammer"),
        Has("Perch")
        & CanReachRegion("Promenade Deck")
    )

    """ world.set_rule(
        world.get_location("Promenade Deck - Pureed Starlings"),
        HasAll("Blue Fuse", "Bar Glass")
        & has_second_class(world)
        & CanReachRegion("Promenade Deck")
        & CanReachRegion("Titania's Room")
    ) """

    # ---------------------------------------------------------------- #
    # Arboretum
    # ---------------------------------------------------------------- #
    world.set_rule(
        # Green Fuse not needed - Arboretum starts with lemon available
        world.get_location("Arboretum - Lemon"),
        Has("Long Stick")
        & CanReachRegion("Arboretum")
    )

    world.set_rule(
        world.get_location("Arboretum - Titania's Speech Center"),
        HasAll("Green Fuse", "Long Stick")
        & CanReachRegion("Arboretum")
        & CanReachRegion("Titania's Room")
    )

    world.set_rule(
        world.get_location("Arboretum - Titania's Mouth"),
        HasAll("Green Fuse", "Maitre'D Bot's Left Arm", "Maitre'D Bot's Right Arm")
        & CanReachRegion("Arboretum")
        & CanReachRegion("Titania's Room")
    )

    # ---------------------------------------------------------------- #
    # Bar
    # ---------------------------------------------------------------- #
    world.set_rule(
        world.get_location("Bar - Titania's Vision Center"),
        HasAll("Lemon", "Crushed TV", "Bar Glass", "Blue Fuse", "Yellow Fuse")
        & CanReachRegion("Bar")
        & CanReachRegion("Promenade Deck")
        & CanReachRegion("Titania's Room")  # the fuses go in the Fuse Box
    )

    # ---------------------------------------------------------------- #
    # Music Room
    # ---------------------------------------------------------------- #
    world.set_rule(
        world.get_location("Music Room - Titania's Ear (Phonograph)"),
        CanReachRegion("Music Room")
    )

    # ---------------------------------------------------------------- #
    # 1st Class Restaurant
    # ---------------------------------------------------------------- #
    # All these locations are on the locked table in the 1st class restaurant
    first_class_restaurant_table_locations = (
        "1st Class Restaurant - Napkin",
        "1st Class Restaurant - Green Fuse",
        "1st Class Restaurant - Maitre'D Bot's Left Arm",
    )
    for loc_name in first_class_restaurant_table_locations:
        world.set_rule(world.get_location(loc_name),
            Has("Restaurant Table Reservation")
            & CanReachRegion("1st Class Restaurant")
        )
    
    world.set_rule(
        world.get_location("1st Class Restaurant - Maitre'D Bot's Right Arm"),
        CanReachRegion("1st Class Restaurant")
    )

    world.set_rule(
        # Event
        world.get_location("1st Class Restaurant - Titania's Auditory Center"),
        HasAll("Maitre'D Bot's Right Arm", "Maitre'D Bot's Left Arm")
        & CanReachRegion("1st Class Restaurant")
        & CanReachRegion("Music Room")  # the Phonograph puzzle releases it
    )

    # ---------------------------------------------------------------- #
    # Bridge
    # ---------------------------------------------------------------- #
    if world.options.goal != Goal.option_repair_titania:  # not created under this goal (regions.py)
        world.set_rule(
            world.get_location("Bridge - Visited"),
            CanReachRegion("Bridge")
        )

    world.set_rule(
        # Event
        world.get_location("The End - Return Home"),
        CanReachRegion("Bridge")
    )

    # ---------------------------------------------------------------- #
    # SGT Class Lobby
    # ---------------------------------------------------------------- #
    world.set_rule(
        world.get_location("SGT Class Lobby - Magazine"),
        has_sgt_room(world)
        & CanReachRegion("SGT Class Stateroom")
        )

    world.set_rule(
        world.get_location("SGT Class Lobby - Long Stick"),
        Has("Hammer")
        & CanReachRegion("SGT Class Lobby")
    )

    # ---------------------------------------------------------------- #
    # 2nd Class Lobby
    # ---------------------------------------------------------------- #
    # (no location rules)

    # ---------------------------------------------------------------- #
    # 1st Class Lobby
    # ---------------------------------------------------------------- #
    # (no location rules)

    # ---------------------------------------------------------------- #
    # 1st Class Stateroom
    # ---------------------------------------------------------------- #
    # (no location rules)

    # ---------------------------------------------------------------- #
    # 2nd Class Stateroom
    # ---------------------------------------------------------------- #
    if not parrot_hunt:
        world.set_rule(
            # Any 2nd Class Stateroom works, not just the assigned one
            world.get_location("2nd Class Stateroom - Titania's Ear (Pistachio Bowl)"),
            CanReachRegion("Parrot Lobby")
            & CanReachRegion("2nd Class Stateroom")
        )

    # ---------------------------------------------------------------- #
    # SGT Class Stateroom
    # ---------------------------------------------------------------- #
    # (no location rules)

    # ---------------------------------------------------------------- #
    # Parrot Hunt
    # ---------------------------------------------------------------- #
    if parrot_hunt:
        # In the chosen stateroom's region (regions.py), whose entrances already cover the class and Access Card
        world.set_rule(
            # Event
            world.get_location(PARROT_HUNT_GOAL_LOCATION),
            Has(PROGRESSIVE_CLUE, count=world.options.parrot_hunt_clues.value)
        )


def set_completion_condition(world: StarshipTitanicWorld) -> None:
    if world.options.goal == Goal.option_repair_titania:
        world.set_completion_rule(Has("Titania Repaired"))
    elif world.options.goal == Goal.option_parrot_hunt:
        world.set_completion_rule(Has(PARROT_RELEASED_EVENT))
    else:
        world.set_completion_rule(Has("Victory"))
