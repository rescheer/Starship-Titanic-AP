"""
Starship Titanic - Player options.
"""
from dataclasses import dataclass
from random import Random
from typing import Dict, List

from Options import Choice, OptionGroup, PerGameCommonOptions, Toggle, Visibility # pyright: ignore[reportMissingImports]


# --------------------------------------------------------------------------
# Game Options (default group)
# --------------------------------------------------------------------------
class Goal(Choice):
    """Return Home: Repair Titania and fly the Starship Titanic back to Earth from the Bridge.
    Repair Titania: Acquire all of Titania's parts and put them back into their correct slots."""
    display_name = "Goal"
    option_return_home = 0
    option_repair_titania = 1
    default = 0

class PassengerClassUpgrades(Choice):
    """Multiworld: Receive Progressive Passenger Class Upgrade items from the multiworld (adds 2 items).
    Vanilla: Upgrade through the DeskBot as in the vanilla game."""
    display_name = "Passenger Class Upgrades"
    option_multiworld = 0
    option_vanilla = 1
    default = 0


class StateroomAssignment(Choice):
    """Multiworld: Receive Progressive Stateroom items from the multiworld (adds 3 items).
    Vanilla: The DeskBot assigns them as in the vanilla game."""
    display_name = "Stateroom Assignment"
    option_multiworld = 0
    option_vanilla = 1
    default = 0


class ItemDeliveryMethod(Choice):
    """Succubus: Incoming carryable items must be picked up at any Succ-U-Bus on the ship.
    Direct To Inventory: Incoming carryable items are inserted directly into your inventory upon being received."""
    display_name = "Item Delivery Method"
    option_succubus = 0
    option_direct_to_inventory = 1
    default = 0


class EasyMusicRoomPuzzle(Toggle):
    """Sets the Music Room panel's controls to the correct settings upon entering the room."""
    display_name = "Easy Music Room Puzzle"


class RoomAccessCardsMode(Choice):
    """Enabled: Requires a matching Access Card item from the multiworld to access any room.
    Randomized: Chooses a random set of Access Cards to enable at generation (minimum 1).
    Selection: Enables only the Access Cards selected below.
    Disabled: Room Access Cards are disabled. (Vanilla behavior)"""
    display_name = "Room Access Cards Mode"
    option_enabled = 0
    option_randomized = 1
    option_selection = 2
    option_disabled = 3
    default = 3


class RequireAccessCardsAllRooms(Toggle):
    """Deprecated: use Room Access Cards Mode instead. When true, forces Room Access Cards Mode to Enabled so
    older YAMLs keep their behavior."""
    display_name = "Require Access Cards for All Rooms (Deprecated)"
    visibility = Visibility.none

# --------------------------------------------------------------------------
# Access Cards (Selection)
# --------------------------------------------------------------------------
class RequireAccessCardTopOfTheWell(Toggle):
    """Require Access Card item from the multiworld to access the Top of the Well. Ignored unless Room Access 
    Cards Mode is Selection."""
    display_name = "Require Access Card for Top of the Well"


class RequireAccessCardBottomOfTheWell(Toggle):
    """Require Access Card item from the multiworld to access the Bottom of the Well. Ignored unless Room Access 
    Cards Mode is Selection."""
    display_name = "Require Access Card for Bottom of the Well"


class RequireAccessCardParrotLobby(Toggle):
    """Require Access Card item from the multiworld to access the Parrot Lobby. Ignored unless Room Access 
    Cards Mode is Selection."""
    display_name = "Require Access Card for Parrot Lobby"


class RequireAccessCardBilgeRoom(Toggle):
    """Require Access Card item from the multiworld to access the Bilge Room. Ignored unless Room Access 
    Cards Mode is Selection."""
    display_name = "Require Access Card for Bilge Room"


class RequireAccessCardTitaniasRoom(Toggle):
    # Currently disabled due to possible softlock
    """Require Access Card item from the multiworld to access Titania's Room. Ignored unless Room Access 
    Cards Mode is Selection."""
    display_name = "Require Access Card for Titania's Room"


class RequireAccessCardCreatorsChamber(Toggle):
    """Require Access Card item from the multiworld to access the Creator's Chamber. Ignored unless Room Access 
    Cards Mode is Selection."""
    display_name = "Require Access Card for Creator's Chamber"


class RequireAccessCardSculptureChamber(Toggle):
    """Require Access Card item from the multiworld to access the Sculpture Chamber. Ignored unless Room Access 
    Cards Mode is Selection."""
    display_name = "Require Access Card for Sculpture Chamber"


class RequireAccessCardPromenadeDeck(Toggle):
    """Require Access Card item from the multiworld to access the Promenade Deck. Ignored unless Room Access 
    Cards Mode is Selection."""
    display_name = "Require Access Card for Promenade Deck"


class RequireAccessCardArboretum(Toggle):
    """Require Access Card item from the multiworld to access the Arboretum. Ignored unless Room Access 
    Cards Mode is Selection."""
    display_name = "Require Access Card for Arboretum"


class RequireAccessCardBar(Toggle):
    """Require Access Card item from the multiworld to access the Bar. Ignored unless Room Access 
    Cards Mode is Selection."""
    display_name = "Require Access Card for Bar"


class RequireAccessCardMusicRoom(Toggle):
    """Require Access Card item from the multiworld to access the Music Room. Ignored unless Room Access 
    Cards Mode is Selection."""
    display_name = "Require Access Card for Music Room"


class RequireAccessCard1stClassRestaurant(Toggle):
    """Require Access Card item from the multiworld to access the 1st Class Restaurant. Ignored unless Room Access 
    Cards Mode is Selection."""
    display_name = "Require Access Card for 1st Class Restaurant"


