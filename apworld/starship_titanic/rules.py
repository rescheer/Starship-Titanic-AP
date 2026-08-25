"""
Starship Titanic - Access rules.

This is the direct translation of the dependency model into Archipelago
logic. Each rule is commented with the walkthrough section it comes from
so it can be re-checked against the source FAQ if the region graph changes.

Changelog vs. the previous revision:
- "2nd Class Upgrade" / "1st Class Upgrade" event checks are gone.
  Region access now reads state.count("Progressive Passenger Class
  Upgrade", player) against SECOND_CLASS_TIER / FIRST_CLASS_TIER.
- "DeskBot - 2nd Class Upgrade" / "DeskBot - 1st Class Upgrade" are
  ordinary locations now. They keep their original in-fiction
  prerequisites (Magazine in hand; already 2nd Class to ask for 1st) as
  location-level rules, even though receiving the actual class-upgrade
  item no longer happens "at" these locations specifically.
- Entrances updated for the new Top of the Well hub and the SGT Class
  Lobby / 2nd Class Lobby / 1st Class Lobby renames.
- "Visited" locations get no extra rule -- reaching the
  region is the whole requirement, which the entrance rules already cover.

Consistency pass fixes (this revision):
- location() calls updated to match locations.py's current key names --
  four had drifted after a rename and would have raised KeyError at
  generation time: "2nd Class Room - Titania's Ear" ->
  "...(Pistachio Bowl)", "Music Room - Titania's Ear" ->
  "...(Phonograph)", "Arboretum (Autumn) - Titania's Speech Center" ->
  "Arboretum - Titania's Speech Center", "Arboretum (Winter) - Titania's
  Mouth" -> "Arboretum - Titania's Mouth" (locations.py dropped the
  season qualifiers from the check names; the season is still what the
  rule itself requires via the Arboretum Working event).
- state.has("Perch", player) -> state.has("Perch (Luggage Tool)", player).
  "Perch" was never a real item name in items.py -- this would also have
  raised KeyError.
- "DeskBot - First Class Upgrade" -> "DeskBot - 1st Class Upgrade" to
  match the rename in locations.py.
"""
from worlds.generic.Rules import set_rule # pyright: ignore[reportMissingImports]

from .items import (
    item_name_groups,
    PROGRESSIVE_CLASS_UPGRADE,
    SECOND_CLASS_TIER,
    FIRST_CLASS_TIER,
)

TITANIA_PARTS = item_name_groups["Titania Parts"]


