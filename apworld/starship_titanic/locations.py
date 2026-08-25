"""
Starship Titanic - Location (check) definitions.

ID space: 771901000 - 771901999 (kept separate from item IDs; see items.py
for the ID-base disclaimer).

Each entry: name -> (region it belongs to, id offset, event item name or
None). Locations with an event item are logic-only milestones locked to
that event item; they never hold a real shuffled item.

Changelog vs. the previous revision:
- "DeskBot - 2nd Class Upgrade" / "DeskBot - 1st Class Upgrade" are no
  longer event locations -- Progressive Passenger Class Upgrade (see
  items.py) is now a real shuffled item, so these two checks just hold
  whatever the fill places there like any other location. Region access
  is what now depends on the *count* of that item, not these locations
  directly.
- Added one "Visited" check per region (offsets 200+).
- "SGT Class Lobbys" renamed to "SGT Class Lobby" throughout, to match the
  singular naming used by every other region.
- Added the "Top of the Well" region and its first-visit check.
- Consistency pass: "DeskBot - First Class Upgrade" renamed to
  "DeskBot - 1st Class Upgrade" -- every other "1st Class"/"2nd Class"
  location and region in this table uses the numeral form, and this was
  the one holdout still spelling it out.
"""
from typing import Dict, NamedTuple, Optional

from BaseClasses import Location

LOCATION_ID_BASE = 771901000


class STLocationData(NamedTuple):
    region: str
    offset: int
    event_item: Optional[str] = None


class StarshipTitanicLocation(Location):
    game: str = "Starship Titanic"


