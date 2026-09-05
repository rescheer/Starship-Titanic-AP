"""
Starship Titanic - Access rules
"""
from __future__ import annotations

from typing import TYPE_CHECKING

from rule_builder.rules import CanReachRegion, Has, HasAll, Rule # type: ignore

from .items import (
    item_name_groups,
    PROGRESSIVE_CLASS_UPGRADE,
    SECOND_CLASS_TIER,
    FIRST_CLASS_TIER,
    PROGRESSIVE_STATEROOM,
    SGT_STATEROOM_TIER,
    SECOND_STATEROOM_TIER,
    FIRST_STATEROOM_TIER,
)

if TYPE_CHECKING:
    from . import StarshipTitanicWorld

TITANIA_PARTS = item_name_groups["Titania Parts"]

HAS_SECOND_CLASS = Has(PROGRESSIVE_CLASS_UPGRADE, count=SECOND_CLASS_TIER)
HAS_FIRST_CLASS = Has(PROGRESSIVE_CLASS_UPGRADE, count=FIRST_CLASS_TIER)

HAS_SGT_ROOM = Has(PROGRESSIVE_STATEROOM, count=SGT_STATEROOM_TIER)
HAS_SECOND_ROOM = Has(PROGRESSIVE_STATEROOM, count=SECOND_STATEROOM_TIER)
HAS_FIRST_ROOM = Has(PROGRESSIVE_STATEROOM, count=FIRST_STATEROOM_TIER)


def set_rules(world: StarshipTitanicWorld) -> None:
    # In order for AP to generate an item layout that is actually possible for the player to complete,
    # we need to define rules for our Entrances and Locations.
    # Note: Regions do not have rules, the Entrances connecting them do!
    # We'll do entrances first, then locations, and then finally set our victory condition.
    set_all_entrance_rules(world)
    set_all_location_rules(world)
    set_completion_condition(world)


def set_all_entrance_rules(world: StarshipTitanicWorld) -> None:
    # First Class
    first_class_entrances = (
        "Top of the Well -> Arboretum",
        "Top of the Well -> 1st Class Restaurant",
        "Top of the Well -> 1st Class Lobby",
        "1st Class Lobby -> 1st Class Stateroom",
    )
    for entrance_name in first_class_entrances:
        world.set_rule(world.get_entrance(entrance_name), HAS_FIRST_CLASS)

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
        world.set_rule(world.get_entrance(entrance_name), HAS_SECOND_CLASS)

    # The Bridge only unlocks once Titania has been repaired
    world.set_rule(world.get_entrance("Titania's Room -> Bridge"), Has("Titania Repaired"))


def set_all_location_rules(world: StarshipTitanicWorld) -> None:
    # ---------------------------------------------------------------- #
    # Embarkation Lobby
    # ---------------------------------------------------------------- #
    world.set_rule(
        world.get_location("DeskBot - 2nd Class Upgrade"),
        Has("Magazine")
        & CanReachRegion("Embarkation Lobby")
    )
    world.set_rule(
        world.get_location("DeskBot - 2nd Class Upgrade (Event)"),
        Has("Magazine")
        & CanReachRegion("Embarkation Lobby")
    )
    world.set_rule(
        world.get_location("DeskBot - 1st Class Upgrade"),
        Has("2nd Class Upgrade")
        & CanReachRegion("Sculpture Chamber")
        & CanReachRegion("Embarkation Lobby")
    )

    # ---------------------------------------------------------------- #
    # Top of the Well
    # ---------------------------------------------------------------- #
    # (no location rules)

    # ---------------------------------------------------------------- #
    # Bottom of the Well / Broken ELevator
    # ---------------------------------------------------------------- #
    world.set_rule(
        world.get_location("Broken Elevator - Titania's Eye (Elevator)"),
        Has("LiftBot Head")
        & CanReachRegion("Top of the Well") | CanReachRegion("Bottom of the Well")
    )

    # ---------------------------------------------------------------- #
    # Parrot Lobby
    # ---------------------------------------------------------------- #
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
        # Event
        world.get_location("Titania's Room - Repair Titania"),
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
        & HAS_SECOND_CLASS
        & CanReachRegion("Promenade Deck")
        & CanReachRegion("Titania's Room")
    ) """

    # ---------------------------------------------------------------- #
    # Arboretum
    # ---------------------------------------------------------------- #
    world.set_rule(
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
        HasAll("Lemon", "Crushed TV", "Bar Glass", "Blue Fuse")
        & CanReachRegion("Bar")
        & CanReachRegion("Promenade Deck")
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
        "1st Class Restaurant - Maitre'D Bot's Right Arm",
    )
    for loc_name in first_class_restaurant_table_locations:
        world.set_rule(world.get_location(loc_name),
            Has("Restaurant Table Reservation")
            & CanReachRegion("1st Class Restaurant")
        )

    world.set_rule(
        # Event
        world.get_location("1st Class Restaurant - Titania's Auditory Center"),
        Has("Maitre'D Bot's Right Arm")
        & CanReachRegion("1st Class Restaurant")
    )

    # ---------------------------------------------------------------- #
    # Bridge
    # ---------------------------------------------------------------- #
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
        HAS_SGT_ROOM
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
    world.set_rule(
        world.get_location("2nd Class Stateroom - Titania's Ear (Pistachio Bowl)"),
        Has("Magazine")
        & HAS_SECOND_ROOM
        & CanReachRegion("Parrot Lobby")
        & CanReachRegion("2nd Class Stateroom")
    )

    # ---------------------------------------------------------------- #
    # SGT Class Stateroom
    # ---------------------------------------------------------------- #
    # (no location rules)


def set_completion_condition(world: StarshipTitanicWorld) -> None:
    world.set_completion_rule(Has("Victory"))
