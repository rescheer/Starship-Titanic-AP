"""
Starship Titanic - Player options.
"""
from dataclasses import dataclass

from Options import PerGameCommonOptions # pyright: ignore[reportMissingImports]


@dataclass
class StarshipTitanicOptions(PerGameCommonOptions):
    pass
