"""
Batman: Arkham City Archipelago client.

Runs inside Archipelago (launched from ArchipelagoLauncher) and bridges the
AP server to the running game.

The game side is BmSDK's ApBridge.cs, which hosts a plain-text TCP server on
127.0.0.1:7777 inside the game process. Same shape as the Twilight Princess
Dusklight client's socket transport - the game is just another endpoint we
talk to over a socket.

    game  -> PICKUP,<zone>,<type>,<index>    -> LocationChecks to AP
    game  -> GAME_LOADED                     -> re-push authoritative state
    AP    -> ReceivedItems                   -> GRANT_NAMED,<class> to game
    AP    -> PrintJSON                       -> TOAST,<text> to game
"""
import asyncio
import re
import traceback
from typing import Any, Optional

import Utils
from CommonClient import (
    ClientCommandProcessor,
    CommonContext,
    get_base_parser,
    gui_enabled,
    logger,
    server_loop,
)
from NetUtils import ClientStatus, JSONtoTextParser

from .Items import AP_NAME_TO_CLASS_NAME, COMBAT_MOVE_ITEMS, UPGRADE_FLAG_INFO, item_table
from .Locations import location_table

GAME_NAME = "Batman: Arkham City"

BRIDGE_HOST = "127.0.0.1"
BRIDGE_PORT = 7777
BRIDGE_RETRY_SECONDS = 2

ITEM_ID_TO_NAME = {v: k for k, v in item_table.items()}
TROPHY_ITEM_NAME = "Riddler Trophy"

ANSI_ESCAPE_RE = re.compile(r"\x1b\[[0-9;]*m")

STATUS_WAITING = "Waiting for Batman: Arkham City... (start the game and load a save)"
STATUS_CONNECTED = "Connected to Batman: Arkham City."
STATUS_LOST = "Lost connection to the game - waiting for it to come back..."


class BatmanACCommandProcessor(ClientCommandProcessor):
    def _cmd_game(self) -> None:
        """Show whether the client is currently attached to the running game."""
        ctx: "BatmanACContext" = self.ctx
        logger.info(ctx.game_status)
        logger.info(f"Gadgets currently granted: {len(ctx.owned_gadget_classes)}")

    def _cmd_resync(self) -> None:
        """Re-send full state to the game: gadgets, upgrades and counter lock."""
        ctx: "BatmanACContext" = self.ctx
        if not ctx.bridge_writer:
            logger.info("Not connected to the game yet.")
            return
        asyncio.create_task(ctx.push_all_state())
        logger.info("Re-syncing full state with the game (gadgets, upgrades, counter)...")


