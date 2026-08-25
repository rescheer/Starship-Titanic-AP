"""
Starship Titanic - Item definitions.

ID space: 771900000 - 771900999 (reserved arbitrarily for this fan project;
change to a real registered base before publishing if this game is ever
formally onboarded to Archipelago).
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
# Progression items -- physical objects the player finds/carries in-game.
# These are the real dependency backbone described in the logic model.
# --------------------------------------------------------------------------
_progression_items: Dict[str, int] = {
    "Feather": 0,
    "Magazine": 1,
    "LiftBot Head": 2,
    "Perch (Luggage Tool)": 3,
    "Hammer": 4,
    "Long Stick": 5,
    "Lemon": 6,
    "Crushed Television": 7,
    "Glass of Pureed Starlings": 8,
    "Hose": 9,
    "Napkin": 10,
    "Maitre'D Bot Arm (Loose)": 11,
    "Maitre'D Bot Arm (Key)": 12,
    "Recorded Cylinder": 13,
    "Blue Fuse": 14,
    "Green Fuse": 15,
    "Chevron Code": 16,
    "Designer Room Number (Parrot Lobby)": 17,
    "Designer Room Number (Player's Room)": 18,
}

# --------------------------------------------------------------------------
# Progressive Passenger Class Upgrade
#
# Replaces the old "2nd Class Upgrade" / "1st Class Upgrade" *event*
# items. This is now a real, shuffle-able progression item with two copies
# in the pool:
#   count(state) >= 1  ->  2nd Class granted
#   count(state) >= 2  ->  1st Class granted
#
# NOTE: this changes downstream logic. locations.py no longer needs the
# "DeskBot - 2nd Class Upgrade" / "DeskBot - 1st Class Upgrade" event
# locations to hold locked event items -- they either become normal
# locations (holding any shuffled item, DeskBot just narratively "gives"
# whatever AP placed there), or are dropped if there's no in-fiction
# check to hang them on. rules.py needs its class-gate rules changed from
# state.has("2nd Class Upgrade"/"1st Class Upgrade", player) to:
#   state.count("Progressive Passenger Class Upgrade", player) >= 1  (2nd)
#   state.count("Progressive Passenger Class Upgrade", player) >= 2  (1st)
# --------------------------------------------------------------------------
_progressive_items: Dict[str, int] = {
    "Progressive Passenger Class Upgrade": 19,
}
_progressive_quantities: Dict[str, int] = {
    "Progressive Passenger Class Upgrade": 2,
}

# --------------------------------------------------------------------------
# Titania's eleven parts. Progression, and also form the "Titania Parts"
# item group so rules.py can check state.has_all(group, player) once
# Titania Repaired is evaluated.
# --------------------------------------------------------------------------
_titania_parts: Dict[str, int] = {
    "Titania's Eye (Elevator)": 20,
    "Titania's Eye (Chevron)": 21,
    "Titania's Ear (Pistachio Bowl)": 22,
    "Titania's Ear (Phonograph)": 23,
    "Titania's Nose": 24,
    "Titania's Mouth": 25,
    "Titania's Core": 26,
    "Titania's Olfactory Center": 27,
    "Titania's Auditory Center": 28,
    "Titania's Speech Center": 29,
    "Titania's Vision Center": 30,
}

# --------------------------------------------------------------------------
# Useful (not strictly required, but never harmful to have early)
# --------------------------------------------------------------------------
_useful_items: Dict[str, int] = {
    "Red Fuse": 40,  # unlocks Creator's Room / e-mail side content only
}

# --------------------------------------------------------------------------
# Filler -- flavor pickups with no logic weight. Free to reorder/rename.
# --------------------------------------------------------------------------
_filler_items: Dict[str, int] = {
    "Bar Snacks": 50,
    "Polite Passenger Comment Card": 51,
    "Ship-Themed Snow Globe": 52,
    "SGT Class Chocolate": 53,
    "Bomb Taunt Transcript": 54,
}

# --------------------------------------------------------------------------
# Traps -- negative/comedic filler. Kept small and reversible/non-softlocking
# in line with in-game fiction (disposition/cellpoint dials going into the
# red just makes bots crankier temporarily).
# --------------------------------------------------------------------------
_trap_items: Dict[str, int] = {
    "Disposition Trap": 60,
    "Cellpoint Trap": 61,
}

# --------------------------------------------------------------------------
# Events -- code=None, never placed in the multiworld item pool, always
# locked to their corresponding location by regions.py / rules.py.
#
# "2nd Class Upgrade" and "1st Class Upgrade" have been REMOVED from
# this set -- they're now tiers of the real "Progressive Passenger Class
# Upgrade" item above, not events.
# --------------------------------------------------------------------------
_event_items: Set[str] = {
    "Yellow Fuse Removed",
    "Arboretum Working",
    "Maitre'D Bot Defeated",
    "Titania Repaired",
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
    "Fuses": {"Blue Fuse", "Green Fuse", "Red Fuse"},
    "Filler": set(_filler_items.keys()),
    "Traps": set(_trap_items.keys()),
    "Progressive": set(_progressive_items.keys()),
}

filler_item_names = list(_filler_items.keys())
trap_item_names = list(_trap_items.keys())

# Convenience constants for rules.py / __init__.py so the tier thresholds
# aren't magic numbers scattered across files.
PROGRESSIVE_CLASS_UPGRADE = "Progressive Passenger Class Upgrade"
SECOND_CLASS_TIER = 1
FIRST_CLASS_TIER = 2
