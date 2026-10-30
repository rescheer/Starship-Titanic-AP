"""
Starship Titanic - Item definitions

ID space: 771900000 - 771900999
"""
from typing import Dict, NamedTuple, Optional, Set

from BaseClasses import Item, ItemClassification # pyright: ignore[reportMissingImports]

ITEM_ID_BASE = 771900000


class STItemData(NamedTuple):
    code: Optional[int]
    classification: ItemClassification
    group: str = ""
    quantity: int = 1


class StarshipTitanicItem(Item):
    game: str = "Starship Titanic"


# --------------------------------------------------------------------------
# Progression items
# --------------------------------------------------------------------------
_progression_items: Dict[str, int] = {
    "Feather": 0,
    "Magazine": 1,
    "LiftBot Head": 2,
    "Perch": 3,
    "Hammer": 4,
    "Long Stick": 5,
    "Lemon": 6,
    "Crushed TV": 7,
    "Bar Glass": 8,
    "Hose": 9,
    "Napkin": 10,
    "Maitre'D Bot's Left Arm": 11,
    "Maitre'D Bot's Right Arm": 12,
    "Red Fuse": 13,
    "Blue Fuse": 14,
    "Green Fuse": 15,
    "Yellow Fuse": 16,
    "Restaurant Table Reservation": 17,
}

# --------------------------------------------------------------------------
# Progressive Stateroom Assignment
# --------------------------------------------------------------------------
_progressive_room_items: Dict[str, int] = {
    "Progressive Stateroom": 18,
}
_progressive_room_quantities: Dict[str, int] = {
    "Progressive Stateroom": 3,
}

# --------------------------------------------------------------------------
# Progressive Clue - Parrot Hunt goal only. quantity comes from the Parrot Hunt Clues option
# --------------------------------------------------------------------------
_progressive_clue_items: Dict[str, int] = {
    "Progressive Clue": 46,
}

# --------------------------------------------------------------------------
# Class Upgrade Records - locked to the DeskBot's upgrade checks under vanilla class upgrades (locations.py), never
# in the pool. Otherwise these names are plain events with no ID (create_event)
# --------------------------------------------------------------------------
_class_upgrade_records: Dict[str, int] = {
    "2nd Class Upgrade": 47,
    "1st Class Upgrade": 48,
}

# --------------------------------------------------------------------------
# Titania Parts
# --------------------------------------------------------------------------
_titania_parts: Dict[str, int] = {
    "Titania's Eye (Elevator)": 20,
    "Titania's Eye (Light)": 21,
    "Titania's Ear (Pistachio Bowl)": 22,
    "Titania's Ear (Phonograph)": 23,
    "Titania's Nose": 24,
    "Titania's Mouth": 25,
    "Titania's Core": 26,
    "Titania's Olfactory Center": 27,
    "Titania's Speech Center": 28,
    "Titania's Vision Center": 29,
}

# --------------------------------------------------------------------------
# Progressive Passenger Class Upgrade
# --------------------------------------------------------------------------
_progressive_items: Dict[str, int] = {
    "Progressive Passenger Class Upgrade": 19,
}
_progressive_quantities: Dict[str, int] = {
    "Progressive Passenger Class Upgrade": 2,
}

# --------------------------------------------------------------------------
# Access Cards
# --------------------------------------------------------------------------
_access_card_items: Dict[str, int] = {
    "Top of the Well Access Card": 30,
    "Bottom of the Well Access Card": 31,
    "Parrot Lobby Access Card": 32,
    "Bilge Room Access Card": 33,
    "Titania's Room Access Card": 34,
    "Creator's Chamber Access Card": 35,
    "Sculpture Chamber Access Card": 36,
    "Promenade Deck Access Card": 37,
    "Arboretum Access Card": 38,
    "Bar Access Card": 39,
    "Music Room Access Card": 40,
    "1st Class Restaurant Access Card": 41,
    "Bridge Access Card": 42,
    "SGT Class Staterooms Access Card": 43,
    "2nd Class Staterooms Access Card": 44,
    "1st Class Staterooms Access Card": 45,
}

# region (regions.py) -> the Access Card needed to enter it. Each class's Lobby and Stateroom share a card
REGION_ACCESS_CARDS: Dict[str, str] = {
    "Top of the Well": "Top of the Well Access Card",
    "Bottom of the Well": "Bottom of the Well Access Card",
    "Parrot Lobby": "Parrot Lobby Access Card",
    "Bilge Room": "Bilge Room Access Card",
    "Titania's Room": "Titania's Room Access Card",
    "Creator's Chamber": "Creator's Chamber Access Card",
    "Sculpture Chamber": "Sculpture Chamber Access Card",
    "Promenade Deck": "Promenade Deck Access Card",
    "Arboretum": "Arboretum Access Card",
    "Bar": "Bar Access Card",
    "Music Room": "Music Room Access Card",
    "1st Class Restaurant": "1st Class Restaurant Access Card",
    "Bridge": "Bridge Access Card",
    "SGT Class Lobby": "SGT Class Staterooms Access Card",
    "SGT Class Stateroom": "SGT Class Staterooms Access Card",
    "2nd Class Lobby": "2nd Class Staterooms Access Card",
    "2nd Class Stateroom": "2nd Class Staterooms Access Card",
    "1st Class Lobby": "1st Class Staterooms Access Card",
    "1st Class Stateroom": "1st Class Staterooms Access Card",
}