class RequireAccessCardBridge(Toggle):
    """Require Access Card item from the multiworld to access the Bridge. Ignored unless Room Access 
    Cards Mode is Selection."""
    display_name = "Require Access Card for Bridge"


class RequireAccessCardSGTClassStaterooms(Toggle):
    """Require Access Card item from the multiworld to access the SGT Class Staterooms. Ignored unless Room Access 
    Cards Mode is Selection."""
    display_name = "Require Access Card for SGT Class Staterooms"


class RequireAccessCard2ndClassStaterooms(Toggle):
    """Require Access Card item from the multiworld to access the 2nd Class Staterooms. Ignored unless Room Access 
    Cards Mode is Selection."""
    display_name = "Require Access Card for 2nd Class Staterooms"


class RequireAccessCard1stClassStaterooms(Toggle):
    """Require Access Card item from the multiworld to access the 1st Class Staterooms. Ignored unless Room Access 
    Cards Mode is Selection."""
    display_name = "Require Access Card for 1st Class Staterooms"


@dataclass
class StarshipTitanicOptions(PerGameCommonOptions):
    goal: Goal
    passenger_class_upgrades: PassengerClassUpgrades
    stateroom_assignment: StateroomAssignment
    item_delivery_method: ItemDeliveryMethod
    easy_music_room_puzzle: EasyMusicRoomPuzzle
    room_access_cards_mode: RoomAccessCardsMode
    require_access_cards_all_rooms: RequireAccessCardsAllRooms
    require_access_card_top_of_the_well: RequireAccessCardTopOfTheWell
    require_access_card_bottom_of_the_well: RequireAccessCardBottomOfTheWell
    require_access_card_parrot_lobby: RequireAccessCardParrotLobby
    require_access_card_bilge_room: RequireAccessCardBilgeRoom
#   require_access_card_titanias_room: RequireAccessCardTitaniasRoom
    require_access_card_creators_chamber: RequireAccessCardCreatorsChamber
    require_access_card_sculpture_chamber: RequireAccessCardSculptureChamber
    require_access_card_promenade_deck: RequireAccessCardPromenadeDeck
    require_access_card_arboretum: RequireAccessCardArboretum
    require_access_card_bar: RequireAccessCardBar
    require_access_card_music_room: RequireAccessCardMusicRoom
    require_access_card_1st_class_restaurant: RequireAccessCard1stClassRestaurant
    require_access_card_bridge: RequireAccessCardBridge
    require_access_card_sgt_class_staterooms: RequireAccessCardSGTClassStaterooms
    require_access_card_2nd_class_staterooms: RequireAccessCard2ndClassStaterooms
    require_access_card_1st_class_staterooms: RequireAccessCard1stClassStaterooms


# Access Card item (items.py) -> the option field that requires it
ACCESS_CARD_OPTIONS: Dict[str, str] = {
    "Top of the Well Access Card": "require_access_card_top_of_the_well",
    "Bottom of the Well Access Card": "require_access_card_bottom_of_the_well",
    "Parrot Lobby Access Card": "require_access_card_parrot_lobby",
    "Bilge Room Access Card": "require_access_card_bilge_room",
#   "Titania's Room Access Card": "require_access_card_titanias_room",
    "Creator's Chamber Access Card": "require_access_card_creators_chamber",
    "Sculpture Chamber Access Card": "require_access_card_sculpture_chamber",
    "Promenade Deck Access Card": "require_access_card_promenade_deck",
    "Arboretum Access Card": "require_access_card_arboretum",
    "Bar Access Card": "require_access_card_bar",
    "Music Room Access Card": "require_access_card_music_room",
    "1st Class Restaurant Access Card": "require_access_card_1st_class_restaurant",
    "Bridge Access Card": "require_access_card_bridge",
    "SGT Class Staterooms Access Card": "require_access_card_sgt_class_staterooms",
    "2nd Class Staterooms Access Card": "require_access_card_2nd_class_staterooms",
    "1st Class Staterooms Access Card": "require_access_card_1st_class_staterooms",
}


def enabled_access_cards(options: StarshipTitanicOptions, random: Random) -> List[str]:
    """The Access Cards this slot requires, in items.py order."""
    candidates = list(ACCESS_CARD_OPTIONS)
    # The Repair Titania goal completes before the Bridge opens, so its card would be useless
    if options.goal == Goal.option_repair_titania:
        candidates.remove("Bridge Access Card")

    mode = options.room_access_cards_mode.value
    if options.require_access_cards_all_rooms:
        mode = RoomAccessCardsMode.option_enabled
    if mode == RoomAccessCardsMode.option_enabled:
        return candidates
    if mode == RoomAccessCardsMode.option_randomized:
        cards = [card for card in candidates if random.random() < 0.5]
        # Always require at least one card
        if not cards:
            cards = [random.choice(candidates)]
        return cards
    if mode == RoomAccessCardsMode.option_selection:
        return [card for card in candidates if getattr(options, ACCESS_CARD_OPTIONS[card])]
    return []


starship_titanic_option_groups = [
    OptionGroup("Access Cards (Selection)", [
        RequireAccessCardTopOfTheWell,
        RequireAccessCardBottomOfTheWell,
        RequireAccessCardParrotLobby,
        RequireAccessCardBilgeRoom,
#       RequireAccessCardTitaniasRoom,
        RequireAccessCardCreatorsChamber,
        RequireAccessCardSculptureChamber,
        RequireAccessCardPromenadeDeck,
        RequireAccessCardArboretum,
        RequireAccessCardBar,
        RequireAccessCardMusicRoom,
        RequireAccessCard1stClassRestaurant,
        RequireAccessCardBridge,
        RequireAccessCardSGTClassStaterooms,
        RequireAccessCard2ndClassStaterooms,
        RequireAccessCard1stClassStaterooms,
    ]),
]