class BatmanACContext(CommonContext):
    command_processor = BatmanACCommandProcessor
    game: str = GAME_NAME
    items_handling: int = 0b111  # full remote items, including starting inventory

    def __init__(self, server_address: Optional[str], password: Optional[str]) -> None:
        super().__init__(server_address, password)
        self.bridge_reader: Optional[asyncio.StreamReader] = None
        self.bridge_writer: Optional[asyncio.StreamWriter] = None
        self.game_status: str = STATUS_WAITING

        # Authoritative list of gadgets AP has granted. Ordered so restores
        # are deterministic.
        self.owned_gadget_classes: list[str] = []
        # AP item name -> how many copies received. Progressive upgrades
        # (armour) use the count to decide how many stage flags to set.
        self.upgrade_counts: dict[str, int] = {}
        # Experimental counter lock. Only active if the YAML enabled it;
        # otherwise Counter is never locked and this stays False.
        self.counter_randomized: bool = False
        # When AP owns the upgrades, stop the in-game shop selling them too.
        self.upgrades_randomized: bool = False
        self.has_counter: bool = False
        self.trophies_received: int = 0
        self.trophy_goal: Optional[int] = None
        self.goal_sent: bool = False

        self.json_parser = JSONtoTextParser(self)

    async def server_auth(self, password_requested: bool = False) -> None:
        if password_requested and not self.password:
            await super().server_auth(password_requested)
        await self.get_username()
        await self.send_connect()

    def on_package(self, cmd: str, args: dict[str, Any]) -> None:
        if cmd == "Connected":
            slot_data = args.get("slot_data") or {}
            self.trophy_goal = slot_data.get("trophy_goal")
            self.counter_randomized = bool(slot_data.get("randomize_counter"))
            self.upgrades_randomized = bool(slot_data.get("randomize_upgrades"))
            if self.trophy_goal:
                logger.info(f"Goal: receive {self.trophy_goal} Riddler Trophies.")
            # A fresh connection may replay the whole item list, so rebuild
            # owned state from scratch rather than appending to stale data.
            self.owned_gadget_classes = []
            self.upgrade_counts = {}
            self.has_counter = False
            self.trophies_received = 0
            self.goal_sent = False

            # The bridge may already be attached, in which case its
            # connect-time push was skipped because we hadn't joined the
            # server yet. Whichever of the two connects second has to assert
            # state, or nothing is ever sent to the game at all.
            if self.bridge_writer:
                asyncio.create_task(self.push_all_state())

        elif cmd == "ReceivedItems":
            asyncio.create_task(self.handle_received_items(args))

        elif cmd == "PrintJSON":
            asyncio.create_task(self.handle_print_json(args))

    # ---- AP -> game --------------------------------------------------

    async def handle_received_items(self, args: dict[str, Any]) -> None:
        """React to items. CommonContext already maintains self.items_received."""
        newly_granted: list[str] = []
        upgrades_changed = False
        counter_changed = False

        for net_item in args.get("items", []):
            item_name = ITEM_ID_TO_NAME.get(net_item.item)
            if item_name is None:
                continue

            if item_name == TROPHY_ITEM_NAME:
                self.trophies_received += 1
                continue

            if item_name in COMBAT_MOVE_ITEMS:
                self.has_counter = True
                counter_changed = True
                continue

            if item_name in UPGRADE_FLAG_INFO:
                self.upgrade_counts[item_name] = self.upgrade_counts.get(item_name, 0) + 1
                upgrades_changed = True
                continue

            class_name = AP_NAME_TO_CLASS_NAME.get(item_name)
            if class_name is None:
                continue
            if class_name not in self.owned_gadget_classes:
                self.owned_gadget_classes.append(class_name)
                newly_granted.append(class_name)

        # Incremental grants show the in-game popup; bulk restores don't.
        for class_name in newly_granted:
            await self.send_bridge(f"GRANT_NAMED,{class_name}")

        if upgrades_changed:
            await self.push_upgrade_state()

        if counter_changed:
            await self.push_counter_state()

        await self.check_goal()

    async def handle_print_json(self, args: dict[str, Any]) -> None:
        """Mirror relevant multiworld events to the in-game toast widget.

        Only events involving this slot go on screen - in a large multiworld
        most traffic is other players trading between themselves, and that
        would spam the HUD and collide with combat XP messages. The client's
        own log still shows everything.
        """
        if not self.is_relevant_to_me(args):
            return
        try:
            text = self.json_parser(args.get("data", []))
        except Exception:
            return
        # The parser emits terminal colour codes; strip them or they show
        # up as garbage characters in the game's HUD.
        text = ANSI_ESCAPE_RE.sub("", text).replace("\n", " ").strip()
        if text:
            await self.send_bridge(f"TOAST,{text}")

    def is_relevant_to_me(self, args: dict[str, Any]) -> bool:
        ptype = args.get("type")
        if ptype in ("Goal", "Countdown", "Chat", "ServerChat"):
            return True
        if ptype in ("ItemSend", "ItemCheat", "Hint"):
            if args.get("receiving") == self.slot:
                return True
            item = args.get("item")
            return bool(item is not None and getattr(item, "player", None) == self.slot)
        return False

    async def check_goal(self) -> None:
        if self.goal_sent or not self.trophy_goal:
            return
        if self.trophies_received >= self.trophy_goal:
            self.goal_sent = True
            logger.info(f"Goal complete - {self.trophies_received} Riddler Trophies received!")
            await self.send_msgs([{"cmd": "StatusUpdate", "status": ClientStatus.CLIENT_GOAL}])

    def owned_upgrade_flags(self) -> list[str]:
        """Expand received upgrade items into the game's flag suffixes.

        Progressive upgrades map N copies onto stage flags 1..N, e.g. two
        copies of Progressive Ballistic Armour -> BallisticArmour1,
        BallisticArmour2.

        The stage numbering is ONE-based: buying the first armour rank in the
        menu sets Unlocked_BallisticArmour1, and the package contains
        Unlocked_BallisticArmour4. This was 0-based, which set a bogus
        ...Armour0 that nothing reads and never granted the top rank - it has
        to match UpgradePool.AllFlagNames() on the game side exactly.
        """
        flags: list[str] = []
        for item_name, count in self.upgrade_counts.items():
            info = UPGRADE_FLAG_INFO.get(item_name)
            if not info:
                continue
            if info[0] == "PROGRESSIVE":
                _kind, flag_base, stages = info
                for stage in range(1, min(count, stages) + 1):
                    flags.append(f"{flag_base}{stage}")
            else:
                flags.append(info[1])
        return flags

    async def push_upgrade_state(self) -> None:
        """Converge the game to exactly the upgrades AP says we own."""
        flags = self.owned_upgrade_flags()
        await self.send_bridge("SET_UPGRADES," + ",".join(flags))
        # Zero the spendable upgrade points so the in-game menu can't hand
        # out the same upgrades AP is distributing.
        await self.send_bridge(
            f"SET_SUPPRESS_UPGRADE_POINTS,{'1' if self.upgrades_randomized else '0'}")

    async def push_counter_state(self) -> None:
        """Tell the game whether Counter should be suppressed.

        Only ever locks it when the YAML opted in - otherwise Counter
        behaves exactly as vanilla.
        """
        locked = self.counter_randomized and not self.has_counter
        await self.send_bridge(f"SET_COUNTER_LOCKED,{'1' if locked else '0'}")

    async def push_gadget_state(self) -> None:
        """Converge the game to exactly the gadgets AP says we own."""
        payload = ",".join(self.owned_gadget_classes)
        await self.send_bridge(f"SET_GADGETS,{payload}")

    async def push_all_state(self) -> None:
        """Assert every piece of state the game holds.

        Anything that can leave the game out of sync has to call this, not
        just push_gadget_state - a save reload restores gadgets AND armour
        AND spendable upgrade points, and a script rebuild resets the game's
        suppression flag to false. Pushing only gadgets is why counter and
        XP suppression were never applied in any session.
        """
        await self.push_gadget_state()
        await self.push_upgrade_state()
        await self.push_counter_state()

    # ---- game -> AP --------------------------------------------------

    async def handle_bridge_line(self, text: str) -> None:
        parts = text.split(",")

        if parts[0] == "PICKUP" and len(parts) >= 4:
            zone, _riddler_type, index = parts[1], parts[2], parts[3]
            location_name = f"{zone}_{index}"
            location_id = location_table.get(location_name)

            if location_id is None:
                logger.warning(f"{location_name} is not a known location - ignoring.")
                return
            if location_id in self.locations_checked:
                # AP checks are permanent; this happens after restoring a
                # game save without also resetting the AP room.
                logger.info(f"{location_name} was already checked this seed - no item will send.")
                return

            await self.check_locations([location_id])
            logger.info(f"Checked {location_name}")

        elif parts[0] == "GAME_LOADED":
            # The save restored every gadget the player "owns" in-game,
            # regardless of AP progress - and armour and spendable upgrade
            # points come back with it - so re-assert everything.
            await self.push_all_state()

    async def send_bridge(self, line: str) -> None:
        if not self.bridge_writer:
            return
        try:
            self.bridge_writer.write((line + "\n").encode("utf-8"))
            await self.bridge_writer.drain()
        except (ConnectionResetError, OSError):
            pass  # the bridge task will notice and reconnect

    def make_gui(self):
        ui = super().make_gui()
        ui.base_title = "Batman: Arkham City Client"
        return ui


