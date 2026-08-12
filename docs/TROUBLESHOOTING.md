# Troubleshooting

Most problems fall into one of three buckets: the client can't see the game,
the game can't see the client, or the save is wrong.

---

## The client says "Waiting for Batman: Arkham City..."

The client can't reach the bridge on `127.0.0.1:7777`.

- **Is the game actually running?** The bridge only exists inside the game
  process.
- **Did the scripts load?** BmSDK logs `ApBridge: listening on 127.0.0.1:7777`
  on startup. No line means the scripts aren't being compiled — check they're
  in `BmGame\Scripts\` and that BmSDK is installed.
- **Did you launch the patched exe?** After the Steam/GOG compatibility patch
  you may need to run `Binaries\Win32\BatmanAC.exe` directly.
- **Firewall.** It's loopback only, but some security software still blocks it.

The client retries forever, so you can start things in either order.

---

## Nothing gets stripped / I still have all my gadgets

Almost always one of:

- **You're not connected to the multiworld server yet.** The client only knows
  what to strip once it's joined a room.
- **You're in a menu.** Bridge commands are processed on the game's tick, so
  anything sent while paused just queues until you're back in gameplay.
- **You reconnected to an old room.** If you've been testing, an old server may
  still be running on the port, with old items in your inventory.

Type **`/resync`** in the client to force a full state push.

---

## I have an item the multiworld didn't give me

State converges to whatever the server says you own. If you cheated an item in
with `!getitem` at any point, or reconnected to an older room, you'll keep it.

Start a fresh room from a fresh seed. `/resync` won't help — the server really
does think you own it.

---

## The trophy counter in-game doesn't match my item count

**Known limitation.** The game's own Riddler counter still increments when you
physically pick a trophy up, but the trophies that count toward your goal are
the ones you *receive*. The two numbers will drift apart.

Trust the client and the tracker, not the in-game counter.

---

## Armour / upgrades aren't doing anything

- Check `randomize_upgrades` is actually enabled in your YAML.
- Armour arrives as **Progressive Ballistic Armour** / **Progressive Melee
  Armour**, four ranks each. One copy is rank 1.
- Upgrade points should sit at **0** — that's deliberate, so the in-game shop
  can't sell you what the multiworld is distributing.

If XP points reappear after a game restart, the client should re-zero them the
next time it pushes state. Loading a save or `/resync` forces it.

---

## Combat is impossible

If you turned on **`randomize_counter`**, this is expected and is why the
option is flagged experimental. Counter is the core defensive mechanic, and
the logic doesn't model fights — a seed can be technically completable and
still miserable.

Start a new seed with `randomize_counter: false`.

---

## My save isn't showing / the trophy count isn't 0

- The game reads saves from
  `Documents\WB Games\Batman Arkham City GOTY\SaveData\0000000000000000\`
  (possibly under `OneDrive\Documents\`).
- Launching **through Steam** may instead use
  `Steam\userdata\<id>\200260\remote\`. If a copied save doesn't appear, try
  the other location.
- **Close the game completely before copying saves.** The game writes on exit
  and will overwrite you.
- Steam Cloud can restore its own copy over a manually placed save. If a save
  keeps reverting, close Steam entirely and copy again.

---

## Generation fails with "Location Accessibility requirements not fulfilled"

This means some locations aren't reachable with the items available. If you
see it, please **open an issue with the seed's YAML and the list of missing
locations** — that's a logic bug and exactly the kind of report that's useful
at this stage.

---

## The tracker map is blank / items don't autotrack

- Make sure you picked the **AP variant** of the pack in PopTracker, not the
  plain one.
- Autotracking connects to the same Archipelago server as your client, with
  the same slot name.
- PopTracker holds pack zips open while running; close it before replacing one.

---

## Where the logs are

The scripts write to an **`ArchipelagoLogs`** folder next to the game
executable:

| file | contents |
|---|---|
| `pickup_log.csv` | every trophy pickup the hook saw |
| `gadget_pool.csv` | which gadgets are currently stripped |

The Archipelago client's own log is in `C:\ProgramData\Archipelago\logs\`.

Those two together usually show whether the game or the client is at fault —
if the pickup log has an entry the client never reported, the bridge is the
problem; if neither has it, the hook never fired.

---

## Reporting a bug

Please include:

- What you expected and what happened
- Your YAML
- The client log from `C:\ProgramData\Archipelago\logs\`
- `ArchipelagoLogs\gadget_pool.csv` if it's a gadget problem

This is alpha and largely untested in real multiworlds, so genuinely useful
reports are welcome — just don't expect a fast turnaround.
