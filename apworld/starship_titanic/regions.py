"""
Starship Titanic - Region graph.

Regions are deliberately coarse (one per "area" from the walkthrough) rather
than per-room, since the source material's logic gates operate at that
granularity. Entrance rules encode the class-upgrade/item gates from the
logic model; per-location rules (for checks that need something extra beyond
simply reaching the region) live in rules.py.

Changelog vs. the previous revision:
- Added "Top of the Well" as a hub region between Embarkation Lobby and
  most of the rest of the ship (per the walkthrough: it's where the
  elevators, the hidden door to Titania's Room, and the stairs all are).
  Bilge Room is the one exception -- its service elevator is explicitly "in the
  Embarkation lobby," so it stays a direct child of Embarkation Lobby.
- "SGT Class Lobbys" renamed to "SGT Class Lobby".
- Region access for 2nd Class Lobby / Bottom of the Well / 1st Class
  Lobby now flows through Top of the Well rather than directly off
  Embarkation Lobby.
"""
from typing import Dict, List

from BaseClasses import Region

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
    "Parrot Lobby",
    "Bilge Room",
    "Titania's Room",
    "Creator's Room",
    "Sculpture Room",
    "SGT Class Lobby",
    "2nd Class Lobby",
    "Bottom of the Well",
    "Broken Elevator",
    "1st Class Lobby",
    "Chevron Room",
    "Promenade Deck",
    "Arboretum",
    "Bar",
    "Music Room",
    "1st Class Restaurant",
    "Bridge",
]

# (from_region, to_region) -> used purely for documentation/creation order;
# actual access rules are attached in rules.py via
# multiworld.get_entrance(name, player).access_rule = ...
# Entrance names follow the "A -> B" convention.
REGION_CONNECTIONS: List[tuple] = [
    ("Menu", "Embarkation Lobby"),
    ("Embarkation Lobby", "Bilge Room"),
    ("Embarkation Lobby", "Top of the Well"),
    ("Top of the Well", "Parrot Lobby"),
    ("Top of the Well", "Titania's Room"),
    ("Top of the Well", "Sculpture Room"),
    ("Top of the Well", "SGT Class Lobby"),
    ("Top of the Well", "2nd Class Lobby"),
    ("Top of the Well", "Bottom of the Well"),
    ("Top of the Well", "1st Class Lobby"),
    ("Titania's Room", "Creator's Room"),
    ("Titania's Room", "Bridge"),
    ("Bottom of the Well", "Broken Elevator"),
    ("1st Class Lobby", "Chevron Room"),
    ("1st Class Lobby", "Promenade Deck"),
    ("1st Class Lobby", "Arboretum"),
    ("1st Class Lobby", "Bar"),
    ("1st Class Lobby", "Music Room"),
    ("1st Class Lobby", "1st Class Restaurant"),
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
