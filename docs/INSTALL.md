# Installation

For **Batman: Arkham City — Game of the Year Edition** on **Steam** (Windows).

Only the Steam GOTY build has been tested. Epic and GOG may work but nobody
has tried.

Set aside 20–30 minutes for the first install. Most of it is BmSDK.

---

## What you'll need

| | |
|---|---|
| Batman: Arkham City GOTY | Steam |
| [BmSDK](https://bmsdk.dev) | `Team-BmSDK/BmSDK-AC` — the C# scripthook |
| [Archipelago](https://github.com/ArchipelagoMW/Archipelago/releases) | 0.6.1 or newer |
| [PopTracker](https://github.com/black-sliver/PopTracker/releases) | optional, but recommended |

---

## 1. Install BmSDK

This is the part that touches your game install, so do it carefully.

1. Download the latest release from
   [`Team-BmSDK/BmSDK-AC`](https://github.com/Team-BmSDK/BmSDK-AC/releases).
   The zip contains `Binaries` and `BmGame` folders.
2. **Copy/merge both** into your game folder, e.g.
   `...\steamapps\common\Batman Arkham City GOTY\`
3. **Steam and GOG users need the compatibility patch**, also on the releases
   page (under the older `v0.15.1` tag). It replaces `BatmanAC.exe`.

   > **Back up `Binaries\Win32\BatmanAC.exe` first.** Copy it to
   > `BatmanAC.exe.original_backup` before overwriting.

4. After patching, you may need to launch `Binaries\Win32\BatmanAC.exe`
   **directly** rather than through Steam.

Launch the game once and make sure it still runs before continuing.

---

## 2. Install the game scripts

Copy **every `.cs` file** from this repo's `game_scripts/` folder into:

```
...\Batman Arkham City GOTY\BmGame\Scripts\
```

That's six files:

| file | what it does |
|---|---|
| `ApBridge.cs` | TCP bridge on `127.0.0.1:7777` that the client talks to |
| `ApPaths.cs` | where logs get written |
| `StripGadgets.cs` | removes and restores gadgets |
| `UpgradePool.cs` | Batsuit/combat upgrades and XP suppression |
| `CounterLock.cs` | the experimental counter lock |
| `RiddlerHook.cs` | detects trophy pickups and sends checks |

BmSDK compiles these on launch — there's nothing to build.

> Don't copy anything from `dev/game_scripts_dev/`. Those are development
> tools, and one of them binds **G** to "give all gadgets", which would ruin
> a real run.

---

## 3. Install the apworld

Copy `releases/batman_arkham_city.apworld` into Archipelago's custom worlds
folder:

```
C:\ProgramData\Archipelago\custom_worlds\
```

This gives you both the world (for generating seeds) and the client.

---

## 4. Install the starting save

**This step is required.** The randomizer assumes the main story is complete
and every Riddler puzzle is unsolved. Starting from your own save will not
work properly.

1. **Close the game completely.**
2. Copy `releases/CANONICAL_story_complete_no_riddler.sgd` into your save
   folder, renaming it to whichever slot you want to use:

   ```
   ...\Documents\WB Games\Batman Arkham City GOTY\SaveData\0000000000000000\
   ```

   Name it `Save1.sgd`, `Save2.sgd`, or `Save3.sgd`.

   > **Don't overwrite `Save0.sgd`** unless you're happy to lose it. Filling
   > two or three slots means you can start a fresh run without repeating
   > this step.

3. If your Documents folder is redirected to OneDrive, the path will be under
   `OneDrive\Documents\` instead. Search for `SaveData` if unsure.

> Launching the game **through Steam** may use a *different* save location
> (`Steam\userdata\<id>\200260\remote\`). If your save doesn't appear,
> check there too.

---

## 5. Install the tracker (optional)

Copy `releases/batman_arkham_city_ap_0.4.1.zip` into PopTracker's `packs\`
folder, then pick **Batman: Arkham City** in PopTracker and choose the
**AP** variant to autotrack.

The pack has five map tabs: the overworld plus Museum, Steel Mill, Subway and
Wonder City. Interior checks live on their own maps, with a single marker on
the overworld showing where to go in.

---

## 6. Generate and play

1. Copy `yaml/BatmanArkhamCity.yaml` into Archipelago's `Players\` folder and
   edit the `name:` field. Read the comments — `randomize_counter` in
   particular.
2. Generate a seed and host it, or upload the YAML to
   [archipelago.gg](https://archipelago.gg).
3. **Start the game first** and load your save.
4. Open **ArchipelagoLauncher** → **Batman: Arkham City Client**.
5. Connect to the server with your slot name.

The client says `Connected to Batman: Arkham City.` when it finds the game,
separately from connecting to the multiworld server. You need both.

### Order matters less than it used to, but

Start the **game first**, then the client. The client re-asserts your item
state whenever it connects or you load a save, so either order should work —
but game-first is the tested path.

---

## Verifying it works

Once connected with a save loaded, you should see:

- Your gadgets **stripped** down to whatever the multiworld has given you
- **No spendable XP points** in the upgrade menu (if `randomize_upgrades` is
  on)
- A **centre-screen "Archipelago" banner** when an item arrives
- Trophy pickups appearing as checks in the client

If any of that doesn't happen, see
[TROUBLESHOOTING.md](TROUBLESHOOTING.md).

---

## Uninstalling

- Delete the six `.cs` files from `BmGame\Scripts\`
- Restore `BatmanAC.exe.original_backup` over `BatmanAC.exe`
- Remove the `.apworld` from `custom_worlds\`
- Verify game files through Steam if anything feels off

Your saves aren't modified by the mod — it only reads and writes live game
state, so your normal saves are untouched.
