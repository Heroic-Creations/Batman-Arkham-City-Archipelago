"""Keep the repo's copy of the BmSDK scripts in step with the game folder.

You edit the scripts in the game install (that's what VS builds and what the
game loads), so the repo copy drifts silently unless something syncs it. Run
this before committing.

    python dev/sync_scripts.py            # show what differs
    python dev/sync_scripts.py --pull     # game folder  -> repo   (usual)
    python dev/sync_scripts.py --push     # repo -> game folder    (after a clone)

Player-facing scripts live in game_scripts/. Development-only ones live in
dev/game_scripts_dev/ and are deliberately NOT part of a normal install -
UnlockTest.cs binds G to "give all gadgets".
"""
import argparse
import filecmp
import os
import shutil
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GAME_SCRIPTS = r"D:\SteamLibrary\steamapps\common\Batman Arkham City GOTY\BmGame\Scripts"

PLAYER = ["ApBridge.cs", "ApPaths.cs", "StripGadgets.cs",
          "UpgradePool.cs", "CounterLock.cs", "RiddlerHook.cs"]
DEV = ["DumpTrophies.cs", "DumpZoneNames.cs", "DumpGadgetSources.cs", "UnlockTest.cs"]

TARGETS = [(PLAYER, os.path.join(REPO, "game_scripts")),
           (DEV, os.path.join(REPO, "dev", "game_scripts_dev"))]


def main():
    ap = argparse.ArgumentParser()
    g = ap.add_mutually_exclusive_group()
    g.add_argument("--pull", action="store_true", help="game folder -> repo")
    g.add_argument("--push", action="store_true", help="repo -> game folder")
    args = ap.parse_args()

    if not os.path.isdir(GAME_SCRIPTS):
        sys.exit(f"Game scripts folder not found:\n  {GAME_SCRIPTS}\n"
                 "Edit GAME_SCRIPTS at the top of this file to match your install.")

    changed = missing = 0
    for names, repo_dir in TARGETS:
        os.makedirs(repo_dir, exist_ok=True)
        for name in names:
            src = os.path.join(GAME_SCRIPTS, name)
            dst = os.path.join(repo_dir, name)
            rel = os.path.relpath(dst, REPO)

            if not os.path.exists(src):
                print(f"  MISSING in game folder: {name}")
                missing += 1
                continue
            if os.path.exists(dst) and filecmp.cmp(src, dst, shallow=False):
                continue

            changed += 1
            if args.pull:
                shutil.copy2(src, dst)
                print(f"  pulled  {rel}")
            elif args.push:
                shutil.copy2(dst, src)
                print(f"  pushed  {name}")
            else:
                state = "differs" if os.path.exists(dst) else "not in repo"
                print(f"  {state:<12} {rel}")

    if missing:
        print(f"\n{missing} file(s) missing from the game folder.")
    if not changed:
        print("  in sync")
    elif not (args.pull or args.push):
        print(f"\n{changed} file(s) differ. Use --pull to update the repo.")


if __name__ == "__main__":
    main()
