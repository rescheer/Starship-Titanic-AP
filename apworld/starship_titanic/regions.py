"""
Starship Titanic - Region graph
"""
from typing import Dict, List

from BaseClasses import Region # pyright: ignore[reportMissingImports]

from .locations import location_table, StarshipTitanicLocation

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

    regions: Dict[str, Region] = {}
    for region_name in ALL_REGIONS:
        region = Region(region_name, player, multiworld)
        for loc_name in _regions_and_locations.get(region_name, []):
            loc_data = location_table[loc_name]
            code = None if loc_data.event_item else world.location_name_to_id[loc_name]
            location = StarshipTitanicLocation(player, loc_name, code, region)
            if loc_data.event_item:
                location.place_locked_item(world.create_event(loc_data.event_item))
            region.locations.append(location)
        regions[region_name] = region
        multiworld.regions.append(region)

    for from_region, to_region in REGION_CONNECTIONS:
        regions[from_region].connect(regions[to_region], f"{from_region} -> {to_region}")
