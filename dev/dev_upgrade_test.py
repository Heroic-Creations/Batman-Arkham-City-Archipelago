"""Dev tool: grant one level of every upgrade over the game bridge, then
read the state back to prove it actually landed.

Run with the game in NORMAL GAMEPLAY (not in a menu). ApBridge processes
commands from OnTick, so anything sent while paused just queues.

    python dev_upgrade_test.py            # grant one level of each, verify
    python dev_upgrade_test.py --clear    # lock everything again
    python dev_upgrade_test.py --dump     # just read current state

Flag names are generated from apworld/Items.py, so this can't drift out of
sync with the randomizer.

Why it verifies rather than trusting the reply: SetGlobalFlag happily creates
unknown flags and reports success, so "granted=N" is not evidence that
anything happened. Only reading the state back is. That's precisely how the
0-based/1-based armour bug hid for so long.
"""
import argparse
import os
import socket
import sys
import time

HOST, PORT = "127.0.0.1", 7777
HERE = os.path.dirname(os.path.abspath(__file__))


def upgrade_flags():
    """One level of every upgrade, straight from the apworld item tables.

    Loads Items.py directly by path rather than importing the package -
    apworld/__init__.py pulls in Archipelago's `worlds` module, which isn't
    available when running this standalone.
    """
    import importlib.util
    import types

    # Items.py only needs Item / ItemClassification from BaseClasses.
    if "BaseClasses" not in sys.modules:
        bc = types.ModuleType("BaseClasses")

        class _Stub:
            def __init__(self, *a, **k):
                pass

        class _IC:
            progression = useful = filler = progression_skip_balancing = None

        bc.Item = _Stub
        bc.ItemClassification = _IC
        sys.modules["BaseClasses"] = bc

    path = os.path.join(HERE, "apworld", "Items.py")
    spec = importlib.util.spec_from_file_location("_bm_items", path)
    Items = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(Items)

    flags = [f"{base}1" for _n, (_i, base, _s) in Items.PROGRESSIVE_UPGRADES.items()]
    flags += [flag for _n, (_i, flag) in Items.SINGLE_UPGRADES.items()]
    return flags


class Bridge:
    def __init__(self):
        self.s = socket.create_connection((HOST, PORT), timeout=5)
        self.s.settimeout(1.0)
        self.read(0.8)  # discard anything already queued

    def read(self, seconds):
        end = time.time() + seconds
        buf = b""
        while time.time() < end:
            try:
                chunk = self.s.recv(4096)
                if not chunk:
                    break
                buf += chunk
            except socket.timeout:
                pass
        return [ln.strip() for ln in buf.decode(errors="replace").splitlines() if ln.strip()]

    def send(self, line, wait=3.0):
        print(f">>> {line}")
        self.s.sendall((line + "\n").encode())
        replies = self.read(wait)
        for r in replies:
            print(f"    {r}")
        return replies

    def close(self):
        self.s.close()


def state_flags(replies):
    for r in replies:
        if r.startswith("UPGRADE_STATE,"):
            for part in r.split(","):
                if part.startswith("unlocked="):
                    got = part[len("unlocked="):]
                    return set() if got == "(none)" else set(got.split())
    return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--clear", action="store_true", help="lock every upgrade")
    ap.add_argument("--dump", action="store_true", help="only read current state")
    args = ap.parse_args()

    try:
        b = Bridge()
    except OSError as e:
        sys.exit(f"Could not reach the game bridge on {HOST}:{PORT} - is the game running? ({e})")

    if args.dump:
        b.send("DUMP_UPGRADE_STATE")
        b.close()
        return

    wanted = [] if args.clear else upgrade_flags()
    print(f"{'clearing all upgrades' if args.clear else f'granting {len(wanted)} flags'}\n")

    b.send("DUMP_UPGRADE_STATE")
    print()
    b.send("SET_UPGRADES," + ",".join(wanted))
    print()
    after = b.send("DUMP_UPGRADE_STATE")
    b.close()

    got = state_flags(after)
    if got is None:
        print("\nNo UPGRADE_STATE came back - is the game paused in a menu?")
        return

    missing = sorted(set(wanted) - got)
    extra = sorted(got - set(wanted))
    print()
    print(f"asked for {len(wanted)}, game reports {len(got)} set")
    if missing:
        print(f"  MISSING : {missing}")
    if extra:
        print(f"  EXTRA   : {extra}")
    if not missing and not extra:
        print("  exact match")


if __name__ == "__main__":
    main()
