"""
Starship Titanic - Player options.
"""
from dataclasses import dataclass
from typing import Dict, List

from Options import OptionGroup, PerGameCommonOptions, Toggle # pyright: ignore[reportMissingImports]


# --------------------------------------------------------------------------
# Access Cards
# --------------------------------------------------------------------------
class RequireAccessCardsAllRooms(Toggle):
    """Require Access Card item from the multiworld to access each room. Overrides the individual room settings
    when true."""
    display_name = "Require Access Cards for All Rooms"


class RequireAccessCardTopOfTheWell(Toggle):
    """Require Access Card item from the multiworld to access the Top of the Well. If "Require Access Cards for All Rooms"
    is true, this setting is ignored."""
    display_name = "Require Access Card for Top of the Well"


class RequireAccessCardBottomOfTheWell(Toggle):
    """Require Access Card item from the multiworld to access the Bottom of the Well. If "Require Access Cards for All Rooms"
    is true, this setting is ignored."""
    display_name = "Require Access Card for Bottom of the Well"


class RequireAccessCardParrotLobby(Toggle):
    """Require Access Card item from the multiworld to access the Parrot Lobby. If "Require Access Cards for All Rooms"
    is true, this setting is ignored."""
    display_name = "Require Access Card for Parrot Lobby"


class RequireAccessCardBilgeRoom(Toggle):
    """Require Access Card item from the multiworld to access the Bilge Room. If "Require Access Cards for All Rooms"
    is true, this setting is ignored."""
    display_name = "Require Access Card for Bilge Room"


class RequireAccessCardTitaniasRoom(Toggle):
    # Currently disabled due to possible softlock
    """Require Access Card item from the multiworld to access Titania's Room. If "Require Access Cards for All Rooms"
    is true, this setting is ignored."""
    display_name = "Require Access Card for Titania's Room"


class RequireAccessCardCreatorsChamber(Toggle):
    """Require Access Card item from the multiworld to access the Creator's Chamber. If "Require Access Cards for All Rooms"
    is true, this setting is ignored."""
    display_name = "Require Access Card for Creator's Chamber"


class RequireAccessCardSculptureChamber(Toggle):
    """Require Access Card item from the multiworld to access the Sculpture Chamber. If "Require Access Cards for All Rooms"
    is true, this setting is ignored."""
    display_name = "Require Access Card for Sculpture Chamber"


class RequireAccessCardPromenadeDeck(Toggle):
    """Require Access Card item from the multiworld to access the Promenade Deck. If "Require Access Cards for All Rooms"
    is true, this setting is ignored."""
    display_name = "Require Access Card for Promenade Deck"


class RequireAccessCardArboretum(Toggle):
    """Require Access Card item from the multiworld to access the Arboretum. If "Require Access Cards for All Rooms"
    is true, this setting is ignored."""
    display_name = "Require Access Card for Arboretum"


class RequireAccessCardBar(Toggle):
    """Require Access Card item from the multiworld to access the Bar. If "Require Access Cards for All Rooms"
    is true, this setting is ignored."""
    display_name = "Require Access Card for Bar"


class RequireAccessCardMusicRoom(Toggle):
    """Require Access Card item from the multiworld to access the Music Room. If "Require Access Cards for All Rooms"
    is true, this setting is ignored."""
    display_name = "Require Access Card for Music Room"


class RequireAccessCard1stClassRestaurant(Toggle):
    """Require Access Card item from the multiworld to access the 1st Class Restaurant. If "Require Access Cards for All Rooms"
    is true, this setting is ignored."""
    display_name = "Require Access Card for 1st Class Restaurant"


class RequireAccessCardBridge(Toggle):
    """Require Access Card item from the multiworld to access the Bridge. If "Require Access Cards for All Rooms"
    is true, this setting is ignored."""
    display_name = "Require Access Card for Bridge"


class RequireAccessCardSGTClassStaterooms(Toggle):
    """Require Access Card item from the multiworld to access the SGT Class Staterooms. If "Require Access Cards for All Rooms"
    is true, this setting is ignored."""
    display_name = "Require Access Card for SGT Class Staterooms"


class RequireAccessCard2ndClassStaterooms(Toggle):
    """Require Access Card item from the multiworld to access the 2nd Class Staterooms. If "Require Access Cards for All Rooms"
    is true, this setting is ignored."""
    display_name = "Require Access Card for 2nd Class Staterooms"


class RequireAccessCard1stClassStaterooms(Toggle):
    """Require Access Card item from the multiworld to access the 1st Class Staterooms. If "Require Access Cards for All Rooms"
    is true, this setting is ignored."""
    display_name = "Require Access Card for 1st Class Staterooms"


@dataclass
class StarshipTitanicOptions(PerGameCommonOptions):
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


def enabled_access_cards(options: StarshipTitanicOptions) -> List[str]:
    """The Access Cards this slot requires, in items.py order."""
    if options.require_access_cards_all_rooms:
        return list(ACCESS_CARD_OPTIONS)
    return [card for card, field in ACCESS_CARD_OPTIONS.items() if getattr(options, field)]


starship_titanic_option_groups = [
    OptionGroup("Access Cards", [
        RequireAccessCardsAllRooms,
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