async def game_bridge_task(ctx: BatmanACContext) -> None:
    """Keep a connection to the in-game bridge, reconnecting as needed.

    The game restarts often (every script rebuild during development, and
    normally when the player quits), so this never gives up - it just waits
    for the game to come back and re-asserts state when it does.
    """
    announced_waiting = False

    while not ctx.exit_event.is_set():
        # (Re)connect
        if ctx.bridge_writer is None:
            try:
                reader, writer = await asyncio.open_connection(BRIDGE_HOST, BRIDGE_PORT)
                ctx.bridge_reader, ctx.bridge_writer = reader, writer
                ctx.game_status = STATUS_CONNECTED
                logger.info(STATUS_CONNECTED)
                announced_waiting = False
                # The game may have loaded before we attached, in which case
                # its GAME_LOADED broadcast went nowhere. Assert state now.
                if ctx.server and ctx.slot:
                    await ctx.push_all_state()
            except OSError:
                if not announced_waiting:
                    ctx.game_status = STATUS_WAITING
                    logger.info(STATUS_WAITING)
                    announced_waiting = True
                await asyncio.sleep(BRIDGE_RETRY_SECONDS)
                continue

        # Read a line
        try:
            line = await ctx.bridge_reader.readline()
        except (ConnectionResetError, OSError):
            line = b""

        if not line:
            ctx.game_status = STATUS_LOST
            logger.info(STATUS_LOST)
            ctx.bridge_reader = None
            ctx.bridge_writer = None
            continue

        text = line.decode("utf-8", errors="replace").strip()
        if not text:
            continue

        try:
            await ctx.handle_bridge_line(text)
        except Exception:
            logger.error(f"Error handling '{text}':\n{traceback.format_exc()}")


def main(connect: Optional[str] = None, password: Optional[str] = None) -> None:
    Utils.init_logging("Batman Arkham City Client")

    async def _main(connect: Optional[str], password: Optional[str]) -> None:
        ctx = BatmanACContext(connect, password)
        ctx.server_task = asyncio.create_task(server_loop(ctx), name="ServerLoop")
        if gui_enabled:
            ctx.run_gui()
        ctx.run_cli()
        await asyncio.sleep(1)

        bridge_task = asyncio.create_task(game_bridge_task(ctx), name="BatmanGameBridge")

        await ctx.exit_event.wait()
        ctx.server_address = None

        await ctx.shutdown()
        bridge_task.cancel()

    import colorama  # type: ignore

    colorama.init()
    asyncio.run(_main(connect, password))
    colorama.deinit()


if __name__ == "__main__":
    parser = get_base_parser()
    args = parser.parse_args()
    main(args.connect, args.password)
