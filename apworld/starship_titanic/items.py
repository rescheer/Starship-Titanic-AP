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
    quantity: int = 1  # how many copies of this item go into the pool


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
    "Victory",
}

# Create the table
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

for name, offset in _titania_parts.items():
    item_table[name] = STItemData(ITEM_ID_BASE + offset, ItemClassification.progression, "Titania Part")

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
    "Fuses": {"Blue Fuse", "Green Fuse", "Red Fuse", "Yellow Fuse"},
    "Filler": set(_filler_items.keys()),
    "Traps": set(_trap_items.keys()),
    "Progressive": set(_progressive_items.keys()) | set(_progressive_room_items.keys()),
}

filler_item_names = list(_filler_items.keys())
trap_item_names = list(_trap_items.keys())

PROGRESSIVE_CLASS_UPGRADE = "Progressive Passenger Class Upgrade"
SECOND_CLASS_TIER = 1
FIRST_CLASS_TIER = 2

PROGRESSIVE_STATEROOM = "Progressive Stateroom"
SGT_STATEROOM_TIER = 1
SECOND_STATEROOM_TIER = 2
FIRST_STATEROOM_TIER = 3
