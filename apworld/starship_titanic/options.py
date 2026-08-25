"""
Starship Titanic - Player options.

Deliberately minimal for this first pass. items.py has more real locations
than real items on purpose (see create_items() in __init__.py, which pads
the shortfall with filler), so options that would change either count
(e.g. an "include side content" toggle) are left for a follow-up revision
once the client mod side of this project exists to test against.
"""
from dataclasses import dataclass

from Options import PerGameCommonOptions


@dataclass
class StarshipTitanicOptions(PerGameCommonOptions):
    pass
