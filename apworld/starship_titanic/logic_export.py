"""
Starship Titanic - Exports a slot's logic into slot data, so the client can work out which checks are in logic without
Archipelago installed. rules.py builds different rules per goal, class mode, stateroom mode and Access Card set, so the
export happens at generation, from exactly the rules this slot got.

Rules are written compactly (the client's LogicTracker.cs reads this format):
    true / false
    ["has", item, count]
    ["all", [item, ...]]
    ["region", region]
    ["and", [rule, ...]]
    ["or", [rule, ...]]
"""
from __future__ import annotations

from typing import TYPE_CHECKING, Any, Dict, List

if TYPE_CHECKING:
    from . import StarshipTitanicWorld


def _compact_rule(data: Dict[str, Any]) -> Any:
    if data.get("options"):
        raise ValueError(f"Starship Titanic: can't export a rule with option filters: {data}")

    name = data["rule"]
    if name in ("And", "Or"):
        return [name.lower(), [_compact_rule(child) for child in data["children"]]]

    args = data["args"]
    if name == "Has":
        return ["has", args["item_name"], args["count"]]
    if name == "HasAll":
        return ["all", list(args["item_names"])]
    if name == "CanReachRegion":
        return ["region", args["region_name"]]
    if name == "True_":
        return True
    if name == "False_":
        return False
    raise ValueError(f"Starship Titanic: can't export rule {name}; add it here and to the client's LogicTracker.cs")


def export_logic(world: StarshipTitanicWorld) -> Dict[str, List[Any]]:
    def rule_for(spot_name: str) -> Any:
        rule = world.logic_rules.get(spot_name)
        return True if rule is None else _compact_rule(rule.to_dict())

    entrances: List[Any] = []
    locations: List[Any] = []
    events: List[Any] = []
    for region in world.multiworld.get_regions(world.player):
        for entrance in region.exits:
            entrances.append([region.name, entrance.connected_region.name, rule_for(entrance.name)])
        for location in region.locations:
            if location.address is not None:
                locations.append([location.name, region.name, rule_for(location.name)])
            else:
                events.append([location.item.name, region.name, rule_for(location.name)])

    return {"entrances": entrances, "locations": locations, "events": events}
