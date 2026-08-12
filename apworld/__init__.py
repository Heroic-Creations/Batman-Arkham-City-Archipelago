from worlds.AutoWorld import World, WebWorld
from worlds.LauncherComponents import Component, Type, components, launch_subprocess
from BaseClasses import Region, LocationProgressType, ItemClassification

from .Items import (
    item_table,
    GATING_GADGETS,
    NON_GATING_GADGETS,
    BASE_BATARANG,
    PROGRESSIVE_UPGRADES,
    SINGLE_UPGRADES,
    COMBAT_MOVE_ITEMS,
    STARTING_KIT_ITEM_NAMES,
    BatmanACItem,
    create_item_classification,
)
from .Locations import location_table, BatmanACLocation
from .Options import BatmanACOptions
from .Rules import (
    LOCATION_REQUIREMENTS,
    LOCATION_ANY_REQUIREMENTS,
    UNKNOWN_LOCATIONS,
)


def run_client() -> None:
    """Launch the Batman: Arkham City client from the Archipelago Launcher."""
    from .BatmanClient import main
    launch_subprocess(main, name="BatmanArkhamCityClient")


components.append(
    Component(
        "Batman: Arkham City Client",
        func=run_client,
        component_type=Type.CLIENT,
    )
)


class BatmanACWeb(WebWorld):
    pass


class BatmanACWorld(World):
    """
    Batman: Arkham City, post-story-completion gadget strip/regrant with
    Riddler Trophies as the check/goal layer.

    247 locations - every Batman-collectable physical Riddler trophy.
    Catwoman's trophies share the same actor class and index space but sit
    above each zone's Batman count; they are excluded, since Batman cannot
    collect them.

    Per-trophy gadget requirements come from Rules.py, derived from a
    walkthrough whose numbering was confirmed in-game to be the game's own
    PickupIndex. The goal is a configurable number of received Riddler
    Trophies.
    """
    game = "Batman: Arkham City"
    options_dataclass = BatmanACOptions
    options: BatmanACOptions

    item_name_to_id = item_table
    location_name_to_id = location_table

    web = BatmanACWeb()

    def create_regions(self) -> None:
        menu = Region("Menu", self.player, self.multiworld)
        self.multiworld.regions.append(menu)

        arkham_city = Region("Arkham City", self.player, self.multiworld)
        arkham_city.add_locations(location_table, BatmanACLocation)
        self.multiworld.regions.append(arkham_city)

        menu.connect(arkham_city)

    def create_items(self) -> None:
        gadget_names = list(GATING_GADGETS.keys()) + list(NON_GATING_GADGETS.keys()) + list(BASE_BATARANG.keys())

        if not self.options.randomize_starting_kit:
            for name in STARTING_KIT_ITEM_NAMES:
                self.multiworld.push_precollected(self.create_item(name))
            gadget_names = [name for name in gadget_names if name not in STARTING_KIT_ITEM_NAMES]

        pool = [self.create_item(name) for name in gadget_names]

        if self.options.randomize_upgrades:
            # One copy per stage for progressive armour, one each otherwise.
            for name, (_id, _flag, stages) in PROGRESSIVE_UPGRADES.items():
                pool += [self.create_item(name) for _ in range(stages)]
            pool += [self.create_item(name) for name in SINGLE_UPGRADES]

        if self.options.randomize_counter:
            pool += [self.create_item(name) for name in COMBAT_MOVE_ITEMS]

        trophy_count = len(location_table) - len(pool)
        goal = self.options.trophy_goal.value

        # Only the trophies actually needed for the goal must be treated as
        # progression - AP guarantees those are reachable. Marking all ~235
        # as progression overwhelms the fill, because logic gates a large
        # share of locations behind gadgets. A buffer above the goal keeps
        # placement flexible without recreating that problem.
        progression_trophies = min(trophy_count, int(goal * 1.25) + 5)

        for i in range(trophy_count):
            item = self.create_item("Riddler Trophy")
            if i >= progression_trophies:
                item.classification = ItemClassification.filler
            pool.append(item)

        self.multiworld.itempool += pool

    def create_item(self, name: str) -> BatmanACItem:
        return BatmanACItem(name, create_item_classification(name), self.item_name_to_id[name], self.player)

    def fill_slot_data(self) -> dict:
        # The client needs the goal count so it can report completion.
        return {
            "trophy_goal": self.options.trophy_goal.value,
            "randomize_counter": bool(self.options.randomize_counter),
            "randomize_upgrades": bool(self.options.randomize_upgrades),
        }

    def set_rules(self) -> None:
        player = self.player

        # Every gadget listed must be held.
        for loc_name, required in LOCATION_REQUIREMENTS.items():
            if not required:
                continue  # reachable with base traversal - no rule needed
            location = self.multiworld.get_location(loc_name, player)
            location.access_rule = (
                lambda state, reqs=tuple(required): state.has_all(reqs, player)
            )

        # Any ONE of the listed gadgets is enough.
        for loc_name, options in LOCATION_ANY_REQUIREMENTS.items():
            location = self.multiworld.get_location(loc_name, player)
            location.access_rule = (
                lambda state, opts=tuple(options): state.has_any(opts, player)
            )

        # Trophies whose requirements we couldn't determine. Rather than
        # guess - a wrong rule is what creates unwinnable seeds - they stay
        # reachable but are barred from holding progression items.
        for loc_name in UNKNOWN_LOCATIONS:
            self.multiworld.get_location(loc_name, player).progress_type = (
                LocationProgressType.EXCLUDED
            )

        self.multiworld.completion_condition[player] = lambda state: state.has(
            "Riddler Trophy", player, self.options.trophy_goal.value
        )