# --------------------------------------------------------------------------
# Useful
# --------------------------------------------------------------------------
_useful_items: Dict[str, int] = {}

# --------------------------------------------------------------------------
# Filler
# --------------------------------------------------------------------------
_filler_items: Dict[str, int] = {
    "Bar Snacks": 50,
    "Polite Passenger Comment Card": 51,
    "Ship-Themed Snow Globe": 52,
    "SGT Class Chocolate": 53,
    "Bomb Taunt Transcript": 54,
}

# --------------------------------------------------------------------------
# Traps
# --------------------------------------------------------------------------
_trap_items: Dict[str, int] = {
    "Disposition Trap": 60,
    "Cellpoint Trap": 61,
    "Parrot Trap": 62,
}

# --------------------------------------------------------------------------
# Events
# --------------------------------------------------------------------------
_event_items: Set[str] = {
    "Titania's Auditory Center",
    "Titania Repaired",
    "Parrot Released",
    "Victory",
}

item_table: Dict[str, STItemData] = {}

for name, offset in _progression_items.items():
    item_table[name] = STItemData(ITEM_ID_BASE + offset, ItemClassification.progression)

for name, offset in _progressive_items.items():
    item_table[name] = STItemData(
        ITEM_ID_BASE + offset,
        ItemClassification.progression,
        "Progressive",
        quantity=_progressive_quantities[name],
    )

for name, offset in _progressive_room_items.items():
    item_table[name] = STItemData(
        ITEM_ID_BASE + offset,
        ItemClassification.progression,
        "Progressive",
        quantity=_progressive_room_quantities[name],
    )

for name, offset in _progressive_clue_items.items():
    item_table[name] = STItemData(ITEM_ID_BASE + offset, ItemClassification.progression, "Progressive", quantity=0)

for name, offset in _class_upgrade_records.items():
    item_table[name] = STItemData(ITEM_ID_BASE + offset, ItemClassification.progression, quantity=0)

for name, offset in _titania_parts.items():
    item_table[name] = STItemData(ITEM_ID_BASE + offset, ItemClassification.progression, "Titania Part")

for name, offset in _access_card_items.items():
    item_table[name] = STItemData(ITEM_ID_BASE + offset, ItemClassification.progression, "Access Card")

for name, offset in _useful_items.items():
    item_table[name] = STItemData(ITEM_ID_BASE + offset, ItemClassification.useful)

for name, offset in _filler_items.items():
    item_table[name] = STItemData(ITEM_ID_BASE + offset, ItemClassification.filler)

for name, offset in _trap_items.items():
    item_table[name] = STItemData(ITEM_ID_BASE + offset, ItemClassification.trap)

for name in _event_items:
    item_table[name] = STItemData(None, ItemClassification.progression)

item_name_to_id: Dict[str, int] = {
    name: data.code for name, data in item_table.items() if data.code is not None
}

item_name_groups: Dict[str, Set[str]] = {
    "Titania Parts": set(_titania_parts.keys()),
    "Access Cards": set(_access_card_items.keys()),
    "Fuses": {"Blue Fuse", "Green Fuse", "Red Fuse", "Yellow Fuse"},
    "Filler": set(_filler_items.keys()),
    "Traps": set(_trap_items.keys()),
    "Progressive": set(_progressive_items.keys()) | set(_progressive_room_items.keys())
                   | set(_progressive_clue_items.keys()),
}

filler_item_names = list(_filler_items.keys())
trap_item_names = list(_trap_items.keys())

PROGRESSIVE_CLASS_UPGRADE = "Progressive Passenger Class Upgrade"
SECOND_CLASS_TIER = 1
FIRST_CLASS_TIER = 2

SECOND_CLASS_UPGRADE_EVENT = "2nd Class Upgrade"
FIRST_CLASS_UPGRADE_EVENT = "1st Class Upgrade"

PROGRESSIVE_CLUE = "Progressive Clue"

PROGRESSIVE_STATEROOM = "Progressive Stateroom"
SGT_STATEROOM_TIER = 1
SECOND_STATEROOM_TIER = 2
FIRST_STATEROOM_TIER = 3
