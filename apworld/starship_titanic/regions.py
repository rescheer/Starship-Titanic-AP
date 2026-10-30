"""
Starship Titanic - Region graph
"""
from typing import Dict, List

from BaseClasses import Region # pyright: ignore[reportMissingImports]

from .locations import (
    AUDITORY_CENTER_ITEM,
    AUDITORY_CENTER_LOCATION,
    CLASS_UPGRADE_CHECKS,
    CLASS_UPGRADE_EVENT_LOCATIONS,
    location_table,
    StarshipTitanicLocation,
)
from .options import Goal
from .parrot_hunt import PARROT_HUNT_GOAL_LOCATION, PARROT_HUNT_REMOVED_LOCATIONS, STATEROOM_REGIONS

# (region_name, [location_names_in_this_region])
_regions_and_locations: Dict[str, List[str]] = {}
for loc_name, loc_data in location_table.items():
    _regions_and_locations.setdefault(loc_data.region, []).append(loc_name)

# All regions that exist, even if (currently) empty of locations.
ALL_REGIONS: List[str] = [
    "Menu",
    "Embarkation Lobby",
    "Top of the Well",
    "Bottom of the Well",
    "Parrot Lobby",
    "Bilge Room",
    "Titania's Room",
    "Creator's Chamber",
    "Sculpture Chamber",
    "Promenade Deck",
    "Arboretum",
    "Bar",
    "Music Room",
    "1st Class Restaurant",
    "Bridge",
    "SGT Class Lobby",
    "2nd Class Lobby",
    "1st Class Lobby",
    "1st Class Stateroom",
    "2nd Class Stateroom",
    "SGT Class Stateroom",
]

# (from_region, to_region)
REGION_CONNECTIONS: List[tuple] = [
    # Always Available
    ("Menu", "Embarkation Lobby"),
    # SGT Class
    ("Embarkation Lobby", "Bilge Room"),
    ("Embarkation Lobby", "Top of the Well"),
    ("Top of the Well", "Parrot Lobby"),
    ("Top of the Well", "Titania's Room"),
    ("Top of the Well", "SGT Class Lobby"),
    ("SGT Class Lobby", "SGT Class Stateroom"),
    ("Top of the Well", "Bottom of the Well"),
    ("Top of the Well", "2nd Class Lobby"),
    ("Top of the Well", "1st Class Lobby"),
    # 2nd Class
    ("2nd Class Lobby", "2nd Class Stateroom"),
    ("Top of the Well", "Sculpture Chamber"),
    ("Top of the Well", "Creator's Chamber"),
    ("Top of the Well", "Promenade Deck"),
    ("Top of the Well", "Bar"),
    ("Top of the Well", "Music Room"),
    # 1st Class
    ("1st Class Lobby", "1st Class Stateroom"),
    ("Top of the Well", "Arboretum"),
    ("Top of the Well", "1st Class Restaurant"),
    # After Titania Repair
    ("Titania's Room", "Bridge"),
]


def create_regions(world) -> None:
    multiworld = world.multiworld
    player = world.player

    # The Repair Titania goal completes before the Bridge opens, so keep items out of it
    skip_bridge_checks = world.options.goal == Goal.option_repair_titania
    # The parrot stays in the mail under the Parrot Hunt goal, so the locations that need it can't be completed
    parrot_hunt = world.options.goal == Goal.option_parrot_hunt

    regions: Dict[str, Region] = {}
    for region_name in ALL_REGIONS:
        region = Region(region_name, player, multiworld)
        for loc_name in _regions_and_locations.get(region_name, []):
            loc_data = location_table[loc_name]
            if skip_bridge_checks and region_name == "Bridge" and not loc_data.event_item:
                continue
            if parrot_hunt and loc_name in PARROT_HUNT_REMOVED_LOCATIONS:
                continue
            if loc_name == PARROT_HUNT_GOAL_LOCATION:
                continue  # created below, in the chosen stateroom's region
            if world.class_upgrades_locked and loc_name in CLASS_UPGRADE_EVENT_LOCATIONS:
                continue
            is_auditory_center = loc_name == AUDITORY_CENTER_LOCATION
            # Seeds from before the Auditory Center was a real check (Universal Tracker regenerating one)
            is_event = loc_data.event_item or (is_auditory_center and not world.auditory_center_locked)
            code = None if is_event else world.location_name_to_id[loc_name]
            location = StarshipTitanicLocation(player, loc_name, code, region)
            if loc_data.event_item:
                location.place_locked_item(world.create_event(loc_data.event_item))
            elif is_auditory_center:
                location.place_locked_item((world.create_item if world.auditory_center_locked else world.create_event)(AUDITORY_CENTER_ITEM))
            elif world.class_upgrades_locked and loc_name in CLASS_UPGRADE_CHECKS:
                location.place_locked_item(world.create_item(CLASS_UPGRADE_CHECKS[loc_name]))
            region.locations.append(location)
        regions[region_name] = region
        multiworld.regions.append(region)

    if parrot_hunt:
        region = regions[STATEROOM_REGIONS[world.parrot_room["class"]]]
        location = StarshipTitanicLocation(player, PARROT_HUNT_GOAL_LOCATION, None, region)
        location.place_locked_item(world.create_event(location_table[PARROT_HUNT_GOAL_LOCATION].event_item))
        region.locations.append(location)

    for from_region, to_region in REGION_CONNECTIONS:
        regions[from_region].connect(regions[to_region], f"{from_region} -> {to_region}")
