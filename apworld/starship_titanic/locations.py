"""
Starship Titanic - Location check definitions
"""
import collections
from typing import Dict, Optional

from BaseClasses import Location # pyright: ignore[reportMissingImports]

LOCATION_ID_BASE = 771900000

_next_titanic_offset = 0

_STLocationDataBase = collections.namedtuple("STLocationData", ["region", "event_item", "offset"])

class STLocationData(_STLocationDataBase):
    def __new__(cls, region: str, event_item: Optional[str] = None) -> "STLocationData":
        global _next_titanic_offset
        offset = _next_titanic_offset
        _next_titanic_offset += 1
        return super().__new__(cls, region, event_item, offset)


class StarshipTitanicLocation(Location):
    game: str = "Starship Titanic"


location_table: Dict[str, STLocationData] = {
    # ---------------------------------------------------------------- #
    # Embarkation Lobby
    # ---------------------------------------------------------------- #
    # POI/Visit
    "Embarkation Lobby - Succ-U-Bus": STLocationData("Embarkation Lobby"),
    "Embarkation Lobby - Visited": STLocationData("Embarkation Lobby"),
    # Item
    # Puzzle
    "DeskBot - 2nd Class Upgrade": STLocationData("Embarkation Lobby"),
    "DeskBot - 1st Class Upgrade": STLocationData("Embarkation Lobby"),
    "DeskBot - SGT Stateroom Assigned": STLocationData("Embarkation Lobby", "SGT Stateroom Assigned"),
    "DeskBot - 2nd Class Stateroom Assigned": STLocationData("Embarkation Lobby", "2nd Class Stateroom Assigned"),
    "DeskBot - 1st Class Stateroom Assigned": STLocationData("Embarkation Lobby", "1st Class Stateroom Assigned"),
    # Other

    # ---------------------------------------------------------------- #
    # Top of the Well
    # ---------------------------------------------------------------- #
    # POI/Visit
    "Top of the Well - Visited": STLocationData("Top of the Well"),
    # Item
    # Puzzle
    # Other

    # ---------------------------------------------------------------- #
    # Parrot Lobby
    # ---------------------------------------------------------------- #
    # POI/Visit
    "Parrot Lobby - Succ-U-Bus": STLocationData("Parrot Lobby"),
    "Parrot Lobby - Visited": STLocationData("Parrot Lobby"),
    # Item
    "Parrot Lobby - Feather": STLocationData("Parrot Lobby"),
    "Parrot Lobby - Perch": STLocationData("Parrot Lobby"),
    "Parrot Lobby - Titania's Nose": STLocationData("Parrot Lobby"),
    "Parrot Lobby - Titania's Core": STLocationData("Parrot Lobby"),
    # Puzzle
    # Other

    # ---------------------------------------------------------------- #
    # Bilge Room (Mother Succ-U-Bus)
    # ---------------------------------------------------------------- #
    # POI/Visit
    "Bilge Room - Succ-U-Bus (Mother)": STLocationData("Bilge Room"),
    "Bilge Room - Visited": STLocationData("Bilge Room"),
    # Item
    "Bilge Room - Blue Fuse": STLocationData("Bilge Room"),
    "Bilge Room - Titania's Olfactory Center": STLocationData("Bilge Room"),
    # Puzzle
    # Other

    # ---------------------------------------------------------------- #
    # Titania's Room / Fuse Box / Bomb Room
    # ---------------------------------------------------------------- #
    # POI/Visit
    "Bomb Room - Succ-U-Bus": STLocationData("Titania's Room"),
    "Titania's Room - Visited": STLocationData("Titania's Room"),
    # Item
    # Puzzle
    "Titania's Room - Repair Titania": STLocationData("Titania's Room", "Titania Repaired"),
    # Other

    # ---------------------------------------------------------------- #
    # Creator's Chamber
    # ---------------------------------------------------------------- #
    # POI/Visit
    "Creator's Chamber - Succ-U-Bus": STLocationData("Creator's Chamber"),
    "Creator's Chamber - Visited": STLocationData("Creator's Chamber"),
    # Item
    # Puzzle
    # Other

    # ---------------------------------------------------------------- #
    # Sculpture Chamber
    # ---------------------------------------------------------------- #
    # POI/Visit
    "Sculpture Chamber - Succ-U-Bus": STLocationData("Sculpture Chamber"),
    "Sculpture Chamber - Visited": STLocationData("Sculpture Chamber"),
    # Item
    # Puzzle
    # Other

    # ---------------------------------------------------------------- #
    # SGT Class Stateroom/Lobby
    # ---------------------------------------------------------------- #
    # POI/Visit
    "SGT Class Lobby - Succ-U-Bus": STLocationData("SGT Class Lobby"),
    "SGT Class Lobby - Visited": STLocationData("SGT Class Lobby"),
    "SGT Class Stateroom - Visited": STLocationData("SGT Class Stateroom"),
    # Item
    "SGT Class Lobby - Magazine": STLocationData("SGT Class Lobby"),
    "SGT Class Lobby - Long Stick": STLocationData("SGT Class Lobby"),
    # Puzzle
    # Other

    # ---------------------------------------------------------------- #
    # 2nd Class Stateroom/Lobby
    # ---------------------------------------------------------------- #
    # POI/Visit
    "2nd Class Lobby - Succ-U-Bus": STLocationData("2nd Class Lobby"),
    "2nd Class Lobby - Visited": STLocationData("2nd Class Lobby"),
    "2nd Class Stateroom - Succ-U-Bus": STLocationData("2nd Class Lobby"),
    "2nd Class Stateroom - Visited": STLocationData("2nd Class Stateroom"),
    # Item
    "2nd Class Stateroom - Titania's Ear (Pistachio Bowl)": STLocationData("2nd Class Stateroom"),
    # Puzzle
    # Other

    # ---------------------------------------------------------------- #
    # Bottom of the Well
    # ---------------------------------------------------------------- #
    # POI/Visit
    "Bottom of the Well - Succ-U-Bus": STLocationData("Bottom of the Well"),
    "Bottom of the Well - Visited": STLocationData("Bottom of the Well"),
    # Item
    "Bottom of the Well - LiftBot Head": STLocationData("Bottom of the Well"),
    "Bottom of the Well - Crushed Television": STLocationData("Bottom of the Well"),
    # Puzzle
    # Other
    
    # ---------------------------------------------------------------- #
    # Broken Elevator
    # ---------------------------------------------------------------- #
    # POI/Visit
    # Item
    "Broken Elevator - Titania's Eye (Elevator)": STLocationData("Bottom of the Well"),
    # Puzzle
    # Other

    # ---------------------------------------------------------------- #
    # 1st Class Stateroom/Lobby
    # ---------------------------------------------------------------- #
    # POI/Visit
    "1st Class Stateroom - Succ-U-Bus": STLocationData("1st Class Lobby"),
    "1st Class Stateroom - Visited": STLocationData("1st Class Stateroom"),
    "1st Class Lobby - Succ-U-Bus": STLocationData("1st Class Lobby"),
    "1st Class Lobby - Visited": STLocationData("1st Class Lobby"),
    # Item
    "1st Class Stateroom - Titania's Eye (Light)": STLocationData("1st Class Lobby"),
    # Puzzle
    # Other

    # ---------------------------------------------------------------- #
    # Promenade Deck
    # ---------------------------------------------------------------- #
    # POI/Visit
    "Promenade Deck - Succ-U-Bus": STLocationData("Promenade Deck"),
    "Promenade Deck - Visited": STLocationData("Promenade Deck"),
    # Item
    "Promenade Deck - Hammer": STLocationData("Promenade Deck"),
    # "Promenade Deck - Pureed Starlings": STLocationData("Promenade Deck"),
    # Puzzle
    # Other

    # ---------------------------------------------------------------- #
    # Arboretum
    # ---------------------------------------------------------------- #
    # POI/Visit
    "Arboretum - Succ-U-Bus": STLocationData("Arboretum"),
    "Arboretum - Visited": STLocationData("Arboretum"),
    # Item
    "Arboretum - Lemon": STLocationData("Arboretum"),
    "Arboretum - Hose": STLocationData("Arboretum"),
    "Arboretum - Titania's Speech Center": STLocationData("Arboretum"),
    "Arboretum - Titania's Mouth": STLocationData("Arboretum"),
    # Puzzle
    # Other

    # ---------------------------------------------------------------- #
    # Bar
    # ---------------------------------------------------------------- #
    # POI/Visit
    "Bar - Succ-U-Bus": STLocationData("Bar"),
    "Bar - Visited": STLocationData("Bar"),
    # Item
    "Bar - Titania's Vision Center": STLocationData("Bar", "Titania's Vision Center"),
    # Puzzle
    # Other

    # ---------------------------------------------------------------- #
    # Music Room
    # ---------------------------------------------------------------- #
    # POI/Visit
    "Music Room - Succ-U-Bus": STLocationData("Music Room"),
    "Music Room - Visited": STLocationData("Music Room"),
    # Item
    "Music Room - Titania's Ear (Phonograph)": STLocationData("Music Room"),
    # Puzzle
    # Other

    # ---------------------------------------------------------------- #
    # 1st Class Restaurant
    # ---------------------------------------------------------------- #
    # POI/Visit
    "1st Class Restaurant - Succ-U-Bus": STLocationData("1st Class Restaurant"),
    "1st Class Restaurant - Visited": STLocationData("1st Class Restaurant"),
    # Item
    "1st Class Restaurant - Maitre'D Bot's Left Arm": STLocationData("1st Class Restaurant"),
    "1st Class Restaurant - Napkin": STLocationData("1st Class Restaurant"),
    "1st Class Restaurant - Green Fuse": STLocationData("1st Class Restaurant"),
    "1st Class Restaurant - Maitre'D Bot's Right Arm": STLocationData("1st Class Restaurant"),
    "1st Class Restaurant - Titania's Auditory Center": STLocationData("1st Class Restaurant", "Titania's Auditory Center"),
    # Puzzle
    # Other

    # ---------------------------------------------------------------- #
    # Bridge / Ending
    # ---------------------------------------------------------------- #
    # POI/Visit
    "Bridge - Visited": STLocationData("Bridge"),
    # Item
    # Puzzle
    "The End - Return Home": STLocationData("Bridge", "Victory"),
    # Other
}

location_name_to_id: Dict[str, int] = {
    name: LOCATION_ID_BASE + data.offset for name, data in location_table.items()
}

event_location_names = {name for name, data in location_table.items() if data.event_item is not None}
