"""
Starship Titanic - Parrot Hunt goal: the parrot's target stateroom and its clues
"""
from random import Random
from typing import Dict, List

CLUE_PARTS = ["elevator", "floor", "room"]

PARROT_HUNT_REMOVED_LOCATIONS = {
    "Parrot Lobby - Feather",
    "Parrot Lobby - Titania's Core",
    "2nd Class Stateroom - Titania's Ear (Pistachio Bowl)",
}

PARROT_HUNT_GOAL_LOCATION = "Parrot Hunt - Release the Parrot"
PARROT_RELEASED_EVENT = "Parrot Released"

STATEROOM_REGIONS: Dict[int, str] = {
    1: "1st Class Stateroom",
    2: "2nd Class Stateroom",
}


def valid_rooms(passenger_class: int) -> List[Dict[str, int]]:
    """Every stateroom of a class. 1st Class: floors 2-19, rooms 1-3. 2nd Class: floors 20-27, rooms 1-3 
    for an odd elevator or 1-4 for an even one. Elevator 4's rooms are reachable from elevator 3's
    side of the lobby while it's broken."""
    rooms = []
    if passenger_class == 1:
        for floor in range(2, 20):
            for elevator in range(1, 5):
                for room in range(1, 4):
                    rooms.append({"elevator": elevator, "floor": floor, "room": room, "class": 1})
    else:
        for floor in range(20, 28):
            for elevator in range(1, 5):
                for room in range(1, 5 if elevator % 2 == 0 else 4):
                    rooms.append({"elevator": elevator, "floor": floor, "room": room, "class": 2})
    return rooms


def choose_room(classes: List[int], random: Random) -> Dict[str, int]:
    """Picks the parrot's stateroom uniformly from every room of the given classes."""
    candidates = [room for passenger_class in classes for room in valid_rooms(passenger_class)]
    return random.choice(candidates)


def clue_thresholds(clues: int) -> List[int]:
    """The Progressive Clue counts at which clues 1-3 are revealed. Aims for 1/3 total, 2/3 total, and all"""
    return [(2 * clues * k + 3) // 6 for k in range(1, 4)]