def set_rules(world) -> None:
    multiworld = world.multiworld
    player = world.player

    def entrance(name: str):
        return multiworld.get_entrance(name, player)

    def location(name: str):
        return multiworld.get_location(name, player)

    def has_class(state, tier: int) -> bool:
        return state.count(PROGRESSIVE_CLASS_UPGRADE, player) >= tier

    # ---------------------------------------------------------------- #
    # Region entrance rules
    # ---------------------------------------------------------------- #

    # 2nd Class content requires one copy of the progressive upgrade.
    set_rule(
        entrance("Top of the Well -> 2nd Class Lobby"),
        lambda state: has_class(state, SECOND_CLASS_TIER),
    )
    set_rule(
        entrance("Top of the Well -> Bottom of the Well"),
        lambda state: has_class(state, SECOND_CLASS_TIER),
    )
    # Broken Elevator additionally needs the LiftBot Head in hand.
    set_rule(
        entrance("Bottom of the Well -> Broken Elevator"),
        lambda state: state.has("LiftBot Head", player),
    )

    # 1st Class content requires both copies of the progressive upgrade.
    set_rule(
        entrance("Top of the Well -> 1st Class Lobby"),
        lambda state: has_class(state, FIRST_CLASS_TIER),
    )

    # Creator's Room needs the Red Fuse removed & turned (E-Mail section).
    set_rule(
        entrance("Titania's Room -> Creator's Room"),
        lambda state: state.has("Red Fuse", player),
    )

    # Chevron Room needs the copied Chevron Code from Channel 4 TV.
    set_rule(
        entrance("1st Class Lobby -> Chevron Room"),
        lambda state: state.has("Chevron Code", player),
    )

    # Bridge only opens once Titania is fully repaired.
    set_rule(
        entrance("Titania's Room -> Bridge"),
        lambda state: state.has("Titania Repaired", player),
    )

    # ---------------------------------------------------------------- #
    # Location rules -- checks that need something beyond "reach the
    # region", per the dependency model.
    # ---------------------------------------------------------------- #

    # DeskBot: giving her the Magazine is what triggers this check at all.
    set_rule(
        location("DeskBot - 2nd Class Upgrade"),
        lambda state: state.has("Magazine", player),
    )
    # DeskBot won't entertain a 1st Class request until you're already
    # 2nd Class.
    set_rule(
        location("DeskBot - 1st Class Upgrade"),
        lambda state: has_class(state, SECOND_CLASS_TIER),
    )

    # Mother/Bilge Room (walkthrough: "Mother") -- both pickups need the Feather.
    set_rule(
        location("Bilge Room - Titania's Olfactory Center"),
        lambda state: state.has("Feather", player),
    )
    set_rule(
        location("Bilge Room - Blue Fuse"),
        lambda state: state.has("Feather", player),
    )

    # Fuse Box: pulling the Yellow Fuse requires the Blue Fuse to be
    # installed and turned first ("Titanic Titillator" / "Parrot's Chicken").
    set_rule(
        location("Fuse Box - Remove the Yellow Fuse"),
        lambda state: state.has("Blue Fuse", player),
    )

    # Fuse Box: Green Fuse must be obtained (1st Class Restaurant reward)
    # before it can be installed to power the Arboretum/RowBots.
    set_rule(
        location("Fuse Box - Install the Green Fuse"),
        lambda state: state.has("Green Fuse", player),
    )

    # Nose ("Don't Touch That") needs the Hose.
    set_rule(
        location("Parrot Lobby - Titania's Nose"),
        lambda state: state.has("Hose", player),
    )

    # Pistachios ear needs both saved Designer Room Numbers to route the
    # Succ-U-Bus deliveries.
    set_rule(
        location("2nd Class Room - Titania's Ear (Pistachio Bowl)"),
        lambda state: state.has("Designer Room Number (Parrot Lobby)", player)
        and state.has("Designer Room Number (Player's Room)", player),
    )

    # Elevator eye needs the LiftBot Head installed.
    set_rule(
        location("Broken Elevator - Titania's Eye (Elevator)"),
        lambda state: state.has("LiftBot Head", player),
    )

    # Chevron eye needs the Chevron Code (redundant with entrance rule,
    # kept explicit in case region granularity changes later).
    set_rule(
        location("Chevron Room - Titania's Eye (Chevron)"),
        lambda state: state.has("Chevron Code", player),
    )

    # Hammer Dispenser needs the luggage Perch to press the button.
    set_rule(
        location("Promenade Deck - Hammer"),
        lambda state: state.has("Perch (Luggage Tool)", player),
    )

    # Long Stick needs the Hammer to break the display case.
    set_rule(
        location("SGT Class Lobby Side Room - Long Stick"),
        lambda state: state.has("Hammer", player),
    )

    # Lemon needs the Long Stick to knock it down.
    set_rule(
        location("Arboretum - Lemon"),
        lambda state: state.has("Long Stick", player),
    )

    # Pureed Starlings need the chicken dispenser broken (Yellow Fuse
    # removed) plus the Promenade fan, which is only reachable in 1st Class.
    set_rule(
        location("SGT Class Restaurant - Pureed Starlings"),
        lambda state: state.has("Yellow Fuse Removed", player) and has_class(state, FIRST_CLASS_TIER),
    )

    # The Titillator drink (Vision Center) needs all three ingredients.
    set_rule(
        location("Bar - Titania's Vision Center"),
        lambda state: state.has("Lemon", player)
        and state.has("Crushed Television", player)
        and state.has("Glass of Pureed Starlings", player),
    )

    # Recording the cylinder is a pure puzzle-solve; the phonograph ear
    # needs a completed recording.
    set_rule(
        location("Music Room - Titania's Ear (Phonograph)"),
        lambda state: state.has("Recorded Cylinder", player),
    )

    # Maitre'D Bot fight needs his loose arm ripped off first.
    set_rule(
        location("1st Class Restaurant - Defeat the Maitre'D Bot"),
        lambda state: state.has("Maitre'D Bot Arm (Loose)", player),
    )

    # Napkin / Green Fuse / key-arm all sit on Scraliontis' table, unlocked
    # by defeating the Maitre'D Bot.
    for loc_name in (
        "1st Class Restaurant - Napkin",
        "1st Class Restaurant - Green Fuse",
        "1st Class Restaurant - Maitre'D Bot's Key Arm",
    ):
        set_rule(location(loc_name), lambda state: state.has("Maitre'D Bot Defeated", player))

    # Auditory Center needs the correct recording, the unlocking key-arm,
    # and the Maitre'D Bot already defeated (so the music system is
    # reachable at all).
    set_rule(
        location("1st Class Restaurant - Titania's Auditory Center"),
        lambda state: state.has("Recorded Cylinder", player)
        and state.has("Maitre'D Bot Arm (Key)", player)
        and state.has("Maitre'D Bot Defeated", player),
    )

    # Speech Center (Autumn Arboretum) needs the Arboretum powered AND the
    # Long Stick -- the latter is the "hidden" cross-quest dependency the
    # walkthrough doesn't call out explicitly (see logic-model doc section 5).
    set_rule(
        location("Arboretum - Titania's Speech Center"),
        lambda state: state.has("Arboretum Working", player) and state.has("Long Stick", player),
    )

    # Mouth (Winter Arboretum / RowBot) needs the Arboretum powered and
    # BOTH Maitre'D Bot arms free in hand simultaneously -- which in
    # practice means the whole Making Music chain must be finished first
    # (the key-arm has to have been used and returned).
    set_rule(
        location("Arboretum - Titania's Mouth"),
        lambda state: state.has("Arboretum Working", player)
        and state.has("Maitre'D Bot Arm (Loose)", player)
        and state.has("Maitre'D Bot Arm (Key)", player),
    )

    # Parrot's Chicken (Core) needs the Napkin, the chicken dispenser
    # broken, the Parrot Lobby routing number, and First Class access
    # (per the walkthrough's stated section requirement).
    set_rule(
        location("SGT Class Restaurant - Titania's Core"),
        lambda state: state.has("Napkin", player)
        and state.has("Yellow Fuse Removed", player)
        and state.has("Designer Room Number (Parrot Lobby)", player)
        and has_class(state, FIRST_CLASS_TIER),
    )

    # Titania Repaired: all eleven parts.
    set_rule(
        location("Titania's Room - Assemble Titania"),
        lambda state: state.has_all(TITANIA_PARTS, player),
    )

    # Victory: Titania Repaired is already required to reach the Bridge
    # region via the entrance rule above; no further items needed to
    # trigger the ending puzzle itself (pure skill puzzle, see logic
    # model section 7). Restated here for clarity/robustness.
    set_rule(
        location("Bridge - Set Course for Home"),
        lambda state: state.has("Titania Repaired", player),
    )

    multiworld.completion_condition[player] = lambda state: state.has("Victory", player)