location_table: Dict[str, STLocationData] = {
    # ---------------------------------------------------------------- #
    # Embarkation Lobby (SGT hub)
    # ---------------------------------------------------------------- #
    "Embarkation Lobby - Opening Credits": STLocationData("Embarkation Lobby", 0),
    "DeskBot - 2nd Class Upgrade": STLocationData("Embarkation Lobby", 1),
    "DeskBot - 1st Class Upgrade": STLocationData("Embarkation Lobby", 2),

    # ---------------------------------------------------------------- #
    # Parrot Lobby
    # ---------------------------------------------------------------- #
    "Parrot Lobby - Feather": STLocationData("Parrot Lobby", 10),
    "Parrot Lobby - Save Designer Room Number": STLocationData("Parrot Lobby", 11),
    "Parrot Lobby - Perch": STLocationData("Parrot Lobby", 12),
    "Parrot Lobby - Crushed Television": STLocationData("Parrot Lobby", 13),
    "Parrot Lobby - Titania's Nose": STLocationData("Parrot Lobby", 14),

    # ---------------------------------------------------------------- #
    # Bilge Room (Mother Succ-U-Bus)
    # ---------------------------------------------------------------- #
    "Bilge Room - Titania's Olfactory Center": STLocationData("Bilge Room", 20),
    "Bilge Room - Blue Fuse": STLocationData("Bilge Room", 21),

    # ---------------------------------------------------------------- #
    # Titania's Room / Fuse Box
    # ---------------------------------------------------------------- #
    "Fuse Box - Remove the Red Fuse": STLocationData("Titania's Room", 30),
    "Titania's Room - Disarm the Bomb": STLocationData("Titania's Room", 31),
    "Fuse Box - Remove the Yellow Fuse": STLocationData("Titania's Room", 32, "Yellow Fuse Removed"),
    "Fuse Box - Install the Green Fuse": STLocationData("Titania's Room", 33, "Arboretum Working"),
    "Titania's Room - Assemble Titania": STLocationData("Titania's Room", 34, "Titania Repaired"),

    # ---------------------------------------------------------------- #
    # Creator's Room (optional e-mail content, gated by Red Fuse)
    # ---------------------------------------------------------------- #
    "Creator's Room - Leovinus' E-Mail": STLocationData("Creator's Room", 40),
    "Creator's Room - Scraliontis' E-Mail": STLocationData("Creator's Room", 41),
    "Creator's Room - Brobostigon's E-Mail": STLocationData("Creator's Room", 42),

    # ---------------------------------------------------------------- #
    # Sculpture Room
    # ---------------------------------------------------------------- #
    "Sculpture Room - Adjust the BarBot": STLocationData("Sculpture Room", 50),

    # ---------------------------------------------------------------- #
    # SGT Class Lobby (restaurant + small room, generic across 28-38)
    # ---------------------------------------------------------------- #
    "SGT Class Room - Magazine": STLocationData("SGT Class Lobby", 60),
    "SGT Class Lobby - Order a Snack": STLocationData("SGT Class Lobby", 61),
    "SGT Class Lobby Side Room - Long Stick": STLocationData("SGT Class Lobby", 62),
    "SGT Class Restaurant - Pureed Starlings": STLocationData("SGT Class Lobby", 63),
    "SGT Class Restaurant - Titania's Core": STLocationData("SGT Class Lobby", 64),

    # ---------------------------------------------------------------- #
    # 2nd Class Room
    # ---------------------------------------------------------------- #
    "2nd Class Room - Save Designer Room Number": STLocationData("2nd Class Lobby", 70),
    "2nd Class Room - Titania's Ear (Pistachio Bowl)": STLocationData("2nd Class Lobby", 71),

    # ---------------------------------------------------------------- #
    # Bottom of the Well / Broken Elevator
    # ---------------------------------------------------------------- #
    "Bottom of the Well - LiftBot Head": STLocationData("Bottom of the Well", 80),
    "Broken Elevator - Titania's Eye (Elevator)": STLocationData("Broken Elevator", 90),

    # ---------------------------------------------------------------- #
    # 1st Class Room
    # ---------------------------------------------------------------- #
    "1st Class Room - Chevron Code": STLocationData("1st Class Lobby", 100),

    # ---------------------------------------------------------------- #
    # Chevron Room (Floor 7 / Elevator 2 / Room 3)
    # ---------------------------------------------------------------- #
    "Chevron Room - Titania's Eye (Chevron)": STLocationData("Chevron Room", 110),

    # ---------------------------------------------------------------- #
    # Promenade Deck
    # ---------------------------------------------------------------- #
    "Promenade Deck - Hammer": STLocationData("Promenade Deck", 120),

    # ---------------------------------------------------------------- #
    # Arboretum
    # ---------------------------------------------------------------- #
    "Arboretum - Lemon": STLocationData("Arboretum", 130),
    "Arboretum - Hose": STLocationData("Arboretum", 131),
    "Arboretum - Titania's Speech Center": STLocationData("Arboretum", 132),
    "Arboretum - Titania's Mouth": STLocationData("Arboretum", 133),

    # ---------------------------------------------------------------- #
    # Bar
    # ---------------------------------------------------------------- #
    "Bar - Titania's Vision Center": STLocationData("Bar", 140),

    # ---------------------------------------------------------------- #
    # Music Room
    # ---------------------------------------------------------------- #
    "Music Room - Record the Cylinder": STLocationData("Music Room", 150),
    "Music Room - Titania's Ear (Phonograph)": STLocationData("Music Room", 151),

    # ---------------------------------------------------------------- #
    # 1st Class Restaurant
    # ---------------------------------------------------------------- #
    "1st Class Restaurant - Maitre'D Bot's Loose Arm": STLocationData("1st Class Restaurant", 160),
    "1st Class Restaurant - Defeat the Maitre'D Bot": STLocationData(
        "1st Class Restaurant", 161, "Maitre'D Bot Defeated"
    ),
    "1st Class Restaurant - Napkin": STLocationData("1st Class Restaurant", 162),
    "1st Class Restaurant - Green Fuse": STLocationData("1st Class Restaurant", 163),
    "1st Class Restaurant - Maitre'D Bot's Key Arm": STLocationData("1st Class Restaurant", 164),
    "1st Class Restaurant - Titania's Auditory Center": STLocationData("1st Class Restaurant", 165),

    # ---------------------------------------------------------------- #
    # Bridge / Ending
    # ---------------------------------------------------------------- #
    "Bridge - Set Course for Home": STLocationData("Bridge", 170, "Victory"),

    # ---------------------------------------------------------------- #
    # "Visited" -- one per region, no in-fiction item,
    # just a check for reaching that room at all. Access is governed
    # entirely by the region's entrance rule in rules.py, so none of
    # these need a location-level rule of their own.
    # ---------------------------------------------------------------- #
    "Embarkation Lobby - Visited": STLocationData("Embarkation Lobby", 200),
    "Top of the Well - Visited": STLocationData("Top of the Well", 201),
    "Parrot Lobby - Visited": STLocationData("Parrot Lobby", 202),
    "Bilge Room - Visited": STLocationData("Bilge Room", 203),
    "Titania's Room - Visited": STLocationData("Titania's Room", 204),
    "Creator's Room - Visited": STLocationData("Creator's Room", 205),
    "Sculpture Room - Visited": STLocationData("Sculpture Room", 206),
    "SGT Class Lobby - Visited": STLocationData("SGT Class Lobby", 207),
    "2nd Class Lobby - Visited": STLocationData("2nd Class Lobby", 208),
    "Bottom of the Well - Visited": STLocationData("Bottom of the Well", 209),
    "Broken Elevator - Visited": STLocationData("Broken Elevator", 210),
    "1st Class Lobby - Visited": STLocationData("1st Class Lobby", 211),
    "Chevron Room - Visited": STLocationData("Chevron Room", 212),
    "Promenade Deck - Visited": STLocationData("Promenade Deck", 213),
    "Arboretum - Visited": STLocationData("Arboretum", 214),
    "Bar - Visited": STLocationData("Bar", 215),
    "Music Room - Visited": STLocationData("Music Room", 216),
    "1st Class Restaurant - Visited": STLocationData("1st Class Restaurant", 217),
    "Bridge - Visited": STLocationData("Bridge", 218),
}

location_name_to_id: Dict[str, int] = {
    name: LOCATION_ID_BASE + data.offset for name, data in location_table.items()
}

event_location_names = {name for name, data in location_table.items() if data.event_item is not None}
