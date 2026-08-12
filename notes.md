# Batman: Arkham City — Archipelago Notes

Working notes on shape/scope. Nothing built yet.

## Status check (as of 2026-08-07)
- No existing Archipelago apworld for Arkham City that I could find. This would be a
  **from-scratch integration**, not a fork of someone else's work.
- There's an active PC modding/Cheat Engine community for Arkham City (Steam, Epic,
  and Game Pass builds), so the memory-hacking side has prior art to lean on — just
  not AP-specific.
- **David's copy:** Steam, GOTY edition, installed at
  `D:\SteamLibrary\steamapps\common\Batman Arkham City GOTY`. Matches the version the
  community Cheat Engine tables target.
- **Decompile-first approach confirmed working.** Gameplay logic lives in
  `BmGame\CookedPCConsole\BmGame.upk` (compressed — needed Gildor's Unreal Package
  Decompressor to unpack before UE Explorer could read it). Batch-exported all
  1,784 classes to readable `.uc` pseudocode via UE Explorer's console mode. Quality
  is good: real class/variable/function names, readable logic flow. Some native
  calls didn't resolve (show as `__NFUN_###__` placeholders) and some
  `defaultproperties` struct data errored out, but the actual game logic — what we
  care about — reads clearly. This beats blind memory-scanning as a starting point.

## MAJOR PIVOT: BmSDK (found + validated 2026-08-09, late session)

Supersedes most of the raw-memory/save-file archaeology below as the primary
approach going forward. Still worth keeping the old sections for context/
fallback, but BmSDK should be the first tool reached for from now on.

**What it is:** [BmSDK](https://bmsdk.dev) — a community-built, actively
maintained (MIT licensed) C# scripting platform/"scripthook" for Batman:
Arkham City and Arkham Knight. It exposes Unreal Engine 3's own UnrealScript
API directly to C#, with full IntelliSense — the same classes, properties,
and functions the original developers used. GitHub: `Team-BmSDK/BmSDK-AC`.

**Why this changes everything:** all of tonight's pain (blind memory scanning,
byte-level type/index decoding, save-file diffing, a game-crashing hardware
breakpoint) was working around not having named access to the game's own
logic. BmSDK gives us that access directly — no more guessing what a byte
means when we can just read the actual named property.

**Installation (done, confirmed working on David's Steam copy):**
1. Download release from `Team-BmSDK/BmSDK-AC` GitHub releases (zip contains
   `Binaries` and `BmGame` folders — copy/merge both into the game install dir).
2. Steam/GOG users need the compatibility patch (also on the releases page,
   older release `v0.15.1` tag) — replaces `BatmanAC.exe`. **Back up the
   original exe first** — done, saved as `BatmanAC.exe.original_backup` in
   `Binaries\Win32\`.
3. May need to launch `Binaries\Win32\BatmanAC.exe` directly rather than
   through Steam's UI after patching.
4. For actual script development (not just running pre-made scripts): need
   Visual Studio with the ".NET desktop development" workload. Open
   `BmGame\ScriptsDev\ScriptsDev.slnx`, write `.cs` files in `BmGame\Scripts\`
   (auto-compiled per the `.csproj`), press **F5** to build+launch+attach.
   `Debug.Log` output only appears in VS's own Output panel (set the
   dropdown to "Debug") when run this way — dropping a script and launching
   normally shows nothing.
5. **Gotchas found this session:**
   - VS's F5 launch profile (`BmGame\ScriptsDev\Properties\launchSettings.json`)
     does NOT inherit the Steam `-windowed` launch option — added it directly
     to `commandLineArgs` in that file.
   - Focus-pause also came back under this launch path even though the
     `DefaultEngine.ini` edit from earlier was still intact — root cause not
     confirmed, but fixed reliably by setting
     `Game.GetEngine().bPauseOnLossOfFocus = false` in a script's `Main()`
     instead of relying on the ini.
   - **Big one — the save file location changes once BmSDK's compatibility
     patch exe is in place.** Before BmSDK, saves lived at Steam Cloud's
     `C:\Program Files (x86)\Steam\userdata\<steamid>\200260\remote\Save0.sgd`
     (confirmed extensively earlier tonight). After installing the patched
     `BatmanAC.exe`, the game stopped using that location entirely and
     started reading/writing
     `C:\Users\<user>\Documents\WB Games\Batman Arkham City GOTY\SaveData\0000000000000000\Save0.sgd`
     instead (a generic non-Steam-style path — the patched exe likely isn't
     hooking Steamworks cloud saves the same way the original does). Cost a
     lot of confused troubleshooting before we traced it — **always check
     this WB Games path first for anything save-related once BmSDK's exe
     patch is installed**, not the Steam userdata folder.
   - The save-select screen's percentage is **not** Riddler completion — it
     reads as something else (likely story/campaign progress) and can show
     0% even when Riddler trophies are near-complete. Don't trust it; check
     the actual in-game Riddler tracker screen, or grep the save file
     directly for the `NNN/440` string, instead.
   - **The WB Games local save format has a 4-byte header the Steam Cloud
     format doesn't.** Native WB Games saves start with `00 80 04 00` before
     the actual save data; saves sourced from Steam Cloud (or downloaded from
     save-sharing sites, packaged for Steam) are missing this prefix — file
     is 4 bytes short (294,912 vs. the correct 294,916) and the game reads it
     as empty/corrupt. Fix: prepend `00 80 04 00` to the front of the file
     before placing it in the WB Games save folder.

**Confirmed working — the actual breakthrough:** wrote a `[Redirect]` hook on
`RPlayerController.MarkOffRiddlerItem(ERiddlerLocationName, RiddlerType,
Int32)` (the exact function signature known since the very first decompile
session). Real output from a real trophy pickup:
```
Riddler item marked off: Zone=RiddlerLoc_OWA, Type=RiddlerType_Pickup, Index=24
```
This is a **complete, fixed, reliable identity** for a specific physical
trophy — solves the logic-safety problem (apworld needs fixed per-check
identity, not order-based), solves detection, and does it safely (no crash,
unlike the Cheat Engine hardware-breakpoint attempt). Confirms
`RiddlerType_Pickup` specifically means trophy (as opposed to
`RiddlerType_Riddle`, tapes, cameras, etc. — same enum family, other values
not yet confirmed by name).

Working script pattern (`BmGame\Scripts\RiddlerHook.cs`):
```csharp
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Framework;

public class RiddlerHook
{
    [Redirect(typeof(RPlayerController), nameof(RPlayerController.MarkOffRiddlerItem))]
    static void MarkOffRiddlerItemRedirect(RPlayerController self, RPersistentData.ERiddlerLocationName zone, RPlayerController.RiddlerType type, int index)
    {
        Debug.Log($"Riddler item marked off: Zone={zone}, Type={type}, Index={index}");
        self.MarkOffRiddlerItem(zone, type, index); // call original to preserve normal behavior
    }
}

[Script]
public class DisableFocusPause : Script
{
    public override void Main()
    {
        Game.GetEngine().bPauseOnLossOfFocus = false;
    }
}
```

**Next planned step — full trophy map without manual collection:**
`Game.FindObjects<T>()` enumerates all currently-loaded objects of a type.
`RPickupBase` (parent of `RPickup_Riddler`) directly exposes `.Zone`,
`.PickupIndex`, `.PickupName`, and (as an Actor) should have `.Location`.
Plan: visit each of the 9 zones once (to trigger streaming load — likely
doesn't require actually collecting anything, David has a 100% save to use
for this too), run `Game.FindObjects<RPickup_Riddler>()`, log Zone + Index +
Name + Location for every result. Gets the entire 440-trophy map in ~9
visits instead of 440 manual pickups. Not yet attempted — next session.

**The write side (item granting) — real progress, one serious crash found,
not yet solved cleanly.** Session of testing on this specifically:

1. `RCheatManager.DebugGiveAllGadgets` — found via SDK docs, looked promising,
   **but the decompiled source shows it's just `RestoreAmmo()`** — refills
   ammo/recharge for gadgets you *already own*, doesn't unlock anything new.
   `RCheatManager` itself also isn't normally spawned (`pc.CheatManager` is
   null) — call `DebugGiveAllGadgets` directly on the pawn instead:
   `((RPawnPlayer)Game.GetPlayerPawn(0)).DebugGiveAllGadgets()`.
2. First strip attempt: `Game.FindObjects<RInventoryGadget>()` to enumerate
   all 14 owned gadgets, set `bSelectable = false`, `Ammo = 0`, `MaxAmmo = 0`,
   `CurrHuDAmmo = 0`. **Confirmed via logging that all 14 were set correctly**
   — but only visibly affected ammo-based gadgets (gel, freeze spray, etc.).
   Traversal tools (Grapple Gun, Line Launcher, Batarang) aren't ammo-gated at
   all in this game, so zeroing ammo does nothing to them. `bSelectable`
   alone didn't block wheel usage either.
3. **Found the real gate** by reading decompiled `RGadgetSelectV2.SelectGadget`:
   ```
   GadgetToSelect = InvMan.GetGadgetName(Gadget);
   if (GadgetToSelect != 'None') { InvMan.SetCurrentGadgetByName(GadgetToSelect); }
   ```
   Selection depends on whether the inventory manager's `GetGadgetName`
   resolves to something — i.e. whether the gadget is genuinely **in the
   inventory**, not any property on the gadget itself.
4. Switched to `Engine.InventoryManager.RemoveFromInventory(gadget)` (cleaner
   than the parameterless `DiscardInventory`) to properly remove gadgets, and
   kept the removed C# object references in a static list
   (`GadgetPool.Stripped`) planning to `AddInventory` them back later for
   granting.
5. **This caused a `System.ExecutionEngineException`** — a severe, low-level
   CLR failure, not a normal catchable exception. Best working theory:
   `RemoveFromInventory` likely lets Unreal's own GC destroy the underlying
   native object once nothing else references it, so our stored C# reference
   became a dangling pointer to freed native memory — and calling
   `AddInventory` on it later corrupted things badly enough to crash the
   whole engine, not just throw an exception.
6. **Not yet tried, next step:** don't reuse removed references at all —
   spawn a **fresh** instance via `Game.SpawnActor<T>()` when granting a
   gadget back instead of resurrecting the old removed object. Unconfirmed
   whether a freshly-spawned gadget actor needs additional setup beyond
   `AddInventory` to work correctly (equip bone, mesh, etc.) — needs testing.

Current script state (`BmGame\Scripts\StripGadgets.cs`) has the
remove-and-store-reference version that crashes on grant-back — needs the
spawn-fresh rewrite before testing granting again.

## Design shape (updated 2026-08-09, MVP scope)

- **Post-game framing, scoped to gadgets only for MVP.** Main story plays out
  normally, untouched — not part of the AP layer at all. Once the story's done,
  all gadgets (Batarang, Batclaw, Cryptographic Sequencer, Freeze Blast, etc.)
  get stripped. Combat upgrades and armor/health upgrades are a likely
  expansion later, but out of scope for the first version — start narrow, prove
  the loop works, expand from there.
- **Goal: collect X Riddler trophies**, X configurable via YAML.
- **Core loop:** start with just base traversal (no gadgets). Some trophies are
  reachable immediately; most aren't. Getting gadgets back from the multiworld
  is what opens up access to more trophies — this isn't an artificial lock, it
  mirrors how vanilla Arkham City already gates trophies behind specific tools.
- **Trophies are decoupled from their pickup location** (a real, standard AP
  pattern, not a hack): physically finding a trophy in your world still fires
  the AP check as normal, but does **not** by itself increment your native
  trophy counter. "Riddler Trophy" is a real item in the shuffle — your native
  counter only goes up when you actually *receive* a Trophy item, whether
  that's routed back from your own pickup or sent by a friend. This means your
  friends can meaningfully help you finish by sending you trophies, and your
  own pickups might go help them instead.
  - Needs a **second, separate counter** for "checks completed toward the YAML
    goal" (different thing from "trophies received"). Default plan: this lives
    in the external AP client's own window, not injected into the game. An
    in-game version of this counter is a **nice-to-have for later, not required
    to complete the game.**
- **Check detection is read-only.** The game already tracks trophy pickups
  natively — the client just watches memory for the state change and reports
  it. No injection needed for this half.
- **Item granting and counter-suppression are write-based — the harder half.**
  Two separate write-side problems: (1) granting gadgets/trophies received
  from the multiworld, (2) suppressing/correcting the native trophy counter so
  a physical pickup doesn't count itself before the item system decides where
  it actually goes. Both unproven so far — will need testing once we're
  further along.

## The three pieces every AP game integration needs

1. **The apworld** (Python, runs on your PC alongside Archipelago core)
   - Defines the checks (locations), items, regions, and logic rules.
   - This is "what can be randomized" — e.g. Riddler trophies, upgrade points,
     gadgets, story progression gates.
   - Pure data/logic, no game connection needed to write this part.

2. **The client** (Python, connects apworld ↔ running game)
   - Talks to the Archipelago server over websocket (send/receive items).
   - Talks to the actual game process to:
     - detect when you've collected a check locally, and
     - grant items the server sends you (gadgets, upgrades, etc.)
   - For a game with no scripting hooks, this is done by reading/writing the
     game's memory directly from Python (e.g. via `pymem`).

3. **The game hook** (the hard part for Arkham City specifically)
   - Arkham City is Unreal Engine 3, closed-source, no official mod API.
   - Need to find where in memory things like "have Batclaw," "Riddler trophy
     count," "upgrade points," etc. live — normally done with Cheat Engine
     (find addresses, watch them change, pin them down).
   - Once addresses are known, the Python client reads/pokes them directly.
   - This is the same shape as the TP Dusklight AP work, but Lua-in-Dolphin is
     swapped for memory-read/write-in-Windows-process — more manual, no emulator
     scripting layer to lean on.

## What to download/install to start

- **Batman: Arkham City (GOTY, PC)** — ✅ installed (Steam, GOTY, D drive).
- **UE Explorer** — ✅ installed. UnrealScript decompiler/package browser.
  https://github.com/UE-Explorer/UE-Explorer/releases
- **Gildor's Unreal Package Decompressor** — ✅ installed. Needed first, to unpack
  compressed `.upk` files before UE Explorer can open them.
  http://www.gildor.org/downloads
- **Cheat Engine** (7.0+) — ✅ installed. Fallback/confirmation tool for the memory
  side; also needed to open the community `.CT` cheat table for this game/version.
  Official site's download page has fake ad "Download" buttons — use an ad blocker
  (uBlock Origin) when grabbing it. Real free download exists, no need to pay for
  the Patreon "clean" version, just navigate carefully.
  https://www.cheatengine.org/
- **Not needed yet:** Python, `pymem`, and the Archipelago core repo
  (https://github.com/ArchipelagoMW/Archipelago) — these matter once we're building
  the actual client. Still in the "map out what's possible" phase.

## Content catalog (from decompiled BmGame.upk)

Raw inventory of what actually exists in the game's code — candidates for "what
could be a check or item," not a scope decision yet. Mostly pulled from enums in
`RPersistentData.uc` (the game's central save-data class) plus class names across
the export. Counts include a `_MAX` sentinel in the source enums, so real usable
count is one less than shown.

**Character bios** — `EBioCharacter` enum, 32 entries. Batman, Bruce Wayne, Alfred,
all the named villains (Riddler, Joker, Two-Face, Bane, Mad Hatter, Zsasz, Deadshot,
Azrael, Hush, Black Mask, etc.) Unlocked via `RPickup_Riddler.PickedUp()` calling
`PC.UnlockCharacterBio(...)`.

**Concept art** — `EConceptArt` enum, 81 entries. Very granular (individual
environment/character renders) — probably too fine-grained to use individually,
but a real unlock category tied to Riddler trophies.

**Character viewer / skins** — `ECharacterViewer` enum, 73 entries. Includes base
cast plus a large block of DLC skin unlocks (`EViewer_DLC_*` — Batman Beyond,
Batman '70s, Year One, Nightwing, etc.)

**Side missions ("Most Wanted")** — `EProgressCharacter` enum, 12 entries: Azrael,
Bane, AR Training, Deadshot, Hush, Iceberg Lounge Cops, Mad Hatter, Nora (Mr.
Freeze's wife), Freeze Cluster, Riddler, Zsasz, Bullies. This is likely the
cleanest "one check per side mission" list.

**Riddler zones** — `ERiddlerLocationName` enum, 9 entries (broad areas: Steel
Mill, Museum, Underworld, several open-world sub-zones). Trophies/riddles are
tracked per-zone, not as 400 individual global IDs — useful if we want to scope
Riddler content by area instead of by trophy.

**Challenge maps** — `EBatmanChallenge` enum, 45 entries. Separate combat/predator
challenge-room content (`RChallengeManager`, `RChallengeGoalDefinitions`,
`RGIChallenge`) — arguably its own game mode, worth a scope decision on whether
it's in or out.

**Gadgets** — no clean enum found; identified by name (Unreal's symbolic `name`
type) rather than a fixed list. Confirmed from combat-move classes:
Batarang, Batclaw, Bullwhip, Caltrops, Freeze Blast, Freeze Cluster, Remote
Electrical Charge (REC), Ricochet Sticks, Shield Bash, Sticky Bomb, Wrist Dart,
Area Stun.

**Upgrade points** — `RExperienceSystem`, `RSeqAct_AwardXP`, `RSeqAct_BankXP`,
`UpgradesIndex` var in `RPersistentData`. XP-based upgrade system confirmed to
exist; haven't dug into the specific upgrade list yet.

**Audio logs ("tapes")** — `ETapeCharacter` enum, 11 entries — a smaller
collectible category tied to specific characters (Catwoman, Joker, Two-Face, etc.)

**Story/map locations** — `EProgressLocations` enum, 21 entries — every major
named area in the game (GCPD, Museum, Steel Works, Iceberg Lounge, Church, Sewers,
Batcave, etc.) Useful more as a logic/region reference than as checks themselves.

Not yet dug into: individual Riddler trophy pickups (400, tracked as
positions/indices rather than named enum entries — would need a different
approach to enumerate), the specific upgrade tree contents, and exact structure
of the `RChallengeGoalDefinitions` data.

## Live memory mapping — reusable technique (found 2026-08-08)

After a lot of trial and error, landed on a much better method than blind
Cheat Engine scan-and-narrow. Worth using this first for every future content
category (side missions, gadgets, upgrades), not just Riddler trophies.

**Environment fixes needed first** (one-time setup):
- Steam launch option `-windowed` — this game defaults to true exclusive
  fullscreen, which blocks Borderless Gaming from working at all.
- [Borderless Gaming](https://legacy.borderlessgam.ing) — apply to `BatmanAC.exe`
  once it's launching windowed, for fast/clean alt-tabbing.
- `BmGame\Config\DefaultEngine.ini`, under `[Engine.Engine]`: changed
  `bPauseOnLossOfFocus=TRUE` → `FALSE`. Without this, the game pauses (stops
  simulating) every time you alt-tab to Cheat Engine, which quietly breaks any
  scan that depends on the game actually running in the background. Steam may
  revert this on file verification/update — reapply if that happens.

**The technique itself:**
1. Get a live object dump: community CE table for this game ("Enable
   Console/Commands", found on FearLess Revolution / cheatengine.net) includes a
   **GNames & GObjects Dumper (UNICODE)** script entry. Activating it writes
   `NamesDump.txt` and `ObjectsDump.txt` to `Binaries\Win32\` — every live
   object's name, class, and current memory address, straight from Unreal's own
   reflection system (`GObjects`/`GNames`).
   - Gotcha: the script's trigger is hardcoded to `GetAsyncKeyState(0x6F)`
     (physical Numpad `/`), checked directly via Windows API — **not** through
     Cheat Engine's own hotkey system, so rebinding the entry's hotkey in the CE
     UI does nothing. No numpad? Simulate the exact keypress instead:
     ```powershell
     Add-Type -TypeDefinition @"
     using System;
     using System.Runtime.InteropServices;
     public class KeySim {
         [DllImport("user32.dll")]
         public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
     }
     "@
     [KeySim]::keybd_event(0x6F, 0, 0, [UIntPtr]::Zero)
     Start-Sleep -Milliseconds 80
     [KeySim]::keybd_event(0x6F, 0, 2, [UIntPtr]::Zero)
     ```
     Must be loaded into an actual save (not main menu) for gameplay objects to
     exist yet.
2. **Find the live instance** of the class you care about — grep
   `ObjectsDump.txt` for `( ClassName PersistentLevel.RGameInfo.ClassName )` —
   that full-path pattern marks a real live instance, as opposed to the class
   definition or the `Default__ClassName` template object (which only holds
   design-time default values, not current save state).
3. **If hunting a specific known array**: search `ObjectsDump.txt` for property
   names (`BmGame.ClassName.FieldName`) to know what exists, then use Cheat
   Engine's **"Array of byte"** scan for a *distinctive known value sequence* if
   one exists (e.g. a set of fixed totals) — searched across the whole process,
   not restricted to the object. This finds the actual array **data**, separate
   from the object itself, since dynamic arrays are heap-allocated elsewhere.
4. **Trace back to the exact offset**: once you have the data's address, do an
   **Exact Value** scan for that address *as a pointer*, restricted to the live
   object's own address range (base to base+0x4000 is a reasonable window).
   That finds the TArray header (pointer + count + max, 12 bytes) at its real
   offset within the object.
5. Unreal lays out class fields in **declaration order** — once one field's
   offset is confirmed, sibling fields declared immediately after it in the
   `.uc` source (or the property list in `ObjectsDump.txt`) sit at fixed
   +12-byte strides, no further scanning needed.
6. **UE Explorer cannot help with any of this** — it only ever shows *static*
   file data (offsets within the `.upk` package, default values). Live memory
   offsets only exist once the game is running, and only Cheat Engine reading
   the live process can reveal them.

**Caveat:** raw addresses (both object instances and the offsets found this
way) are only valid for the *current running process* — heap layout differs
every game launch. This process needs re-running each session; a permanent
solution needs a proper pointer chain from a stable module-relative base,
which we haven't built yet.

## Findings this session (addresses are from one specific run — will differ next launch)

- **Trophy pickup detection is solid, and now fully confirmed (2026-08-09).**
  `PickupCount` (an `IntProperty` on `RPersistentShared`) lives at **two
  offsets that stay in perfect lockstep**: `+0x40` and `+0x248` from the
  object's base. Confirmed via two separate live differential tests (6→7→8,
  both addresses moved together both times) — this is no longer just
  name-based inference. Likely a live working copy + a mirrored/replicated
  copy (common in Unreal's networking-aware architecture even in
  single-player); either address works equally well for detection, no need to
  pick one. Strong circumstantial evidence it's Riddler-specific: sits right
  next to a whole family of separately-named counters for every other
  collectible type (`IncreaseRiddleCount`, `IncreaseJokerTeethCount`,
  `IncreaseBrokenHarleyHeadCount`, etc. — each type gets its own dedicated
  counter, so this generic one is the odd one out, and it's what the decompiled
  `RPickup_Riddler.PickedUp()` calls). This offset relationship (relative to
  `RPersistentShared`'s base) should hold across future sessions even though
  the base address itself will shift each launch — same technique from "Live
  memory mapping" above applies to relocate it.
- **The real "X/440" completion record lives across 9 zone arrays**, not one
  number: `RiddlerData_AreaTotals` (totals per zone: 60,60,60,40,60,40,40,35,45
  — sums to exactly 440, confirmed match to the in-game display) followed
  immediately in memory by `OverworldA/B/C/D/E`, `Steel`, `Museum`,
  `Underworld`, `Batman` — 9 more arrays, one per `ERiddlerLocationName` zone.
- Traced the full live chain for the Batman zone (only zone unlocked by
  default) this session: object base `334982C0` → `AreaTotals` header at
  `+0x128` → Batman zone header 9 slots later → pointer to actual array data.
- **The array data is not a simple 0/1 completion flag per slot** — it's pairs
  of small values (`type byte`, `index byte`, padding), reading like a
  definition/lookup table (what kind of Riddler content — trophy, riddle,
  informant tape, etc. — sits at each position), not a raw "collected" boolean.
  Decoding actual completion state from this needs more work — open for next
  session, now that we know exactly where to look and have the technique to
  get back here fast.

## Save file discovery (2026-08-09, later session)

Big find: the actual save file, not just live memory. Steam Cloud saves for
this game live at
`C:\Program Files (x86)\Steam\userdata\<steamid>\200260\remote\`:
- `Save0.sgd` — the real, actively-auto-saving save file (game autosaves after
  pickups; manual saving isn't available). 294,912 bytes, fixed size.
- `macosx_save0.sgd` — appears to be an unused cross-platform copy, stale.
- `profile.bin` — small (81 bytes), likely settings/profile, not save state.

**Confirmed:** the save file contains plain, human-readable ASCII strings
mixed with binary data — not fully compressed or encrypted, since strings like
`Park Row`, `10/440`, and `PickedUp_OWA_35E` are directly readable via a raw
grep for printable-character runs. `PickedUp_OWA_35E`-style identifiers look
like real per-pickup identity, tied to zone name — exactly what we need for
the logic-safety problem below, if we can fully decode the format.

**Tried and inconclusive:** diffed two save snapshots (10 trophies → 11
trophies). Got ~5,100 differing bytes for one pickup — far more than expected.
Ruled out a simple "new string appended, everything shifts" explanation (no
new `PickedUp_` string appeared in the 11-trophy version; tested byte-shift
alignment directly, best match was zero-shift at only 58%). Likely explanation:
some portion of the file is compressed or checksummed, so a small logical
change cascades into a large binary difference — but this isn't confirmed, and
we didn't fully decode the format before stopping for the session.

**Why per-trophy identity actually matters (important correction from earlier
in the session):** this isn't just for a pop-tracker nice-to-have. Without a
*fixed* identity per trophy (not order-based, not "the Nth trophy picked up
this save"), the apworld's logic system can't reliably say "this check
requires Explosive Gel" — because "the Nth trophy" refers to a different
physical location depending on player order. Getting this wrong risks
generating an unsolvable seed (e.g. placing a required gadget behind a
location that itself needs that gadget). This is a correctness requirement,
not a cosmetic one.

## Next session plan: function/parameter hooking instead of blind diffing

Realization near the end of today's session: modders doing actual logic-level
work (not just visual asset swaps, which don't need any of this) normally
don't reverse-engineer via external memory/save diffing at all — they either
recompile the UnrealScript directly, or hook the actual function call and read
its real, named parameters as the game uses them. We already know the exact
function signature from the original decompile:
`PC.MarkOffRiddlerItem(Zone, 3, PickupIndex)` on `RPickup_Riddler.PickedUp()`.

- Tried Cheat Engine's built-in **"Find out what writes to this address"**
  (right-click an address → sets a hardware write-breakpoint, shows the
  writing instruction + register state on trigger) as an easier first step
  before building a full custom hook.
- **This crashed the game** (`Fatal error!` popup, Wwise-related stack trace)
  when set on the `PickupCount` address (`33498300`) and a trophy was picked
  up. Game fully closed. Save data was safe (autosave already happened before
  the crash), but this specific address + technique combo is confirmed risky.
- **For next time:** retry on a different, less-frequently-written address
  (e.g. a zone-array slot instead of the general counter, since the counter
  may be touched by other frequent/contended code paths), or skip straight to
  a proper injected code hook instead of the breakpoint shortcut. Either way,
  treat this as a real crash risk, not a one-off — save/backup before trying
  again.

## Open questions to resolve before building
- ~~What counts as a "check"~~ — resolved for MVP (2026-08-09): Riddler
  trophies are the checks, gadgets are the items, see "Design shape" above.
  Side missions, bios/skins/concept art, and challenge maps are all set aside
  for a possible later expansion, not part of the first version.
- ~~Does the community CE table get us close enough~~ — resolved: yes, its
  GNames/GObjects Dumper script turned out to be the key tool of the whole
  session (see "Live memory mapping" above). The console/cheat entries
  (AddXP, UnlockAll, etc.) haven't been explored, just the dumper.
- How do we want to *give* items in-game — instant grant vs. some kind of
  in-game acknowledgment/popup?
- **Individual Riddler trophy identity — upgraded from "nice to have" to
  required.** Not just for a pop-tracker: without a fixed per-trophy identity,
  the apworld's logic system can't safely place items without risking an
  unsolvable seed (see "Save file discovery" above). Two live leads, neither
  finished: (a) the live-memory zone arrays (type/index pairs, decode stalled),
  (b) the save file's readable `PickedUp_ZONE_ID` strings (found, but the
  surrounding binary format isn't decoded, and diffing it directly didn't
  cleanly isolate single-pickup changes). Next session: try function-hooking
  approach instead of continuing to diff blindly (see "Next session plan").
- ~~`PickupCount`'s exact live offset~~ — resolved (2026-08-09): confirmed at
  `+0x40` and `+0x248` from `RPersistentShared`'s base, see "Findings" above.
- How do we suppress/correct the native trophy counter on pickup, so it
  doesn't increment before the item system has decided whether the trophy
  actually belongs to this player? New question from today's design session.
- Cheat Engine's hardware write-breakpoint ("Find out what writes to this
  address") crashed the game when used on `PickupCount`. Confirmed real risk,
  not yet understood why — avoid on that specific address, be cautious
  generally, until we understand the cause.

## Trophy topography map (2026-08-09)

New script: `BmGame\Scripts\DumpTrophies.cs` — `L` key, calls
`Game.FindObjects<RPickup_Riddler>()` and appends `Zone,PickupIndex,
PickupName,X,Y,Z` rows to `Batman-City-Archipelago\trophy_map.csv`, deduping
by `Zone_PickupIndex` across multiple presses within a session (safe to
press more than once per zone by accident). Confirmed property names
directly from decompiled `RPickupBase.uc`: `Zone`
(`ERiddlerLocationName`), `PickupIndex` (int), `PickupName` (string) — same
properties `RPickup_Riddler.PickedUp()` passes to `PC.MarkOffRiddlerItem
(Zone, 3, PickupIndex)`, so this map's identities will directly correlate
with what the live hook logs on actual pickups.

Uses David's 100% save specifically because pickup actors aren't deleted
after collection, just marked/hidden — `FindObjects` should still enumerate
all of them regardless of collected state, as long as the zone has been
streamed in at least once (which the 100% save's playthrough already did
for every zone). Plan: visit each of the 9 `ERiddlerLocationName` zones
once, press L in each, then read the accumulated `trophy_map.csv` for the
full ~440-entry map. In progress — 137 unique trophies captured after one
open-world flyover (OWA/B/C/D/E partially covered), confirming the
Zone/Index/Location data is real and correct. Still needed: the four
interior/instanced zones (Steel, Museum, Underworld, Batcave), and more
passes to saturate the open-world zones (proximity-streaming means one visit
per zone isn't enough — e.g. only 36/60 for OWE from one flyover).

**Faster alternative, in progress:** rather than physically flying
everywhere, added `ForceLoadAllLevels` (`K` key) to `DumpTrophies.cs` —
sets `bShouldBeLoaded`/`bShouldBeVisible = true` on every entry in
`WorldInfo.StreamingLevels`, attempting to force the whole city (and
possibly interiors) to stream in at once instead of relying on proximity.
Real risk flagged to David before trying: forcing everything loaded
simultaneously could stutter or destabilize the game, since streaming
normally exists specifically to avoid having it all in memory at once.
Chosen over the zero-risk alternative (reading placed-actor data straight
from the level `.upk` files via UE Explorer, untested territory this
session) because it reuses the already-proven BmSDK script toolchain.

**Tested — crashed.** Pressing K produced the same `ExecutionEngineException`
as the earlier gadget-removal crashes. Confirms the flagged risk was real —
forcing every streaming level to load simultaneously destabilized the
engine. Abandoned; back to the manual flyover approach, which is slower but
proven to work. `ForceLoadAllLevels`/K left in the script file but not
recommended for reuse.

**Bug found and fixed: dedupe didn't survive the K-key crash's relaunch.**
The `Seen` HashSet only lived in memory, so when the game had to be
relaunched after the K crash, the script forgot everything it had already
logged and started re-writing duplicate rows into `trophy_map.csv`. Fixed:
`DumpTrophies.cs` now reads the existing CSV on first `L` press each
session and pre-populates `Seen` from it, so relaunches can't reintroduce
duplicates going forward. Also manually deduped the existing file (149→149
lines including header, 148 unique data rows — no data lost, just
collapsed).

**Progress as of 2026-08-09 (deduped):**
- OWA: 31/60, OWB: 32/60, OWC: 35/60, OWD: 3/40, OWE: 36/60, Museum: 11/40
- Steel Mill, Underworld, Batcave/Batman zone: not yet visited (0/40, 0/35,
  0/45)
- **Total: 148/440**

**Design decision (2026-08-09): uncaptured trophies become filler-only
locations, not a blocker.** Rather than needing 100% precise topology
coverage before apworld logic work can start, decided: any trophy location
we have *confirmed* Zone/PickupIndex/coordinate data for is eligible to hold
any item (including logic-critical gadgets). Any trophy that exists in the
game but wasn't captured by the mapping sweep still counts as a real
location (so the total location count matches reality), but is restricted
to filler/junk items only — never something logic depends on. This removes
the need for perfect topology coverage as a prerequisite, while still
respecting the original logic-safety requirement (never gate a required
item behind an unverified/uncertain location).

**Refinement (2026-08-09):** the earlier "filler-only" idea was conflating
two different things. Corrected understanding:
- **Physical trophies** (`RPickup_Riddler`, has a world `Location`) — core
  MVP checks, always in scope. Ones we haven't mapped coordinates for yet
  are still real, identifiable checks (via the same `MarkOffRiddlerItem`
  Zone/Type/Index identity, not via coordinates) — coordinates were only
  ever useful for our own verification/tracking, not something AP logic
  itself needs. Only genuinely *unidentified* locations (no stable identity
  at all) should be filler-restricted, not just "no coordinate yet."
- **"Task" checks** (riddles, informant interrogations, Physical
  Challenges/challenge maps, etc. — no physical pickup object, but
  identifiable via the same Zone/Type/Index-style hook mechanism) —
  interaction/puzzle-based rather than walk-up-and-grab. **Revised again:**
  both Riddles *and* Physical Challenges are YAML-configurable optional
  check categories, not a hard exclusion for either. Off by default for the
  lean MVP, real legitimate expansions players can opt into individually.

**Status as of end of session (2026-08-09): 234 unique trophies captured,
called good enough to move on.** Museum fully complete (23/23), Steel Mill
(22/24) and Underworld (23/26) nearly done, OWA–OWE all in progress (34,
32, 35, 26, 39 respectively — exact per-zone targets for these five not yet
confirmed, see UI reconciliation below). Decided not to chase 100%
coverage — see "uncaptured trophies become filler-only locations" design
decision above. `trophy_map.csv` in the project root is the live dataset.

**In-game UI reconciliation (via the Riddler tracker screen, RIDDLES
402/440 at time of checking):** the tracker shows 9 categories with
player-facing names, each as a 5-row grid — confirmed **the rightmost
column of each zone's Riddler Trophy grid (5 cells) is Catwoman-exclusive**
content for that zone (David confirmed this directly in-game). Real
categories and totals reported:

| UI name | Total | "Not physical" | Physical trophies | Catwoman column | Batman-reachable |
|---|---|---|---|---|---|
| Park Row | 60 | 19 | 41 | 5 | 36 |
| Amusement Mile | 60 | 18 | 42 | 5 | 37 |
| Industrial District | 60 | 18 | 42 | 5 | 37 |
| Subway | 35 | 9 | 26 | 5 | 21 |
| The Bowery | 60 | 16 | 44 | 5 | 39 |
| Steel Mill | 35 | 11 | 24 | 5 | 19 |
| Museum | 35 | 12 | 23 | 5 | 18 |
| Wonder City | 35 | 10 | 25 | 0 (no column) | 25 |
| Physical Challenges | 40 | 40 | 0 | — | 0 (out of scope, Challenge Map content) |

Batman-reachable total: **232** (small unreconciled discrepancy against the
~40 Catwoman trophies known from external research — this math only
accounts for 35; not worth chasing further right now). "Not physical"
almost certainly means riddles/other non-pickup Riddler content — consistent
with the early-session finding that `RiddlerType_Pickup` is one value in a
larger type enum alongside `RiddlerType_Riddle` and others; our topology map
only ever captures `RPickup_Riddler` objects (the `Pickup` type
specifically), so it was never going to reach the full per-zone totals shown
in this UI.

**Still unresolved:** exact mapping between these UI names (Park Row,
Amusement Mile, Industrial District, Subway, The Bowery, Wonder City) and
our internal `ERiddlerLocationName` codenames (OWA–OWE, Underworld). Steel
Mill and Museum match directly by name. Subway is very likely Underworld
(thematically, tunnels/sewers). The other five need to be puzzled out later
— not blocking, since the "uncaptured = filler-only" design decision means
we don't need this solved before moving forward.

**Resolved — "Riddler's Revenge" is unrelated, nothing to exclude.**
Confirmed via search: it's the Challenge Map mode (combat/predator challenge
rooms, medals, playable as Catwoman/Robin/Nightwing via DLC) — a completely
separate system from the 440 open-world `RPickup_Riddler` trophies, using
different objects entirely. Challenge maps were already out of MVP scope
(see "Design shape" above). Nothing in `trophy_map.csv` needs filtering —
the 148/440 count stands as real, in-scope data.

## Apworld skeleton (2026-08-09)

Set up `apworld/` in the project folder — first real project files, not
just BmSDK scripts. Also initialized git in the project root with a
`.gitignore` excluding copyrighted game assets (`*.upk`, `unpacked/`),
saves (`*.sgd`), Cheat Engine files, and bundled tools — only `notes.md`,
`trophy_map.csv`, and actual code should ever be tracked. Not committed yet
(David wants to review/commit himself).

Grounded the skeleton in Archipelago's actual current `World`/`WebWorld`/
`Options` API (fetched live from `ArchipelagoMW/Archipelago` on GitHub
rather than working from memory) before writing anything, to avoid building
on stale API assumptions.

Four files, all genuinely stub/placeholder, syntax-checked with Python
3.11.9 (available locally) but **not yet tested against real Archipelago
core** — we don't have that repo cloned locally, so this hasn't been run
through actual generation yet:
- `__init__.py` — `BatmanACWorld(World)`, stub `create_regions`/
  `create_items`/`set_rules` (all just `pass`)
- `Items.py` — 4 gadgets + "Riddler Trophy" as a stub item table (not the
  full 14 confirmed-working gadgets yet)
- `Locations.py` — 5 real trophy locations pulled from `trophy_map.csv`
  (not the full ~232 confirmed set yet)
- `Options.py` — `TrophyGoal` range option (1-232 placeholder ceiling,
  default 50)

Deliberately kept minimal/stub rather than wiring in the full item/location
tables immediately, so the shape of the thing is reviewable before scaling
up. Next real step once confirmed: expand `create_regions`/`create_items`/
`set_rules` with real logic, and grow the item/location tables to the full
confirmed sets.

## "Manual signal relay" test rig (2026-08-09)

David's idea, and a good one: instead of building the real AP server/client
plumbing right now, validate the detect→respond→grant loop by hand first,
using Claude as the stand-in for the AP server. Requires: real pickups
logged somewhere Claude can read directly (not just VS Output), and granting
made specific/on-demand instead of random. Also needed: a way to re-trigger
real pickups repeatedly on a 100%-complete save without needing fresh
trophies or a save editor.

**`RiddlerHook.cs` updated** — `MarkOffRiddlerItemRedirect` now also appends
`Timestamp,Zone,Type,Index` to `pickup_log.csv` in the project root on every
real pickup, alongside the existing `Debug.Log`.

**`StripGadgets.cs` reworked** — `GrantRandomGadget` replaced with
`GrantSpecificGadget`: number keys `0`-`9` grant the pool entry at that
index instead of a random one. `StripGadgets` (H key) now also writes the
current pool (index + real gadget name) to `gadget_pool.csv` so Claude can
read exactly what's available and tell David which number to press for a
specific gadget.

**New `ResetNearbyTrophy` script, `R` key** — finds the nearest
already-collected `RPickup_Riddler` and resets it for re-pickup, avoiding
needing a real save editor (which would mean decoding the still-not-fully-
understood `.sgd` binary format — much harder than this). Found via
decompile that this needed two properties, not just one:
`bHasBeenPickedUp = false` (the obvious one) and also `bPendingDelete =
false` — discovered because `RPickupBase.Interact()` guards re-triggering
`PickedUp()` with `if (!bPendingDelete)`, and something in the pickup flow
(an unresolved native call, `__NFUN_279__()`, right after `PickedUp()` in
`Interact()`) might set it. Notably, empirical evidence from the topology
mapping work (finding hundreds of already-collected trophies via
`FindObjects` on a 100% save) already confirmed pickup actors are **not**
actually destroyed on collection, just deactivated somehow — consistent
with this being a flag-reset problem, not an object-lifecycle one. Not yet
tested whether resetting both flags is sufficient on its own.

**Upgraded to a real TCP bridge (2026-08-09) — David correctly pushed back
that file-polling doesn't prove external connectivity.** File-based relay
only proves Claude can read a CSV, not that the game can accept a live
connection from an outside process — which is what the real AP client will
actually need. New `ApBridge.cs`:
- `ApBridge` static class hosts a `TcpListener` on `127.0.0.1:7777`,
  started once from a `[Script].Main()`.
- **Thread safety, learned from this session's crash history:** the accept
  thread and per-client read threads only ever enqueue incoming lines into
  a `ConcurrentQueue<string>` — they never touch game objects directly.
  Actually acting on commands (`GadgetPool.GrantByIndex`) only happens from
  `ApBridge.ProcessQueuedCommands()`, called once per frame from a
  `Script.Tick(float)` override — guessed this method name/signature exists
  on BmSDK's `Script` base class (we've only used `Main`/`OnKeyDown` before
  now); if wrong, will show as a clear compile error to fix. Broadcasting
  *out* (`ApBridge.Broadcast`) is safe to call directly from
  `RiddlerHook.cs`'s redirect, since that hook already runs on the game's
  own thread.
- `RiddlerHook.cs` now also calls `ApBridge.Broadcast($"PICKUP,{zone},
  {type},{index}")` on every real trophy pickup, in addition to the
  existing file log.
- Refactored the grant logic out of `GrantSpecificGadget` and into
  `GadgetPool.GrantByIndex(int)`, shared by both the keyboard path (number
  keys) and the new network path (`GRANT,<index>` command).

**External test client (Claude's side, not part of the shipped project
yet):** two small Python scripts in the scratchpad —
`ap_listener.py` (connects, logs every received line with a timestamp to
`external_client.log` in the project root, run in the background) and
`ap_send.py <command>` (one-shot connect-send-disconnect, e.g.
`ap_send.py GRANT,3`). Both syntax-checked with the local Python 3.11.9.
Not yet tested against the real running game — next step once David
rebuilds.

**Fixed the `Tick` guess.** Compile error confirmed `Tick(float)` isn't
real: `CS0115: 'ApBridgeStarter.Tick(float)': no suitable method found to
override`. Found the actual name via the same strings-extraction technique
used earlier for UE Explorer's CLI flags (reflection via PowerShell fought
us on 32-bit/dependency-loading issues) — `BmSDK.dll`'s strings contain
`OnTick`, matching the `On`-prefix convention already seen in
`OnKeyDown`. Fixed to `OnTick(float deltaTime)`. Not yet rebuilt/retested.

**Still wrong — found the real, authoritative answer.** `OnTick(float)`
also failed to compile (same "no suitable method found to override").
Tried reflecting directly on `BmSDK.dll` via PowerShell to settle this for
good, but hit a real wall: **the DLL targets .NET 10
(`System.Private.CoreLib, Version=10.0.0.0`)**, which Windows PowerShell's
.NET Framework host fundamentally can't load (`mscorlib` vs
`System.Private.CoreLib` mismatch) — reflection this way just isn't
viable on this machine. Found the real answer instead via BmSDK's own
GitHub source, linked directly from their docs:
`github.com/etkramer/BmSDK/blob/main/src/BmSDK/Framework/Script.cs`.
**Full, authoritative list of `Script`'s overridable methods** (all
`public virtual void`, all zero-parameter except `OnKeyDown`):
- `Main()` — once, when the engine first becomes ready
- `OnEnterMenu()` — first load into the main menu
- `OnEnterGame()` — every time a new world loads
- `OnTick()` — **once every world tick, zero parameters** (not `(float
  deltaTime)` — that was the actual bug)
- `OnKeyDown(Keys key)`
- `OnUnload()` / `OnLoad()` — hot-reload lifecycle

Fixed to `OnTick()`. This source file is the definitive reference for
`Script`'s API going forward — check it directly instead of guessing next
time a lifecycle method is needed. (Note: there's also a *different* class,
`ScriptComponent` — attaches to a specific Actor, has `OnAttach()`/
`Tick()`/`OnDetach()` — mentioned in passing while looking this up, not
used here, but worth remembering it's a separate thing from `Script` if it
comes up later.)

**IT WORKS — full round trip confirmed live (2026-08-09).** With `OnTick()`
fixed, the build succeeded and `ApBridge: listening on 127.0.0.1:7777`
appeared in the log. Claude's external Python listener connected
successfully (a real TCP socket from an independent OS process, not
anything running inside the game). Tested the whole loop on a fresh trophy
(not a reset one — the reset trick didn't work on already-collected
trophies across three attempts on the 100% save, abandoned as a side
problem not worth chasing right now; a genuinely fresh pickup on a new game
sidestepped it entirely):

1. David collected a real trophy → `RiddlerHook.cs` fired →
   `pickup_log.csv` got the row **and** `ApBridge.Broadcast` sent it live →
   Claude's listener received `PICKUP,RiddlerLoc_OWA,RiddlerType_Pickup,24`
   within milliseconds of the in-game event.
2. David pressed H (stripped gadgets, `gadget_pool.csv` populated).
3. Claude sent `GRANT,3` over the network via `ap_send.py` → the game's
   `OnTick`-driven queue processor picked it up, called
   `GadgetPool.GrantByIndex(3)` safely on the main thread, and broadcast
   `GRANTED,RGooSprayBm` back → confirmed both in the listener output and
   visually in-game (David actually had Explosive Gel back).

This validates the real, previously-unresolved architecture question: BmSDK
(a full .NET runtime hosted inside the game process) can host a live TCP
server that an independent external process connects to, safely, with
proper thread separation (network threads only enqueue; all game-object
access happens on the main thread via `OnTick`) — no crashes, no
`ExecutionEngineException`, unlike everything that went wrong with direct
inventory manipulation earlier in this project. This is the real foundation
the actual Archipelago client can be built on, whenever that's next.

Worth noting: this was tested on a **brand new save**, not the 100% one
with all its accumulated topology/testing history — no special pre-existing
state needed. That's a good sign for robustness, since it's exactly the
real-world scenario: a player starting a fresh AP randomizer run needs this
to just work from a clean slate, not depend on prior setup.

## Riddler trophy guide — full read-through for logic (2026-08-09/10)

David asked for a complete, non-skimmed read of a Riddler trophy walkthrough
to build real per-trophy gadget-requirement logic (the core logic-safety
goal: never gate a required gadget behind a location that itself needs that
gadget). First source tried, a GameFAQs guide (`gamefaqs.gamespot.com/
xbox360/981374-batman-arkham-city/faqs/63180`), turned out too sparse for
physical trophies specifically (Park Row's entire "Statues" section was
only 3 paragraphs against an expected ~36+) — good for other content
(its "Riddles" subsection uses the exact row/column notation matching the
in-game UI grid, useful later for the optional Riddles category), but not
usable as the primary source for physical-trophy logic.

**Switched to GamesRadar's guide**
(`gamesradar.com/batman-arkham-city-riddler-guide/`), which turned out to
be genuinely exhaustive: numbered per-trophy entries (not vague named
sections), one page per zone at a predictable URL pattern
(`/2/`=Park Row, `/5/`=The Bowery, `/8/`=Amusement Mile,
`/11/`=Industrial District, `/14/`=Subway, `/17/`=Steel Mill,
`/20/`=Museum, `/23/`=Wonder City — zone names match the in-game tracker
exactly). WebFetch choked on the pages (too much image/video markup
bloat, truncated before reaching real content) — worked around this by
`curl`-ing the raw HTML (with a browser user-agent; GameFAQs actively
blocked WebFetch's default UA, curl with a spoofed one got through) and
parsing locally with a small Python script
(`scratchpad/extract_trophies.py`) that pulls each numbered heading's
paragraph text via regex, stripping HTML/entities and trailing site-nav
cruft. One real bug hit and fixed: some headings have descriptive suffixes
appended to their numeric ID (e.g. `...-trophy-33-inside-gcpd`), which
broke the first version of the regex and silently merged/dropped entries —
fixed by allowing an optional suffix after the number.

**Read completely, all 8 zones, ~229 total entries, no skimming**, per
David's explicit requirement. Aggregate gadget/technique mention tally
across everything: Batclaw 39, Explosive Gel 34, Freeze Blast 30,
Cryptographic Sequencer 28, Remote Electric(al) Charge 26, Line Launcher
26, Remote/piloted Batarang 16, Disruptor: Mine Detonator 7, dive bomb
(base move, not a gadget) 9, Disruptor: Firearm Jammer 4, grapnel boost
(base move) 3.

**Cross-referenced against the game's actual internal gadget list**
(`EBatmanGadgetList` enum, `RPawnPlayer.uc` — the authoritative 14-slot
list our strip/grant system operates on: Batarang, RCBatarang,
MultiBatarang, SonicBatarang, BatClaw, ExplosiveGel, LineLauncher,
Resonator, MagneticBlast, FreezeBlast, SmokeBomb, FreezeGunJammer,
JammerGadget, FreezeClusterGrenade). Key findings:
- **Cryptographic Sequencer and Remote Electric Charge aren't in that list
  at all** — not wheel-gated gadgets. Tried to decompile-verify exactly
  what they are; dead end (their defaultproperties are stored as
  unresolved raw byte offsets in this decompile, not readable property
  names). David tested directly in-game instead: Sequencer can't unlock
  gates at the very start of the game, confirming it's a story-progression
  unlock, not something present from minute one — **decided to exclude it
  from the AP item system entirely**, same treatment as Batclaw/Detective
  Mode (always progresses normally, never gated). This fully resolves what
  would otherwise have been a real unknown (whether our wheel-based
  strip/grant mechanism could even touch it).
- **Confirmed via direct in-game test: `RHarpoonGunBm` = Batclaw**, not
  `RGrappleGunBm`. This means `RGrappleGunBm` is the base
  grapple/glide-boost ability (matches it never appearing in
  `PCSelectableGadgets` — always available, not wheel-gated), consistent
  with Batclaw's own already-decided always-accessible status.
- **Gadgets that actually gate physical trophies** (appear as real
  requirements in the 229 entries read): Explosive Gel (`RGooSprayBm`),
  Freeze Blast (`RFreezeSprayBm`), Line Launcher (`RLineLauncherBm`),
  Remote-Control Batarang (`RBatarang_Controllable`), Disruptor/Jammer
  Gadget (`RJammerGadgetBm` — covers both "mine detonator" and "firearm
  jammer" modes mentioned in the guide).
- **Gadgets that never came up as required** for any of the 229 physical
  trophies: Resonator/Sonic Batarang (`RResonatorTunerBm`), Magnetic Blast
  (`RMagneticBlastBm`), Bat-Distract, Multi-Target Batarang, Smoke Bomb,
  Freeze Cluster Grenade. Still real, grantable items — just don't gate
  any trophy locations in this dataset, possibly relevant to side missions
  or combat later (out of current scope).

**New design decision: optional "randomize starting kit" YAML toggle.**
You normally start the game already owning basic Batarang (`RBatarangBm`)
and Remote-Control Batarang (`RBatarang_Controllable`) — confirmed by
David checking in-game. Decided: a YAML option, off by default (vanilla
behavior — start with both, not part of the item pool) or on (both get
pulled into the real AP item pool, classified as progression/priority,
must be received like anything else). Only applies to these two, since
both are confirmed wheel-gated with the already-proven strip/grant
mechanism — Cryptographic Sequencer is excluded from this (and everything
else) per the decision above.

**Correction applied.** `apworld/Items.py` rewritten: removed "Grapple
Gun" entirely, split real gadgets into `GATING_GADGETS` (the 5 that
actually gate physical trophies) and `NON_GATING_GADGETS` (the 6 real,
grantable gadgets that never came up as a trophy requirement), added
`STARTING_KIT_ITEM_NAMES` (Batarang + Remote-Control Batarang) for the new
toggle. `apworld/Options.py` got a new `RandomizeStartingKit` (Toggle)
option. Syntax-checked, not yet tested against real Archipelago core
(still don't have that repo cloned locally).

**Zone-name mapping attempt (2026-08-10).** Tried to resolve which
internal `OWA`-`OWE` codename maps to which of the 5 open-world UI names
(Park Row, Amusement Mile, Industrial District, The Bowery, Wonder City)
without needing David in-game. Got one solid anchor: the very first TCP
bridge test pickup (`RiddlerLoc_OWA`, index 24) happened on a **brand new
game**, and Arkham City's story begins in the Park Row/Courthouse area —
so **OWA = Park Row**, reasonably confident. Computed coordinate bounding
boxes for the other 4 zones to look for adjacency patterns, but without a
second landmark-tagged anchor point couldn't reliably extend this to the
remaining 4 — parked as unresolved rather than guessing wrong on
something logic-safety-relevant.

**Bigger realization: the trophy-guide-to-PickupIndex correlation problem
is deeper than just zone-naming.** Even for zones confirmed by name
(Steel Mill, Museum, Subway≈Underworld), there's no reliable way to map
"GamesRadar guide entry #12 in Steel Mill" to "our PickupIndex 7 in
RiddlerLoc_Steel" — the guide's numbering is arbitrary/its own, not tied
to the game's internal PickupIndex. Solving this exactly would mean
comparing ~229 individual descriptions against coordinates one at a time,
not something reliably automatable at this scale. Computed per-zone
gadget tallies anyway (see table below) and found every zone needs most
of the 5 gating gadgets *somewhere* within it — meaning even zone-level
(not per-trophy) gating would effectively mean "need almost everything to
finish any single zone," which isn't the precise per-check logic the
project actually wants.

| Zone | Gel | Line | Freeze | REC | Disruptor |
|---|---|---|---|---|---|
| Park Row | 7 | 2 | 3 | 4 | 3 |
| Amusement Mile | 7 | 4 | 6 | 5 | 3 |
| Industrial District | 4 | 4 | 3 | 5 | 2 |
| Subway | 2 | 2 | 3 | 2 | 0 |
| The Bowery | 6 | 4 | 2 | 3 | 1 |
| Steel Mill | 3 | 3 | 4 | 3 | 0 |
| Museum | 2 | 3 | 4 | 3 | 2 |
| Wonder City | 3 | 4 | 5 | 1 | 1 |

**Decision: ship without per-trophy gating for now, safe default.** Built
out `create_regions`/`create_items`/`set_rules` for real (grounded in
Archipelago's actual current `Region`/`CollectionRule` API, fetched live
rather than guessed): single `Menu` → `Arkham City` region holding all 234
real locations from `trophy_map.csv`, no access rules on any of them (all
reachable from the start — safe, since "no rule" can never create an
unsolvable seed, only "wrong rule" can). Item pool: 1 each of the 5 gating
+ 6 non-gating gadgets + base Batarang (12 real gadget items), with
Batarang/Remote-Control Batarang precollected instead of pooled when
`randomize_starting_kit` is off (the default). Remaining pool slots filled
with "Riddler Trophy" items. Goal: `state.has("Riddler Trophy", player,
trophy_goal)`. Syntax-checked, not yet tested against real Archipelago
core.

**Open follow-up, not blocking:** if precise per-trophy gadget logic is
wanted later, the two real options are (a) resolve OWB-OWE zone naming via
David checking in-game, then manually correlate a meaningful sample of
guide entries to specific PickupIndex values by cross-referencing
coordinates/landmarks by hand, or (b) accept region-level-only logic
permanently and lean on the "uncaptured/unconfirmed = safe default, no
rule" principle as the permanent design rather than a stopgap.

**David's correction (2026-08-10): "all 234 accessible, no requirements"
was stated wrong.** Important distinction — our *logic* enforces no
requirement, but that's different from no requirement existing. Many of
the 234 genuinely can't be collected without specific gadgets in reality
(confirmed straight from the guide read-through). No-rule logic avoids the
specific *circular* problem this project has cared about since the start
(an item locked behind a location that itself needs that item), but
doesn't guarantee the generator's placement will feel accurate/fair, since
it doesn't know some checks are temporarily unreachable without a gadget.
Also flagged the real risk of *fake* precision: guessing which specific
PickupIndex needs which gadget without real correlation data could easily
assign it backwards (marking an easy trophy as gated while leaving the
actually-gated one falsely marked free) — which risks recreating the
exact circular problem we're trying to avoid, possibly worse than doing
nothing. Presented three options (zone-level approximate gating / invest
in real correlation / leave as-is). **Decided: leave as-is for now,
revisit gating once more of the apworld/client is built and tested.**
Trophy goal fixed to range_start=100, default=100, range_end=234
(matching the confirmed location count) per David's separate request.

## First real Archipelago generation — success (2026-08-10)

David has a full packaged Archipelago install at `C:\ProgramData\Archipelago`
(not a source checkout — the installer distribution, `ArchipelagoGenerate.exe`
etc., version 0.6.7). It has a `custom_worlds/` folder that takes `.apworld`
files directly (a zip containing a folder named after the world module, plus
an `archipelago.json` manifest) — no need for the full source tree.

**Packaged and installed:** wrote `apworld/archipelago.json` (`game`,
`world_version`, `minimum_ap_version`, `compatible_version`/`version: 7`
— matched the format of a real installed apworld, `sonic_heroes.apworld`,
since a couple of other pre-existing custom worlds are already failing to
load with the installed AP version due to manifest format mismatches, a
good reminder this matters). Zipped `apworld/`'s contents into
`batman_arkham_city/` and installed to `custom_worlds\
batman_arkham_city.apworld` via Python's `zipfile` (no `zip` binary
available in this shell).

**Test YAML gotcha:** `game: Batman: Arkham City` in a hand-written test
YAML broke the parser — the second colon in the value needs quoting
(`game: "Batman: Arkham City"`, and the per-game options section key too).
Easy fix once seen; real YAML syntax issue, not an apworld problem.

**Ran `ArchipelagoGenerate.exe` for real — it worked.** Isolated the test
YAML in its own subfolder of `Players/` to avoid entangling with David's
existing Twilight Princess player files. Output:
```
Batman: Arkham City : v0.1.0 | Items: 13 | Locations: 234
Filling the multiworld with 234 items.
Beginning output...
Creating final archive at ...AP_<seed>.zip
Done. Enjoy.
```
Checked the spoiler log to confirm correctness, not just "didn't crash":
- Custom options came through correctly (`Trophy Goal: 100`, `Randomize
  Starting Kit: No`).
- `Starting Items: Batarang, Remote-Control Batarang` — precollection
  logic for the starting-kit toggle worked exactly as intended.
- All 234 real locations present with correct `Zone_Index` names.
- All 10 pool gadgets (12 total minus the 2 precollected) landed at real
  locations: Explosive Gel, Line Launcher, Magnetic Blast, Sonic Batarang,
  Freeze Cluster Grenade, Multi-Target Batarang, Bat-Distract, Freeze
  Blast, Smoke Bomb, Disruptor — one each, matching the design exactly.

This is the first real, external validation of the apworld against actual
Archipelago core (not just our own syntax-checking) — full end-to-end
generation success on the first real attempt after the YAML quoting fix.

## Real Archipelago client (2026-08-10)

New `client/` folder in the project root, separate from `apworld/`.

**`client/generate_game_data.py`** — generates `client/game_data.json`
directly from the real `apworld/Items.py`/`Locations.py` source (not
hand-duplicated, to avoid drift). Has to stub out `BaseClasses`,
`worlds.AutoWorld`, and `Options` (real Archipelago-only modules that only
exist inside an actual AP install's Python environment) since the client
runs in plain system Python. Run it again any time the apworld's item/
location tables change.

**Added `AP_NAME_TO_CLASS_NAME` to `apworld/Items.py`** — a real dict
(not just comments) mapping every AP item name to its actual UnrealScript
class name (e.g. `"Explosive Gel": "RGooSprayBm"`), single source of truth
consumed by both the apworld and the generator.

**`client/ap_client.py`** — the actual client. Bridges two connections in
one asyncio event loop: a websocket to the real Archipelago server (using
AP's actual network protocol — handshake sequence, packet formats, etc.
fetched live from their docs rather than guessed, same approach as the
Region/AutoWorld API earlier) and a raw TCP connection to the in-game
`ApBridge` (127.0.0.1:7777) we already built and proved live.
- Game → AP: `PICKUP,Zone,Type,Index` lines from the bridge get translated
  to `Zone_Index` location names, looked up against `game_data.json`, sent
  as `LocationChecks`.
- AP → Game: `ReceivedItems` packets get translated via `item_id_to_name`
  then `AP_NAME_TO_CLASS_NAME`, sent to the bridge as `GRANT_NAMED,
  <className>`. Items with no class name mapping (i.e. "Riddler Trophy")
  are just counted, nothing to grant in-game.
- Handles the `ReceivedItems.index` field properly for reconnect-safety
  (only processes genuinely new items, matches AP's own client
  conventions).
- Connection scheme handling: tries `wss://` first (needed for real hosted
  servers like archipelago.gg), falls back to `ws://` for local/LAN test
  servers without a certificate — added after David asked how to connect
  to a real hosted server address, not just our local test one.

**C# side extended to match** — `GadgetPool.GrantByClassName(string)`
added to `StripGadgets.cs` (searches the stripped pool by real class name
instead of arbitrary index), and `ApBridge.cs`'s command processor now
handles `GRANT_NAMED,<className>` alongside the existing index-based
`GRANT,<index>` (which stays, still useful for manual keyboard-driven
testing). Note: `GrantByClassName` only works for gadgets that were
stripped at some point this session — matches the MVP's post-game
assumption that everything is already owned before stripping; doesn't yet
handle "grant something the player never had at all," which is out of
scope for the current design.

**First real end-to-end infrastructure test, in progress:** installed
`websockets` (`pip install websockets`, wasn't present). Started a real
`ArchipelagoServer.exe` locally hosting the actual generated multiworld
output from the earlier successful generation test
(`AP_52639101212369415721.zip`) — confirmed `Loading embedded data
package for game Batman: Arkham City` and `server listening on
0.0.0.0:38281`. Full live loop (real pickup → real server → real grant)
not yet run end-to-end — waiting on confirming the game/ApBridge is up
with the `GRANT_NAMED` addition before running `ap_client.py` against it.

**Full live loop confirmed working (2026-08-10).** Real trophy pickup in
Park Row → detected by the hook → sent to the real Archipelago server as a
LocationCheck → server responded with ReceivedItems → client translated
and sent GRANT_NAMED to the game. Found and fixed a real bug along the
way: `handshake()` only looked for the `Connected` packet in the initial
response and silently discarded any other packets bundled in the same
message — the server sends starting inventory (Batarang, Remote-Control
Batarang) bundled together with `Connected`, so it was being dropped,
causing a `ReceivedItems.index` mismatch on the next real packet. Fixed by
routing every non-`Connected` packet in the handshake response through the
same `handle_ap_packet` handler `ap_listen_loop` uses. Also made the
index-gap handling self-healing (resync instead of permanently drop) as a
safety net. Confirmed after the fix: starting items + the real check's
item all processed correctly, with the two starting-kit items correctly
no-op'ing on grant (already owned, never stripped - expected, not a bug).

**David's two follow-up requests, both done:**
1. **Visible client console** — was running the client via a background
   task only Claude could see output from. Switched to launching it via
   PowerShell's `Start-Process` with the fully-resolved python.exe path
   (needed - the bare `python` command resolves to a WindowsApps alias
   stub that doesn't launch correctly through `Start-Process`), giving
   David a real, visible console window he can watch and interact with
   directly.
2. **In-game notification on item receipt** — `GadgetPool.GrantByIndex`
   now also calls `pc.QueueObjectiveMessage(4.0f, "Archipelago",
   $"Received: {friendlyName}", "", 0, false, "", false, false)`, reusing
   the exact same popup mechanism the game's own Riddler trophy pickups
   use (confirmed signature via decompile:
   `RPlayerController.QueueObjectiveMessage(float Time, string Title,
   string Desc, string OrgDesc, int ArrowType, bool bForceShowMap, string
   BackPrompt, bool bNoDuplicates, bool bPulseCompassIndicator)`). Added a
   small `FriendlyNames` dict in C# (mirrors `apworld/Items.py`'s
   `AP_NAME_TO_CLASS_NAME`, inverted, kept in sync by hand) so the popup
   shows "Explosive Gel" instead of "RGooSprayBm". Not yet rebuilt/tested.

**Console visibility fix.** Launching the client via Claude's own
background tools produced a process David couldn't see or interact with
directly, even though it was confirmed running (same session ID as his
interactive desktop, so not a simple session mismatch - more likely a
window-station quirk). Resolved by having David launch it himself directly
in his own terminal instead of trying to spawn a visible window
programmatically - simpler and guaranteed to work.

**Big design gap surfaced (2026-08-10): counter suppression was never
actually built.** David caught two related problems testing live: (1)
picking up a trophy that happens to award filler "Riddler Trophy" gives
zero in-game feedback right now - feels "dead"; (2) bigger issue -
`RiddlerHook.cs` still calls the original `self.MarkOffRiddlerItem(...)`
on every physical pickup, meaning the native trophy counter/tracker
increments immediately regardless of what AP does. This directly
contradicts the project's very first design decision (see "Design shape"
near the top of this file): a physical pickup should fire the AP check
without incrementing the native counter - the counter should only go up
when a "Riddler Trophy" item is actually *received* from AP (self or
friend). This is "the harder half" flagged as unsolved at the very start
of the project and never actually revisited until now. Real plan agreed:
suppress the native mark-off on pickup, and make receiving a "Riddler
Trophy" item trigger the popup + counter increment instead, generically
(not tied to a specific zone/index, since a received trophy could be one
a friend sent). Lead: `WorldInfo.Game.PersistentShared.
IncreasePickupCount(Zone)`, seen in `RPickup_Riddler.PickedUp()`'s
decompiled source - takes a zone but might just be the raw counter
increment, separate from the per-slot checkmark `MarkOffRiddlerItem`
does. **Not yet implemented - proceeding carefully given this project's
crash history whenever counter/memory-adjacent systems get touched**
(the very first Cheat Engine hardware-breakpoint crash was on this exact
`PickupCount` value). Not yet investigated in detail.

**Fresh testing save (2026-08-10).** David wanted Save1 replaced so he
can keep testing pickups without running out, and separately we'd wanted
a "story complete, trophies uncollected" save since early in the project
(see "Real Archipelago core..." section) but never had one. Solved both
at once: backed up the current Save1 to `Save1_before_reset_test_backup.
sgd` in the project folder, then installed `extracted_100save_v2\Save0.sgd`
(the legitimate 100% save) into the active Save1 slot - had to prepend
the 4-byte WB Games header (`00 80 04 00`) since that file is in the
Steam-Cloud format, missing 4 bytes, per the save-format finding from
early in the project.

New `ResetAllTrophies` script, **T** key (`StripGadgets.cs`) - bulk
version of the existing `ResetNearbyTrophy`/R (which only resets the
single nearest one). Resets every already-collected `RPickup_Riddler`
currently loaded/streamed, not just the nearest. Same streaming
limitation as the topology-mapping work applies - only affects what's
currently loaded, so needs pressing repeatedly while flying around to
catch everything, same as the L-key trophy dump earlier. Deliberately did
NOT suggest re-trying the `ForceLoadAllLevels`/K-key trick to speed this
up, since that's the one that crashed the game earlier this project.
Plan: load into the restored 100% save, fly around pressing T repeatedly,
then do an in-game save once fully reset - that becomes the new
long-term "story complete, trophies fresh" testing save.

**David correctly caught that R never actually worked.** All three
original R attempts (trophy cage, near Ace Chemicals, near the start)
came back "no already-collected trophy found nearby" - we abandoned
debugging it at the time and used a fresh-new-game workaround instead,
without ever confirming why. Root cause, found via decompile: the real
"already collected" ground truth is `WorldInfo.GRI.FlagManager.
GetGlobalFlag(t.GetPickedUpName())` - a global flag keyed by a string
(`"PickedUp_" + LevelName + FlagName + PickupIndex`) - completely separate
from the `bHasBeenPickedUp` field the reset scripts were checking and
clearing. `bHasBeenPickedUp` almost certainly only gets set `true` by an
actual live pickup happening *this session*, not by loading a save where
it was already found - so the R/T filter was skipping literally every
trophy, every time, matching the observed failure exactly. Fixed both
`ResetNearbyTrophy` and `ResetAllTrophies` to check/clear the real flag
(`RFlagManager.GetGlobalFlag`/`SetGlobalFlag(string, bool)`, both `native
final function`, confirmed signatures via decompile) via `Game.
GetWorldInfo().GRI.FlagManager`, keeping the old `bHasBeenPickedUp`/
`bPendingDelete` resets too as a belt-and-suspenders. Not yet tested -
per David's (correct) caution, plan is to verify R works for real on a
single trophy before trusting T for a bulk pass.

**Compile fix.** `WorldInfo.GRI` is typed as the base engine
`GameReplicationInfo` in BmSDK's bindings, which doesn't have
`FlagManager` - confirmed via decompile that the real declaring class is
`RGameRI` (`var RFlagManager FlagManager;`), not `GameReplicationInfo` or
a guessed `RGameReplicationInfo`. Fixed both scripts to
`((RGameRI)Game.GetWorldInfo().GRI).FlagManager`. Tested — got "Reset 0
already-collected trophies" twice in a row. Added a diagnostic version to
`ResetNearbyTrophy`/R: now logs the actual computed flag key + result for
the first 5 trophies found (regardless of outcome) plus a total/flagged
count, instead of just a binary yes/no, so we can see real data before
theorizing further. Tested with the diagnostic version — good news, it DID find and clear a
real flagged trophy this time (`RiddlerLoc_OWA_35`), confirming the
`FlagManager` fix is real and working. But the trophy still didn't
reappear in-world, meaning that flag alone isn't sufficient - there's a
second, separate flag store also set in `RPickupBase.PickedUp()`:
`WorldInfo.Game.PersistentShared.SetSharedFlag(GetPickedUpName(), true)`.
Added clearing that too (`RPersistentShared.SetSharedFlag(string, bool)`,
confirmed native signature via decompile), declared on `RGameInfoBase`,
needed a cast (`(RGameInfoBase)Game.GetWorldInfo().Game`) same pattern as
the earlier `RGameRI` cast. Applied to both R and T. Not yet retested.

## Save format FULLY MAPPED (2026-08-10) — supersedes earlier "opaque" conclusion

David asked whether a save editor is possible. Probed the real files
instead of guessing. **The format is now completely understood.**

**It is UE3 chunk-compressed, big-endian, LZO.** Structure:
- 4-byte WB Games header (`00 80 04 00`) on WB-format files only; Steam
  Cloud format omits it (the 294,916 vs 294,912 difference found earlier).
- 40 bytes of preamble, then a chain of UE3 compressed chunks.
- Each chunk: 24-byte big-endian header, then exactly `totalComp` bytes.
  Header = `PACKAGE_FILE_TAG (9E 2A 83 C1)`, blockSize, totalComp,
  totalUncomp, blk1Comp, blk1Uncomp.
- Standard UE3 128KB block size (0x20000).
- **5 chunks** in the 100% save, at offsets 40 / 318 / 5259 / 48330 /
  76503. Chaining verified perfectly: `nextOffset - thisOffset -
  totalComp == 24` for every single one. No gaps, no padding, no mystery
  bytes.
- Totals: 639 + 19191 + 131072 + 131072 + 30077 = ~312KB real data
  compressed to ~83KB, then zero-padded out to the fixed 294,912.
  (Entropy confirms: ~7.1 for the first 64KB, then exactly 0.00 for
  everything past ~80KB — pure zero padding.)
- **Compression is LZO, not zlib.** zlib tested at all five true data
  offsets, failed on every one ("incorrect header check"). Byte patterns
  are textbook LZO literal runs (chunk @48330 starts `0d 61 72 67 65 74
  5f 31` = a literal run spelling "arget_1").

**This corrects two earlier wrong conclusions:**
1. The old "~5,100 bytes changed for one pickup, so it's compressed or
   checksummed" finding was right about compression but the experiment
   was confounded — the two saves also differed in position, time, AI
   state, RNG, etc. The real reason a small logical change cascades is
   simply LZO block recompression.
2. A brief theory that the save held a pre-allocated table of all ~440
   `PickedUp_` flag strings was **disproved by the data** — only 2
   `PickedUp_` literals exist in the whole file. The rest are LZO
   back-references, which also explains the odd broken fragments
   (`edUp`, `OW`, `OWA`) a naive string scan turns up.

Decompressed content is confirmed to be UE3 object-path + property-name
pairs, e.g. `Steel_B2_Ch6789.RBMCombatPoint_Explosive_2.Used`,
`RFractureWall_0.HasExplodedTowardsBack`,
`RExperienceSystem_2.GA_Stun_Batarang`, plus FlagManager flags like
`PickedUp_OWD_Teeth_2` — i.e. exactly the flag store we found via
`RFlagManager`/`RPersistentShared`.

**Verdict: a save editor is genuinely buildable.** Remaining work:
(a) LZO1X decompress — needs a Python lib (`python-lzo`/`lzokay`) or
reuse Gildor's `decompress.exe`, already sitting in the project folder
and built for exactly this UE3 chunk format; (b) edit flags in the
plaintext; (c) **LZO recompress — the genuinely risky step**, output must
be something the game's own decompressor accepts; (d) rewrite chunk
headers with new sizes; (e) confirm no checksum lives elsewhere in the
40-byte preamble. Realistically a few hours with a real chance of
stalling at (c).

**But it may be unnecessary — cheaper test identified.** We already have
working live flag-clearing (`R` successfully found and cleared
`RiddlerLoc_OWA_35` via `FlagManager.SetGlobalFlag` +
`PersistentShared.SetSharedFlag`). The trophy didn't visually reappear,
but pickup visibility is decided at actor spawn / level-load time
(`InitialFlagCheck`/`EarlyDestroy`), NOT re-evaluated live — so an
already-hidden actor wouldn't pop back in mid-session even on success.
**We never tested clearing flags → saving in-game → reloading.** Since
these flags demonstrably live in the save file, the game would write a
correctly-compressed save for us, requiring zero format work. This test
should be run before investing in an editor.

## Canonical starting save — FOUND (2026-08-10)

**Design pivot first.** David correctly killed the whole trophy-reset
approach: resetting the pickup flag is only half the state — the *puzzle*
state is saved separately and independently. Confirmed by strings visible
in the save itself (`RFractureWall_0.HasExplodedTowardsBack`,
`RBreakablePickupPenguin_0.Broken`, `RBMCombatPoint_Explosive_2.Used`).
Even a perfect flag reset leaves trophies sitting inside already-solved
puzzles — not gameplay. And resetting all puzzle state is intractable:
per-level Kismet, heterogeneous, and ambiguous (is `RFractureWall_0` a
Riddler wall or a story wall? resetting wrong breaks story progression).

Critically this is **not just a testing problem — it's a replay problem**.
Anyone finishing one AP run has solved every puzzle; run #2 needs the same
reset. AP games get replayed constantly.

**Solution: ship a canonical starting save.** Don't reset anything -
distribute one prepared save every run starts from. Solves testing,
replayability, AND seed reproducibility (byte-identical start state across
all players = deterministic logic, comparable bug reports). Standard
practice for the genre.

**Found one.** Steam guide "Save Files Compendium"
(`steamcommunity.com/sharedfiles/filedetails/?id=3173362158`) hosts a
Google Drive folder
(`drive.google.com/drive/folders/1pUUSr6i1IiS4aut9rK3dhW595NAl0L7V`) with
16 progressive saves. Files 7-9 are the ones that *add* side missions and
Riddler content — so **file 6** (`6_main_story_catwoman_complete.sgd`,
both Batman + Catwoman stories complete) has story done with Riddler
content untouched. Exactly the target state, and matches David's ask that
side missions look like the 0% file.

Downloaded via direct Drive URL (`drive.google.com/uc?export=download&id=
<fileId>`; IDs scraped from the folder page HTML — file6 =
`1bG1Wj3FurUdwB4XbRicWAkej33KyqliG`, file5 =
`1FSlpkwIVFTYq916BNXsy4GUn8Sb8WCAP`). Steam-format 294,912 bytes, so the
4-byte WB Games header (`00 80 04 00`) was prepended per the known
format quirk.

**Verified before installing** using the newly-cracked chunk format —
total uncompressed state volume is a good proxy for how much progress a
save holds:

| Save | chunks | uncompressed |
|---|---|---|
| 10 trophies (early game) | 3 | 17,830 |
| file5 (story complete) | 3 | 75,274 |
| **file6 (story complete)** | **3** | **79,743** |
| 100% everything | 5 | 312,051 |

The 100% save carries ~4x the state of the story-complete ones — exactly
the difference you'd expect from all the puzzle/collectible flags. Strong
corroboration file6 is genuinely Riddler-untouched.

**Installed non-destructively to `Save2.sgd`** (free slot; Save0 and Save1
deliberately untouched). Pristine copy archived in the project root as
`CANONICAL_story_complete_no_riddler.sgd`, plus a redundant copy in
`canonical_save_backup/`.

**VERIFIED IN-GAME (2026-08-10) — it works.** David loaded it and
confirmed: **all the Riddler puzzles are still unsolved/open**, which was
the entire requirement that killed the reset approach. Also loads with all
gadgets in hand — correct and by design (story complete = gadgets
legitimately earned; the mod strips them at run start, AP grants them
back).

**Which file gives you Batman — settled empirically.** The filenames are
counterintuitive: they name *whose story was completed*, not who you play
as. Confirmed by loading both:
- `6_main_story_catwoman_complete.sgd` → **plays as BATMAN** ← the one we
  want, installed as Save2, archived as the canonical.
- `5_main_story_batman_complete.sgd` → plays as Catwoman (installed as
  Save3 during the mix-up, kept for now as
  `canonical_save_backup/candidate_file5_batman_complete.sgd`; not needed
  — Catwoman has a separate gadget set, our whole system targets Batman's
  `RPawnPlayer` wheel).

The Steam guide's descriptions were accurate all along; a mid-session
guess that they were swapped was wrong, and file IDs were re-verified
against Drive page titles to rule out a download mix-up (they were
correct).

**Note: the live `Save2.sgd` is already dirty** — the game autosaved over
it the moment it was loaded (hash differs from the archive). Always copy
*from* the pristine archive; never save over it.

**Canonical re-cut after a Batman swap (2026-08-10).** David wiped Save2
back to pristine, loaded it, swapped to Batman, saved, and fully closed
the game (important — guarantees the save is flushed, not half-written).
That result was promoted to canonical:
- New canonical hash `9a08ca70...` (previous was `d7750ed4...`).
- Old one preserved as
  `canonical_save_backup/PREVIOUS_canonical_d7750ed4.sgd` rather than
  overwritten.
- Sanity-checked via the chunk format: 3 chunks, **79,709 bytes**
  uncompressed state — squarely in the story-complete band (~75-80k) and
  nowhere near the 100% save's 312k, confirming the swap didn't pick up
  any Riddler progress.

**Restore procedure:** copy
`CANONICAL_story_complete_no_riddler.sgd` over
`...\WB Games\Batman Arkham City GOTY\SaveData\0000000000000000\Save2.sgd`.
Only do this with the game closed — a running game will autosave over it.

**Shipping caveat, not yet resolved:** this is a community-made save from
a Steam guide. If it ships with the randomizer it should credit the guide
author, or better, link users to the original rather than redistributing
the file.

## Persistence / gadget-state sync (2026-08-10)

Fixes the biggest actual playability hole: **save + reload handed every
gadget back**, because the wheel state we manipulate is runtime-only and
the game rebuilds it from the save on load. Built as the "client owns
state, game stays dumb" design rather than incremental patching.

**C# — `StripGadgets.cs` / `GadgetPool`:**
- `IsReady()` — guards on pawn/controller/InvManager all existing.
  `OnEnterGame` can fire before everything is spawned.
- `StripAll()` — strips every wheel gadget from a **clean slate**,
  deliberately clearing the old pool first. Critical after a reload: the
  game rebuilds the wheel arrays from `ACP_Details.GadgetsPC`, so stale
  `Stripped_N` placeholders from the previous session would corrupt the
  mapping.
- `ApplyDesiredState(List<string>)` — strip everything, then silently
  restore exactly the named classes. Idempotent and restart-safe: always
  converges to the same result no matter the starting state.
- `GrantByIndex` gained a `showPopup` flag. Incremental grants (real new
  items) pop the "Received: X" message; bulk restores don't, to avoid a
  wall of popups on every load.
- The H-key script now just calls `StripAll()` instead of duplicating the
  loop.

**C# — `ApBridge.cs`:**
- New `SET_GADGETS,name1,name2,...` command. Empty list is legal and
  means "strip everything".
- Doesn't apply immediately — stores into `pendingDesiredState` and
  applies from `OnTick` once `IsReady()`. Needed because the client
  replies over localhost in milliseconds, easily beating the pawn into
  existence.
- `ApBridgeStarter.OnEnterGame()` broadcasts `GAME_LOADED` on every world
  load (new game, save load, level transition).

**Python — `ap_client.py`:**
- Tracks `owned_gadget_classes` (ordered list, so restores are
  deterministic run to run).
- On `GAME_LOADED` → `push_gadget_state()` sends the full authoritative
  `SET_GADGETS`.
- On a genuinely new `ReceivedItems` gadget → records it, then sends the
  incremental `GRANT_NAMED` (the one that shows the popup).

Two distinct paths on purpose: **incremental = new item, with popup;
bulk = state restore, silent.**

Deliberate design choice: if **no client is connected, nothing is
stripped**. The mod stays inert rather than breaking a normal
playthrough for someone who merely has it installed.

Client syntax-checked; C# not yet rebuilt or tested in-game.

## Message feed + on-demand item testing (2026-08-10)

David raised he couldn't test gadget grants because he doesn't know which
trophies hold real gadgets (only 10 of 234 in a given seed do).

**Immediate answer:** joined the spoiler log against `trophy_map.csv` to
list the gadget-bearing locations with world coordinates. Useful, but
coordinates are awkward to navigate to in-game.

**Better answer built — `!getitem` via typed commands.** Added
`stdin_loop()` to `ap_client.py`: anything typed into the client console
is sent to the server as a `Say` packet, so all normal AP commands work.
Most useful is `!getitem <name>` (server item-cheat is enabled -
`disable_item_cheat: false` in host.yaml), which grants any item **through
the real pipeline** — real server → real ReceivedItems → real
GRANT_NAMED → real in-game grant. Removes the dependency on finding
specific trophies entirely, and also gives chat/!hint/!remaining for free.

**Message feed (David's "Player1 sent Item to Player2" ask).** `PrintJSON`
packets were previously ignored (`pass`). Now rendered by
`render_print_json()`: resolves `player_id` parts via the slot table
captured from `Connected`, and `item_id`/`location_id` parts via our own
tables. Best-effort by design — other games' item names would need their
DataPackage, which we don't request, so those show as `Item#1234` rather
than pretending we know them. Output goes to the client console AND to
the game as a new `TOAST,<message>` bridge command.

`TOAST` on the C# side calls `QueueObjectiveMessage`, same mechanism as
the item popup. **Caveat: positioning/styling is whatever that objective
popup does — not a custom top-right fading feed.** Real control over
placement would mean GFx/Flash movie work, which is a much bigger job.
Also note the popup path itself is still *unverified* — David has never
actually seen one, because the only trophies he's collected held filler.

Not yet rebuilt/tested.

**Debug flag: `--test-random-gadget`.** David's idea — every check sent
grants a random not-yet-owned gadget, so every trophy pickup visibly does
something during testing regardless of what the seed actually placed
there. Explicitly a testing shortcut, not real logic; kept entirely
client-side (behind an opt-in CLI flag, prints a loud TEST MODE banner)
so none of it leaks into the shipped game code. Deliberately routes
through the same `owned_gadget_classes` list the real path uses, so these
fake grants still exercise persistence (save/reload → `SET_GADGETS`)
correctly. Should be removed once the trophy-counter work lands and every
check has real feedback of its own.

**Ordering bug found and fixed.** First test of all this "failed" — no
strip on load, no banner on pickup. Diagnosed from evidence rather than
guesswork: `pickup_log.csv` showed the pickup DID register (so the game
hook was fine), `gadget_pool.csv` was hours stale (so `StripAll` never
ran), and `tasklist` showed **no python process at all** — the client
simply wasn't running (it had died earlier when the game closed, per the
`ConnectionResetError`, and hadn't been restarted). Server (38281) and
game bridge (7777) were both confirmed listening via `netstat`.

That surfaced a genuine robustness flaw though, not just user error:
state was only pushed on `GAME_LOADED`, so starting the game *before* the
client — a completely natural order — meant that broadcast fired into the
void and the game stayed un-stripped until the next world reload. Fixed
by also calling `push_gadget_state()` immediately after the AP handshake,
so connecting at any time converges the game to the correct state.

**Reminder for future debugging:** all three pieces must be running —
AP server, the game (bridge on 7777), and the client. The client is the
one that silently isn't there.

## Client distribution — the real answer (2026-08-10)

David asked, fairly: "are we expecting people to run a PowerShell command
when they start the game?" No. Researched how real apworlds ship clients.

**Standard mechanism: register a client component in the apworld**, which
makes it a clickable entry in `ArchipelagoLauncher.exe`. Confirmed present
in this AP install (`lib/worlds/LauncherComponents.pyc`) and confirmed in
active use by 7+ installed custom apworlds — borderlands2, custom_robo,
dk64, gl, hades, shadow_the_hedgehog, and **twilight_princess_dusklight**
(David's own other project). Exact pattern, lifted from the TP apworld:

```python
from worlds.LauncherComponents import (
    Component, SuffixIdentifier, Type, components, launch_subprocess)

def run_client() -> None:
    from .TPClient import main
    launch_subprocess(main, name="TwilightPrincessClient")

components.append(Component(
    "Twilight Princess (Dusklight) Client",
    func=run_client,
    component_type=Type.CLIENT,
    file_identifier=SuffixIdentifier(".aptp"),
))
```

**This also answers David's earlier "I need a place to connect to the AP
server / enter password / player name" ask** — rewriting our client on
AP's `CommonClient` base (what all those clients use) gives connection
prompts, the command console (`!getitem`/`!hint`/chat, replacing our stdin
hack), and auto-reconnect for free, in the UI players already recognise.
It should also *shrink* our code, since CommonClient owns the handshake,
reconnection and DataPackage handling we currently hand-roll. Strongly
preferred over building custom in-game UI (expensive UE3 GFx work, and
non-idiomatic).

**Interim, built now:** `client/Start Batman AP Client.bat` — double-click
launcher that prompts for server / slot / password / test-mode with
sensible defaults. Explicitly a stopgap so testing isn't blocked on the
proper integration; the .bat header says so.

**DONE (2026-08-10): ported to CommonClient.** David asked why we weren't
using AP's own client machinery — fair question, and the original raw-
websockets choice was a miss on my part (I looked up the World API before
writing the apworld, but didn't check what AP provided for clients).

Clarification worth keeping: **`ArchipelagoTextClient.exe` can't be used
directly** — it's generic, with no idea how to detect a trophy pickup or
push a gadget into the game. But it's built on `CommonClient`, and that
*is* the right base.

New `apworld/BatmanClient.py`, modelled on TP Dusklight's `TPClient.py`
(near-identical situation: that client also talks to its game over a
socket rather than via emulator memory).
- `BatmanACContext(CommonContext)` — `game`, `items_handling = 0b111`,
  `server_auth`, `on_package`.
- `BatmanACCommandProcessor` — adds `/game` (is the client attached to the
  game?) and `/resync` (force re-push gadget state).
- `game_bridge_task()` — owns the 127.0.0.1:7777 socket, reconnects
  forever, re-asserts gadget state on every (re)connect.
- Feed rendering now uses AP's own `NetUtils.JSONtoTextParser` instead of
  our hand-rolled renderer, with ANSI colour codes regex-stripped so they
  don't show as garbage in the HUD. **This fixes the earlier limitation
  where other games' items rendered as `Item#1234`** — CommonContext
  fetches the DataPackage automatically.
- **Goal condition now actually works end-to-end**: added
  `fill_slot_data()` to the World returning `trophy_goal`; the client
  counts received "Riddler Trophy" items and sends
  `ClientStatus.CLIENT_GOAL` on reaching it. Previously nothing ever
  reported completion.

Registered in `__init__.py` via `LauncherComponents`, so it appears as
**"Batman: Arkham City Client"** in `ArchipelagoLauncher.exe`. No
terminal, no .bat.

Verified: all files parse, apworld repackaged and installed, and
generation still succeeds with the component registered (`Batman: Arkham
City : v0.1.0 | Items: 13 | Locations: 234`). Imports checked against
AP's real source rather than assumed — `JSONtoTextParser` (called with a
list of JSONMessagePart, returns str) and `ClientStatus.CLIENT_GOAL` both
confirmed to exist.

Old `client/ap_client.py` + the .bat are deliberately left in place and
working until the new one is proven in a live run.

**CONFIRMED WORKING (2026-08-10).** David launched it from the
Archipelago Launcher, connected, and the game stripped his gadgets with
events reporting correctly. Verified: client attached to both the game
bridge (7777) and the AP server (38281), 10 gadgets in the stripped pool.
The whole chain — Launcher → CommonClient → TCP bridge → BmSDK → game —
works end to end with no terminal, no .bat, no manual steps.

Server note: had to restart on a **fresh seed**
(`AP_65778715848899448051`) because the running one predated
`fill_slot_data`, so it had no `trophy_goal` and carried stale checks.

## Toast styling — switched to the XP message widget (2026-08-10)

**Big milestone: the gadget wipe AND the on-screen text both confirmed
working in-game.** First time the whole chain has been visible to the
player.

David asked for the feed to be smaller/cornered, since with several
players in a multiworld it'll fire constantly and the big objective
banner would wreck gameplay. Searched the decompiled HUD for
alternatives; best fit found:

`RGFxMovieHUD.QueueXPMessage(string Title, string Message, float
HoldDuration)` — the small combat XP notification ("Combo Bonus / 10
XP"). Reached via `pc.HudMovieNew.HealthBars[pc.HudMovieSide]`, the exact
call path `RExperienceSystem` uses. Three reasons it's the right pick
over the objective banner:
- Compact and self-fading by design.
- **Queued** (`XPMessage.AddItem` in the Flash movie) — rapid-fire
  multiworld messages stack properly instead of stomping each other.
- Takes an explicit hold duration.

`TOAST` now routes here via a new `ApBridge.ShowToast()`, wrapped in
try/catch with null/bounds guards (HUD may not exist yet on load).

**Deliberately left the item-receipt popup on `QueueObjectiveMessage`.**
That fires rarely (only ~12 gadget items in a seed) and is a "you got
something" moment worth the prominence; the PrintJSON *feed* is the
high-frequency one that needed shrinking.

**Known limitation:** position is baked into the Flash movie
(`_root.HUD.Contents.XPMessage`), so it lands wherever the XP widget
lives, not necessarily top-left. Actually moving it would require editing
the SWF — real GFx work. Also untested whether long feed strings
("PlayerX sent Explosive Gel to PlayerY") overflow the widget.

Not yet rebuilt/tested.

**David's follow-up: "what if someone has a level up waiting?"** Real
collision concern, investigated:
- **Level-up is safe.** It targets a *different* Flash object
  (`_root.HUD.Contents.HPXPBar.ShowLevelUp`) than XP messages
  (`_root.HUD.Contents.XPMessage.AddItem`), so our toasts can't block or
  delay a pending level-up.
- **Combat XP messages do share our queue**, though, so mid-fight the
  feed would interleave with "Combo Bonus / 10 XP" and get noisy.
- Considered `RHudExtensionRoomName.TriggerRoomName()` (reachable via
  `HudMovieNew.RoomNameMovie`) as an independent corner channel, but
  **rejected it**: it's a plain `SetText` with no queue, so rapid
  multiworld messages would stomp each other — strictly worse for the
  exact problem David is worried about.

**Real fix chosen: send fewer messages, not find a different widget.**
Added `is_relevant_to_me()` filtering to the client. By default only
events actually involving this slot reach the HUD (items received by me,
items sourced from my world, plus Goal/Countdown/Chat). Other players
trading among themselves — the bulk of traffic in a big multiworld —
stays console-only. `--verbose-feed` opts back into everything. This is
standard AP client behaviour and cuts HUD frequency dramatically, which
also largely defuses the combat-XP collision.

Still true: genuinely custom placement/styling (a real top-left feed)
would require editing the SWF. Not attempted.

## Bridge auto-reconnect (2026-08-10)

After a rebuild David asked how to connect and it looked like everything
was already running — server, bridge, AND a python client process with the
correct command line. But `netstat` showed the client had **zero
established connections** to either endpoint. Cause: rebuilding restarts
the game (bridge PID changed 22716 → 38544), which silently killed the
client's socket, leaving a live-but-useless process.

This is a constant workflow problem, since every script change means a
rebuild. Fixed properly rather than telling David to restart the client
every time:
- `connect_bridge()` now retries indefinitely (2s interval) instead of
  throwing, so the client can also be started *before* the game.
- `bridge_listen_loop()` catches drops, reconnects, and then calls
  `push_gadget_state()` on the fresh instance — so a rebuilt game gets
  re-stripped and re-synced automatically with no manual step.

**Diagnostic worth reusing:** a running client process proves nothing.
`netstat -ano | grep ":7777" | grep ESTABLISHED` is what actually tells
you whether the client is attached to the game.

## Strip-too-early bug (2026-08-10)

Loading in with the client already connected, the strip fired before the
game was ready and did nothing. Root cause: `IsReady()` was too weak — it
only checked pawn/controller/InvManager exist. Those come up early, but
the **gadget wheel populates later**, so `ApplyDesiredState` ran against
an effectively empty inventory, stripped nothing, and cleared the pending
state so it never retried.

Two fixes:
1. **Stronger `IsReady()`** — now also requires
   `invManager.PCSelectableGadgets.Count > 0` (wheel actually rebuilt from
   `ACP_Details.GadgetsPC`) and at least one live `RInventoryGadget`
   actor.
2. **Settle delay** — even after ready, wait 1.5s before applying, since
   the wheel can fill in progressively and an immediate strip could miss
   gadgets arriving a moment later. The timer measures from when readiness
   was reached, and resets if readiness is lost again.

David's workaround suggestion (run the .bat only after loading in) should
no longer be necessary, though it remains a valid fallback.

## LOGIC BREAKTHROUGH: guide numbering == PickupIndex (2026-08-10)

Context correction: logic was never built. It was raised during the guide
read-through, three options were offered, and David chose "leave it as-is
for now" — so all 234 locations have no access rules. **That means a live
softlock risk**: nothing stops the generator placing Explosive Gel behind
a wall that needs Explosive Gel. AP won't flag it (we told it everything
is reachable) but the player would be physically stuck — exactly the
circular dependency the project set out to avoid on day one.

**Then a hypothesis worth testing appeared.** Comparing our captured
`PickupIndex` ranges against the GamesRadar guide's own numbering:

| Our zone | n | index range | Guide zone | n | range |
|---|---|---|---|---|---|
| Museum | 23 | 1-23 (contiguous) | museum | 23 | 1-23 |
| OWE | 39 | 1-39 (contiguous) | the-bowery | 39 | 1-39 |
| OWA | 34 | 1-36 | park-row | 36 | 1-36 |

Two exact contiguous matches plus a max-index match. Suggested the
guide's numbering *is* the game's `PickupIndex`. Deliberately did **not**
build logic on it yet — a half-correct mapping produces confidently wrong
rules, which is worse than no rules.

**Decisive test run, and CONFIRMED.** Guide Museum #3 is described as "on
the catwalk above the T-Rex" — David collected it and the hook logged:
```
2026-08-09T23:49:28,RiddlerLoc_Museum,RiddlerType_Pickup,3
```
Guide numbering == PickupIndex. The correlation problem that blocked
per-trophy logic for this whole project is solved, and all ~229 parsed
requirement descriptions become directly usable.

**Remaining gap: zone code -> district mapping.** Confirmed: Museum,
Steel. Likely: OWE=the-bowery (39/39), OWA=park-row (36/36). Unknown:
OWB, OWC, OWD, Underworld. Unassigned guide zones: amusement-mile,
industrial-district, subway, wonder-city. Note the counts don't cleanly
resolve these (e.g. OWD tops out at 27, but no guide zone has max 27), so
this can't be inferred from numbers alone.

**Approach for resolving it — ask the game, not the save.** David
suggested mining the 100% save. Tried Gildor's `decompress.exe` on it
first: **rejected** ("Wrong package tag (00048000)... probably
encrypted") — it's a package-level tool expecting a `.upk` header, not a
raw UE3 chunk decompressor, so it can't read saves. Rather than fight LZO,
realised the running game already knows: `RPickupBase.GetPickedUpName()`
is `"PickedUp_" + GetLevelName() + FlagName + PickupIndex`, so it embeds
the streaming level name.

New `DumpZoneNames.cs`, **P** key. First version dumped
`GetPickedUpName()` per zone — **that failed**: level names turn out to be
just the zone codes again (`PickedUp_OWA_Pickup_11`), no district names.
Rewrote it as a simple "where am I" reporter: nearest trophy's zone plus a
per-zone count of everything loaded. Works on a 100% save since pickup
actors persist after collection.

**ZONE MAPPING COMPLETE (2026-08-10).** David stood in each district and
pressed P:

| Zone code | District | How established |
|---|---|---|
| OWA | Park Row | 36↔36 + first fresh-game pickup was OWA |
| OWB | Amusement Mile | ✅ observed (GCPD) |
| OWC | Industrial District | ✅ observed (Joker's Funland) |
| OWD | Subway | ✅ observed |
| OWE | The Bowery | ✅ observed |
| Underworld | Wonder City | forced (only zone ≤25) |
| Steel | Steel Mill | name |
| Museum | Museum | ✅ T-Rex index test |

Batman trophy totals sum to **247**, exactly matching the guide totals —
the model is fully self-consistent.

**The Catwoman discovery.** There is only ONE pickup class
(`RPickup_Riddler`) — no Catwoman variant — so Catwoman's trophies share
the class *and the index space*, sitting ABOVE each zone's Batman count.
This explained a contradiction that had been breaking the inference
(OWD max index 27 vs Subway's 26 Batman trophies — index 27 is a Catwoman
trophy). Those are excluded entirely: Batman cannot collect them, so
including them would create permanently uncheckable locations.

## LOGIC BUILT AND WORKING (2026-08-10)

- `Locations.py` regenerated: **247** locations (was 234 — the old table
  was only what we happened to capture by flying around, so any trophy we
  hadn't visited would have been an unrecognised check at runtime).
- New `Rules.py`, generated from the parsed guide text:
  - `LOCATION_REQUIREMENTS` (241) — all listed gadgets required.
  - `LOCATION_ANY_REQUIREMENTS` (2) — either/or. The text parser treated
    "line launcher **or** freeze blast" as requiring both; hand-corrected
    after reading each of the 3 affected entries (the third, Bowery #17,
    turned out to be free entirely — its alternative is a dive-bomb glide,
    which needs no gadget).
  - `UNKNOWN_LOCATIONS` (4) — Subway 9/10/23/24, where the guide merges
    entries under combined headings like "22-23-24" so no text is
    attributable. Marked `LocationProgressType.EXCLUDED` rather than
    guessed at.
- Requirement distribution: Explosive Gel 34, Freeze Blast 27, Magnetic
  Blast (= Remote Electrical Charge) 25, Line Launcher 20,
  Remote-Control Batarang 14, Disruptor 11. 127 need nothing.
- Deliberately **not** gated: Batclaw, grapnel/glide/dive-bomb, and the
  Cryptographic Sequencer (story unlock, not part of the wheel system).

**Fill error found and fixed — caused by the logic actually working.**
First generation attempt failed:
`FillError: Not enough locations for progression items. There are 101
more progression items than there are available locations.` Cause: all
~235 Riddler Trophies were classified progression. That was survivable
when every location was reachable, but once 114 locations sit behind
gadgets the initially-reachable sphere is far too small to hold them.
Fixed by marking only `goal * 1.25 + 5` trophies as progression and the
rest filler — AP guarantees reachability for progression items, so the
goal stays satisfiable while fill regains room.

**Verified in the spoiler, not just by exit code:** the playthrough now
has real spheres (0 = starting kit, 1 = gadget-free locations only),
where previously everything was one flat sphere. Gadgets placed at
reachable spots (Explosive Gel at Museum_13, Line Launcher at OWD_3,
etc.). **The softlock risk is now properly dead.**

**Scope arithmetic, corrected** (David caught an overcount of 7 in a
first pass): 247 Batman physical trophies + 113 Batman riddles + 40
physical challenges = 400 base game, + 40 Catwoman = **440** total
Riddler challenges. Riddles is a residual, forced by the other three
rather than independently counted.

## Upgrades layer (2026-08-10)

David asked to lock combat moves and randomize armour/health. Researched
the upgrade system and found a clean mechanism.

**How upgrades work:** `RPlayerController.UpgradeItems` is a **config**
array, defined in `BmGame/Config/DefaultGame.ini` (82 entries). Each is
unlocked by a global flag `"Unlocked_<ItemName>"`, or
`"Unlocked_<ItemName><stage>"` for staged ones — confirmed by
`RCheatManager`'s unlock-all, which sets exactly those. Same `FlagManager`
mechanism already proven in this project.

**Critical distinction found before building anything:** only *buyable*
upgrades are actually honoured at runtime. Searched every reference:

| Flag | Runtime references |
|---|---|
| `Unlocked_Counter`, `_Strike`, `_Evade`, `_Beatdown`, `_Redirect`, `_Stun`, `_Takedown` … | **0** |
| `Unlocked_DoublePowerCombo` | 3 |
| `Unlocked_SuperComboGadgets` | 6 |
| `Unlocked_BallisticArmour` / `_MeleeArmour` / `_Shockwave` / `_Batswarm` | 1 each |

Base combat moves' flags exist **only to draw the upgrade menu** — clearing
them greys out a menu entry and changes no behaviour. So they cannot be
locked this way, and pretending otherwise would have shipped a broken
feature.

**Built: 21 buyable upgrades = 27 unlock flags.** Armour is Arkham City's
health system (no separate health upgrade exists); both armour tracks have
4 stages, modelled as AP **progressive** items.
- `Items.py`: `PROGRESSIVE_UPGRADES`, `SINGLE_UPGRADES`,
  `UPGRADE_FLAG_INFO`. Classified `useful` — real power, but they never
  gate a trophy location.
- `Options.py`: `randomize_upgrades` toggle, with the base-move caveat
  documented in the option description itself.
- New `UpgradePool.cs`: `ApplyDesiredUpgrades()` sets all 27 flags to
  match AP state exactly (idempotent, same converge-don't-patch design as
  gadgets).
- `ApBridge.cs`: `SET_UPGRADES` command with the same deferred-apply
  guard as gadgets (flag manager isn't up immediately on load).
- `BatmanClient.py`: tracks `upgrade_counts`, expands progressive counts
  into stage flags (2 copies -> `BallisticArmour0`, `BallisticArmour1`),
  pushes on connect / `GAME_LOADED` / item receipt.

**Verified by generation:** 34 item types, 247 locations, clean fill.
Spoiler confirms all 27 placed — 4x Progressive Ballistic Armour, 4x
Progressive Melee Armour, and all 19 singles.

**XP-bypass closed (2026-08-10).** The in-game upgrade menu could still
sell the same upgrades AP distributes. Traced the purchase path to
`RGFxMovieBackScreen.UnlockUpgrade()`, which decrements
`PersistentShared.UnlockablesToSpend` — the spendable-points currency.
Chose to **hold that at zero** rather than block the menu or revert
purchases:
- XP and levelling keep working, so that feedback loop is intact.
- The menu still opens, so it remains a useful view of what AP has
  granted. Blocking it would remove that for no benefit.
- `UnlockablesToSpend != 0` also drives the "upgrades available" nag
  prompt (`RPlayerController` 15670/16461/16554), so that disappears too.
- Better than letting a purchase happen and reverting it, which would
  read as a bug to the player.
- It's a value the game itself writes (`RCheatManager` sets it to 0), so
  poking it is supported rather than a hack.

Implemented as a throttled sweep (500ms) in `UpgradePool.Tick()`, because
levelling up grants new points — a one-shot zero wouldn't hold. Gated on
a new `SET_SUPPRESS_UPGRADE_POINTS` bridge command, driven from
`randomize_upgrades` in slot data, so vanilla behaviour is untouched when
the option is off.

## Counter lock — experimental, opt-in (2026-08-10)

Researched whether Batman's Counter can be locked. Findings:
- **No global player-level switch exists.** Every counter boolean is
  per-enemy-attack: `bCanCounter` on `RCombatMove_VillainAttack`,
  `bDisableCounter` on `RCombatMove_VillainCloseAttack`. (`bAllowCounter`
  is Grundy-boss-specific.)
- **But that per-attack path is game-sanctioned** —
  `RBMBehaviour_CombatRifle` already sets `bDisableCounter = true` on its
  own attack. So marking every enemy attack uncounterable uses the
  engine's own mechanism rather than fighting it.
- Rejected the alternative of hooking `RCombatMove_BatmanCounter`: its
  entry point `SetCounterInfo` is `native`, and native calls are exactly
  what produced this project's `ExecutionEngineException` crashes.

**Design concerns raised to David before building** (he asked for it
anyway, with the caveats documented for players — a reasonable call):
Counter is the core defensive mechanic; without it fights with 3+ enemies
are close to unwinnable, and enemy-guarded trophies could become
practically unreachable while logic still considers them fine. Randomized
armour already raises combat difficulty considerably on its own.

**Built as opt-in, as an AP item rather than a plain difficulty switch**
(fits the randomizer — Counter stays locked until received):
- `Options.py`: `randomize_counter` Toggle, with the full warning written
  into the option description so it surfaces in the YAML template players
  actually read.
- `Items.py`: `COMBAT_MOVE_ITEMS` = {"Counter"}.
- New `CounterLock.cs`: throttled sweep (250ms, not per-frame — it's a
  `FindObjects` call) setting `bCanCounter=false` / `bDisableCounter=true`
  on all villain attack moves while locked. Attack move objects churn
  constantly during combat, so a one-shot pass wouldn't hold.
- `ApBridge.cs`: `SET_COUNTER_LOCKED,0|1`, plus `CounterLock.Tick()` in
  `OnTick`.
- `BatmanClient.py`: reads `randomize_counter` from slot data, tracks
  receipt, pushes state on connect / load / item receipt. Never locks
  Counter unless the YAML opted in.

Generation verified: 35 item types, 247 locations, clean fill. **Not yet
tested in-game** — the sweep approach in particular is unproven.

## PopTracker pack (2026-08-10)

Built and installed to
`D:\poptracker_0-35-1_win64\poptracker\packs\batman_arkham_city_ap_0.1.0.zip`
(copy also kept in the project at `poptracker/`).

**Reference used:** David already had PopTracker with three AP packs.
Studied `dark_cloud_1_ap_0.1.0.zip` (223KB, compact) rather than the TP
one (30MB, map-art heavy) — far better template for a pack with no
existing art.

**Everything is generated from the apworld itself** (`Items.py`,
`Locations.py`, `Rules.py`) via
`scratchpad/build_poptracker.py`, so the tracker can't drift out of sync
with the randomizer. Regenerate after any apworld change. The script
stubs Archipelago-only imports the same way `generate_game_data.py` does.

Contents: 35 items, 247 locations across 8 districts, **116 with real
access rules** carried over from `Rules.py`.
- ALL requirements -> one comma-joined rule string (PopTracker ANDs
  within an entry).
- ANY requirements -> separate array entries (PopTracker ORs across
  entries), so the two either/or trophies behave correctly.
- The 4 unknown-requirement Subway trophies are labelled
  "(requirements unknown)" in their display name.

**Art, without any art assets existing:**
- Item icons are generated as distinct coloured tiles - a minimal PNG
  writer built on `zlib`/`struct`, since PIL isn't installed. Placeholders,
  but stable and visually distinguishable.
- **The map is real.** Rather than an arbitrary grid, the 233 trophies
  with known coordinates are projected from actual in-game X/Y into image
  space (Y flipped), so pins sit in roughly their true relative positions
  over a generated dark grid background.
- Location markers use **PopTracker's built-in defaults** - discovered the
  DC1 pack references `close.png`/`open.png` without shipping them, and
  PopTracker has `assets/closed.png`/`open.png` of its own. Omitting
  `chest_*_img` gets the standard squares for free and avoids copying
  another pack's assets (which would be dubious to redistribute).

**Autotracking** follows the standard AP pattern: `archipelago.lua` with
`onClear`/`onItem`/`onLocation` handlers, plus generated
`item_mapping.lua` (AP item id -> code + kind) and `location_mapping.lua`
(AP location id -> `@District/Trophy N/Trophy`). Progressive armour
advances stages; Riddler Trophy is a consumable counter (max 247).

Not yet opened in PopTracker - untested.

**Icon extraction attempt (2026-08-10) — did not pan out.** Tried to
replace the placeholder tiles with real in-game art using `umodel`
(already in the project folder; it supports Arkham City via
`-game=batman2`).

What worked:
- `umodel_64.exe -export -png -game=batman2 -out=<dir> Startup.upk`
  exported **450 textures** cleanly. Kept in
  `scratchpad/startup_tex/` if ever wanted.
- `Startup.upk` holds the Scaleform movies: `MapScreen`, `Map_OW`,
  `Map_Museum`, `Map_Steel`, `Map_Under`, `UpgradeScreen`,
  `RiddlerScreen`, plus per-side-mission map overlays.

What didn't:
- **Gadget wheel icons aren't extractable as textures.** Despite
  `RHudExtensionGadgets.SetGadgetIconName("Icons_" + acronym)` implying an
  `Icons_BM` asset, a binary grep across all 2663 packages found no such
  string, and no HUD SwfMovie exists in `GFxUI.upk` (class definition
  only) or any HUD-named package. They're almost certainly vector shapes
  inside a Scaleform movie.
- **The map is vector art too, not a bitmap.** `Map_OW`'s textures are
  only small UI decorations; the largest map-ish texture
  (`Map_OW_Azrael_I3F`, 1024x1024) turned out to be Azrael's mystical
  circle overlay, not city geometry.
- `UpgradeScreen` has 86 textures at 128x64 — these ARE genuine
  per-upgrade preview art, but they're dark, wide-aspect screenshots,
  unreadable at icon size, and their names (`_I102` etc.) carry no
  mapping back to which upgrade they belong to.

**Second attempt with JPEXS (2026-08-10).** David asked to pursue it, so
downloaded JPEXS FFDec 26.2.1 portable to `ffdec/` (Java 21 already
present). **Built a complete, working extraction pipeline:**

1. `decompress.exe <package>.upk` - required first; UPKs are compressed,
   which invalidated the earlier raw `grep` for `Icons_BM` (a string
   inside compressed data is unfindable, so that search proved nothing).
2. Scan the decompressed package for `CFX`/`GFX` magic - Scaleform movies
   are stored in `SwfMovie.RawData`. Startup.upk yielded exactly 35
   streams, matching its 35 SwfMovie objects.
3. zlib-decompress each CFX and rewrite the header as uncompressed `GFX`.
4. `ffdec.bat -format shape:png -export shape <out> <file>.gfx`

**Proven working** - pulled 194 vector shapes out of `UpgradeScreen` as
PNGs. So the capability is real and reusable.

**But the gadget icons still weren't found.** Checked the four most likely
packages:
- `Startup.upk` - 35 movies (maps, UpgradeScreen, RiddlerScreen...), no
  HUD. UpgradeScreen's shapes turned out to be UI chrome (buttons,
  frames), not gadget icons.
- `Frontend.upk` - 240 Scaleform streams, but all generic `ImageNN`
  wrappers ~320 bytes each; no icon-named textures.
- `GFxUI.upk` - class definitions only, zero Scaleform streams.
- `Playable_Batman_SF.upk` - only BroadcastAnalyser/RadioScanner. The
  `Icons_<CharacterAcronym>` naming suggested per-character packages, but
  it isn't there either.

Interesting names *do* exist in Frontend's name table (`FrontendIcons`,
`IconSetPackage`, `CWEPIcons`, `Upgrades`) but none resolve to an
extractable SwfMovie or Texture2D.

**FOUND THEM (2026-08-10).** David okayed spending the time, so the sweep
was run and it worked. Full trail:

1. **Read the code instead of guessing.** `RPlayerController` line ~16816:
   `ext_gadgets.Init(self, "GadgetSelect", "ModuleGadgetSelect" +
   HudMovieNew.CharacterAcronyms[HudMovieSide])`, and sibling modules use
   the pattern `ModuleX.Image`. So the target was
   `ModuleGadgetSelectBM.Image`.
2. **GuidCache.upk** (uncompressed) confirmed the names exist:
   `ModuleGadgetSelectBM/CW/NW/RB`, `StoryModeHUD`, `HudBits` - but no
   matching files on disk, so they're cooked into other packages.
3. **Swept all 2663 packages** with `umodel -list` (background job, ~10
   min). Result: `Playable_Batman_SF.upk` contains
   `Package ModuleGadgetSelectBM`. (Also `Playable_Catwoman_SF` -> CW,
   `Playable_Nightwing_SF` -> NW, `Playable_Robin*_SF` -> RB, and
   `BmGame.upk` holds `StoryModeHUD`/`HudBits`.)
4. **The decisive insight:** decompiling the GFX gave only red 128x128
   placeholder squares. Scaleform stores images *externally* - the real
   bitmaps are `Texture2D` siblings named `<Movie>_I<hex>`. So the icons
   were reachable with plain `umodel` all along, as `Image_I*` textures in
   `Playable_Batman_SF.upk`.

**23 real 128x128 icons extracted** - white silhouettes on transparent:
batarangs (plain, sonic, RC, multi), launcher and REC guns, the gel
canister, claw head, smoke sphere, cluster stars, darts, bolas, plus a few
non-icons (text, glow rings, fx).

Pack rebuilt as **v0.2.0** with **14 real game icons** mapped onto AP items
(remaining items keep generated tiles). Old 0.1.0 removed from the packs
folder so PopTracker doesn't list both. **Caveat: the item->icon mapping is
by visual identification of the extracted art**, so a few of the less
distinct ones (armour tracks especially) are a sensible guess rather than
authoritative.

Reusable knowledge for future asset work:
- **`umodel` cannot export SwfMovie objects** (only Texture2D / Material /
  StaticMesh) - hence the manual CFX extraction pipeline.
- **Raw `grep` on `.upk` files is useless** - they're compressed, so even
  names that are definitely present won't match. Verified with a control
  test on a known string. Decompress first, or use `umodel -list`.
- Scaleform art usually lives in `Texture2D` siblings, not inside the
  movie - check those *before* reaching for a Flash decompiler.
- `pngtool.py` in scratchpad: pure-Python PNG read/write/composite/contact
  sheet, written because PIL isn't installed. Needed because the icons are
  white-on-transparent and invisible until flattened onto a dark
  background.

## Real in-game map + true pin placement (2026-08-10)

The tracker now uses the game's own overworld map, with trophy pins at their
actual positions. Two separate problems: getting the art, and working out
where world coordinates land on it.

### Getting the art
`Map_OW` lives in `Startup.upk` as a Scaleform movie (`movie_11_361310.gfx`
in `scratchpad/swfhunt/gfx/`). umodel can't export SwfMovie, but JPEXS can
**render vector frames to PNG**:

    ffdec.bat -format sprite:png -export sprite <out> movie_11_361310.gfx

Exporting *sprites* rather than the frame matters — the frame export renders
only the stage-sized view. The sprite named **`MapRoot`** (cid 434) is the
whole city at 4586x3383.

Gotchas:
- The city fill renders as **magenta (204,0,255)** — that's a runtime-tint
  placeholder, not fog. It's its own shape (cid 433). Recoloured to slate.
- Frame 1 also draws every map marker at its authoring position (purple
  hexagons, white arrows, red trajectory lines). Removed with
  `ffdec.bat -removeCharacter <in> <out> <ids...>` over the `Icon_*` sprites.
  **Keep `Icon_PlayerLocation_*` and `Icon_Objective_*`** — the green
  building footprints are bundled into those, and removing them strips all
  the interior outlines that make the map readable.
- `export.zoom` config does **not** apply to sprite export.
- No PIL/numpy on this box. Image work goes through GDI+ via
  **Windows PowerShell 5.1** (`powershell.exe`, not pwsh — .NET 8 dropped
  System.Drawing). See `scratchpad/mapproc.ps1`, `maskgen.ps1`, `overlay.ps1`.

### The world -> map transform
MapRoot's placement list ends with three named markers:

    Min    tx=-15543 ty=-19493
    Max    tx= 21309 ty= 12658
    Center tx=  4033 ty= -3494

`Min`/`Max` bracket the world bounds. They land almost exactly on the
district-fill bbox, which pins the twips->pixel chain:

    shape 432 bounds Xmin=-20443 Ymin=-20089, sprite 433 placed at (5269,-1070)
    district bbox in the MapRoot render = (1361, 832) 1866x1712 px
    => px_x = (twips_x + 15174)/20 + 1361     (1 px = 20 twips, zoom 1.0)
       px_y = (twips_y + 21159)/20 + 832

`Center` is **not** the world origin — no scale fits if you assume it is.

**Orientation** took real work. Fitting the trophy cloud to the district
polygon was useless: naive bbox-fit scored 73.5% vs 71.8% for the runner-up
(random is ~55%), and an unconstrained optimiser just shrank the cloud until
everything fit (four orientations at 100%).

What actually settled it: the movie contains **district-labelled landmark
sprites** — `OW_B_GCPD_Shortcut`, `OW_A_Church_ShortCut`, `OW_A_Court_ShortCut`,
`OW_EP_Museum_Shortcut`, `OW_C_SW_Shortcut`, `OW_D1`..`OW_D5`. Their
placements are **true map positions** (verified: each `Icon_Objective_*`
sits within ~4 px of its matching shortcut sprite, and every cross lands
dead on its building outline in the render).

Result: **the map is rotated 90 degrees from world axes.**

    map horizontal =  world Y   (world +Y is map east)
    map vertical   = -world X   (world +X is map north)

Confirmation — each surface district's trophy centroid lands on its own
named landmark:

    OWA -> Courthouse    OWB -> GCPD    OWC -> Steel Mill    OWE -> Museum

### Interiors are separate coordinate spaces
This is why no global transform ever separated the zones cleanly. Museum
trophies span 22800x28500 world units and Steel 20800x13500 — city-sized,
because **each interior is its own streaming level with a local origin**, so
their coordinates overlap each other numerically.

Only OWA/OWB/OWC/OWE share the overworld space and get true positions. The
four interiors (Museum, Steel, Underworld/Wonder City, OWD/Subway) are laid
out around their building anchor instead, which keeps trophies *within* a
room correctly positioned relative to each other. Anchors are in
`INTERIOR_ANCHORS` in `build_poptracker.py`.

Note OWD ("Subway") is an interior space despite the `OW` prefix; `OW_D1..D5`
draw its corridor across the lower middle of the map.

### Also fixed: the map area rendered blank
The layout defined the item panel but never referenced the map layout. The
tracker root needs an explicit dock:

    {"type": "dock", "content": {"type": "layout", "key": "tabbed_maps_horizontal"}}

Pack is now v0.3.0.

## Per-area maps + interior tabs (2026-08-11) — pack v0.4.0

David's request: tabs per area like the Twilight Princess pack, so the
overworld isn't a wall of squares, with **one marker per interior** on the
main map standing in for everything inside it.

This also fixed a real modelling problem — interiors have their own local
coordinate spaces, so they *should* be their own maps rather than the
blob-around-an-anchor hack v0.3.0 used.

### The aggregation mechanism
Straight from the TP pack (`locations/overworld/main map dungeons.json`): the
overworld location carries **`ref` sections** pointing into the interior's own
tree, so one square aggregates many real checks and colours from their
combined state:

    {"name": "Museum (entrance)",
     "map_locations": [{"map": "Arkham City", "x": "192", "y": "916",
                        "size": 30, "border_thickness": 4}],
     "sections": [{"ref": "Museum/Trophy 1/Trophy", "name": "Trophy 1"}, ...]}

Entrance positions come from the movie's own labelled shortcut sprites
(`OW_EP_Museum_Shortcut`, `OW_A_CW_Shortcut`, `OW_DP_Under_Shortcut`,
`OW_C_SW_Shortcut`), so they sit on the real doors.

### Exact map calibration — the good trick
Getting FFDec to render a sprite gives you *its* auto-computed bounds, which
you can't reliably predict (my reconstruction was 9 px out on the overworld
and **500 px** out on the Museum). Don't fight it. Instead:

Every `Map_*` movie places MapRoot on the stage at **(10240, 7680) with
identity scale**, and the stage rect is a known twips rectangle. So rewrite
the SWF frame rectangle to the Min..Max region and render the **frame**, not
the sprite — the render is then exactly that rectangle, and twips->pixels is
exact by construction. Verified: predicted 1904.2x1272.7 px, got 1905x1273.

    <displayRect ...>   <-- the tag is displayRect, NOT frameSize
    ffdec -swf2xml / edit / -xml2swf / -format frame:png -export frame

Because the frame rect is set explicitly, removing characters no longer
shifts the framing - which is what made stripping the marker glyphs safe.

Movie -> map: **05 = CW (Wonder City), 07 = Museum, 20 = Steel, 21 = Under
(Subway), 11 = OW**. Numbering follows package export order from 282.

### Fitting interior trophies
Interior trophies do **not** span their level's world bounds, so the Min/Max
bbox stretch used on the overworld puts them in the void (verified - first
attempt had ~4 of 23 Museum pins inside a room).

Interior maps are sparse (rooms on black), so "fraction of pins landing on a
room pixel" is a sharp objective. Two traps, both hit:
1. Maximising hit-rate alone is **degenerate** - it shrinks everything into
   one big room (all 8 orientations scored 100%). Fix: scan scale downward and
   take the *largest* placement still >=95% on-room.
2. Forcing the overworld's y/-x orientation bunched every map's pins into one
   corner. **Interiors are authored independently and each has its own
   orientation** - Museum x/-y, Steel y/x, Subway -x/-y, Wonder City x/y.
   Letting each map choose roughly doubled the fitted scale.

Final: 95.5-100% on-room against 11-25% background rates.

Caveat worth remembering: these interior positions are a **geometric fit, not
a derived transform** like the overworld's. Relative layout within a floor is
right; absolute placement is eyeballed. Multi-floor interiors are also
flattened into one map, so pins on different floors can overlap in XY.

### Bug found while doing this
The 4 unknown Subway trophies (9/10/23/24) get " (requirements unknown)"
appended to their display name, but refs and the AP location mapping were
built from the *bare* name - so **those four never resolved for autotracking**.
The name is part of the lookup path, so it has to be settled before anything
builds a path to it. Now 247/247 mappings resolve.

## Two item-pool bugs found by the N-key dump (2026-08-11)

Chasing "the REC didn't get stripped" turned up one non-bug and two real
bugs. `DumpGadgetSources.cs` (key **N**) writes `gadget_sources.txt` with the
three candidate enumeration sources side by side; that dump settled all of it.

### Non-bug: the REC was working correctly
`RMagneticBlastBm` *is* an `RInventoryGadget`, *is* found, and *was* stripped.
It got restored because AP genuinely had it: a `!getitem Magnetic Blast`
cheat on 2026-08-10 was still in the room, and the same server had been
running since 08-09. Arithmetic confirmed it — 12 actors found, 9 left in the
pool, 3 restored = Batarang + RC Batarang (starting kit) + the cheat.

Lesson: check what AP thinks you own before blaming the game side. The
"missing" gadget was the one item that had been cheated in.

### Bug 1: two items that could never work
`RBatDistract` and `RFreezeClusterGrenadeBm` appear in
`InvManager.PCSelectableGadgets` but have **no `RInventoryGadget` actor** and
**no `Unlocked_` flag**, so neither the strip/grant path nor the upgrade-flag
path can touch them. David confirmed they aren't selectable in-game at all.

Removed from the pool. Their ids (BASE_ID+12, +15) are **burned, not reused**,
so older seeds stay unambiguous.

### Bug 2: Magnetic Blast gated 25 locations but wasn't progression
The important one. `create_item_classification` marks only `GATING_GADGETS`
as `progression`, but "Magnetic Blast" lived in `NON_GATING_GADGETS` while
`Rules.py` required it for **25 locations**. AP only guarantees reachability
through progression items, so every one of those 25 was unreachable in logic
and generation failed its accessibility check:

    Could not access required locations for accessibility check. Missing: [...]
    Location Accessibility requirements not fulfilled.

This had been happening on **every seed** — the identical 25-location list
appears in the 08-10 generation log too. It was printed mid-generation and a
seed was still produced, so it was easy to miss.

Fixed by moving it into `GATING_GADGETS` (keeping id BASE_ID+11). Generation
is now clean.

**Guard against the whole class of bug:** anything named in
`LOCATION_REQUIREMENTS` / `LOCATION_ANY_REQUIREMENTS` must be classified
progression. Audit with:

    used = every item named in both rule dicts
    assert used <= set(GATING_GADGETS) | STARTING_KIT_ITEM_NAMES

That audit now reports only the 6 gating gadgets, all progression. Worth
re-running after any change to Rules.py or the item lists.

### Also
- `client/` is **legacy** (the old standalone client). The live client is
  `apworld/BatmanClient.py`, which imports `.Items` directly — so
  `client/game_data.json` is stale and no longer consumed by anything.
- Pack rebuilt as v0.4.1 (33 items, was 35).

## Toast styling — reverted to the centre banner (2026-08-11)

Supersedes "Toast styling — switched to the XP message widget (2026-08-10)".

`ShowToast` is back on `pc.QueueObjectiveMessage(4.0f, "Archipelago", <item>, ...)`
— the big centre banner — because **playtesters preferred it**. The Archipelago
heading and the item name read much better centred, and an item arriving from
another world is a big enough event to earn the screen space.

This is the same call `GrantByIndex` already uses for a fresh pickup, so both
paths now look identical. The earlier "small and unobtrusive, top-left" goal
turned out to be the wrong instinct once real players saw it.

## Upgrades: staged flags were OFF BY ONE (2026-08-11)

**Corrects an earlier wrong conclusion in this file.** Setting
`Unlocked_BallisticArmour0` reported success and showed the toast but gave no
armour, and I concluded the flags only drove the menu. That was wrong.

The real cause: **staged upgrade flags are 1-based.** Buying the first armour
rank in the menu sets `Unlocked_BallisticArmour1`, and the package contains
`Unlocked_BallisticArmour4` / `Unlocked_MeleeArmour4` — so the range is 1..4,
not 0..3. We were setting a `...Armour0` that nothing reads, and never setting
the top rank at all.

Proven by buying one in-game and dumping:

    before   UPGRADE_STATE,points=3,unlocked=(none)
    after    UPGRADE_STATE,points=2,unlocked=BallisticArmour1

Fixed 1-based in **both** places, which must stay in agreement:
- `UpgradePool.AllFlagNames()` (game side)
- `BatmanClient.owned_upgrade_flags()` (client side)

### The trap worth remembering
`SetGlobalFlag` **happily creates unknown flags and reports success**. A
misspelt or wrongly-numbered flag therefore fails completely silently — it
looks like the mechanism is broken rather than the name being wrong. Never
trust "granted=N" as evidence that anything actually happened; read the state
back, or buy one in-game and diff.

Audited all 19 non-staged flag names against the literal `Unlocked_*` strings
in BmGame.upk: **all exact matches**, no case mismatches (note both
`Unlocked_BatSwarm` and `Unlocked_Batswarm` exist in the package; ours matches
the latter). So the off-by-one was the only naming bug.

### Cost model (confirmed, currently unused)
Every upgrade costs exactly **1 point** — 3 -> 2 on a single purchase, and
`DefaultGame.ini` has 82 `UpgradeItems` entries with no cost field anywhere.
An XP-currency model (1 item = 1 point, suppressor holds at received-minus-spent)
was considered and works — both halves verified live:

    suppression on  -> points 8 -> 0
    SET_UPGRADE_POINTS,3 -> points 0 -> 3

David chose **individual upgrades** instead, since receiving a named item
should grant that specific thing. The points path stays available as a
fallback and as a diagnostic.

### Operational gotcha
`ProcessQueuedCommands` runs from `OnTick`, so **bridge commands do not
process while the game is paused in a menu** — they queue and fire on unpause.
A dump sent while the upgrade menu was open returned nothing at all, not even
an error.

Also: `SuppressUpgradePoints` is a static that **resets to false on every
script reload**, so XP comes back after a rebuild until the client re-asserts
it.

New diagnostic commands on the bridge (all wipe on the next client sync, so
they can't leave a save cheated):
- `SET_UPGRADE_POINTS,<n>` - grants spendable points; clears SuppressUpgradePoints
  first, otherwise the 500ms sweep zeroes it immediately
- `DUMP_UPGRADE_STATE` - reports points + every `Unlocked_` flag currently set

## State-push consolidation + XP fails safe (2026-08-11, end of session)

### One call asserts everything
Added `BatmanClient.push_all_state()` (gadgets + upgrades + counter). All four
places that assert state now call it:

| trigger | line | was |
|---|---|---|
| bridge connects (already joined) | game restart | all three, inline |
| **AP `Connected` (bridge already attached)** | **new** | **nothing — the bug** |
| `GAME_LOADED` | save load | gadgets only |
| `/resync` | manual | gadgets only |

The `Connected` case was the long-standing ordering bug: the bridge-connect
push is guarded by `if ctx.server and ctx.slot`, so if the bridge attached
first, nothing was ever sent. Whichever of the two connects *second* now
asserts state. This is why counter and XP suppression were never applied in
any session, and why the order of starting things used to matter.

### SuppressUpgradePoints now defaults to TRUE
It's a static, so it reset to `false` on every script rebuild and silently
handed the player free upgrade points until something pushed state again -
observed live (8 points sitting there after a rebuild).

Defaulting to suppressed **fails safe**: worst case points are withheld a
moment longer than needed, rather than the player buying upgrades AP is
supposed to be distributing. The client sends
`SET_SUPPRESS_UPGRADE_POINTS,0` for seeds that aren't randomizing upgrades,
so vanilla games are unaffected as soon as it connects.

### dev_upgrade_test.py
Grants one level of every upgrade (21 flags: `BallisticArmour1`,
`MeleeArmour1`, plus the 19 singles) and **reads the state back to verify**:

    python dev_upgrade_test.py            # grant one level of each, verify
    python dev_upgrade_test.py --clear    # lock everything again
    python dev_upgrade_test.py --dump     # just read current state

Flag names come from `apworld/Items.py` (loaded by path - importing the
package pulls in Archipelago's `worlds` module), so it can't drift.

It verifies rather than trusting the reply on purpose: `SetGlobalFlag` creates
unknown flags and reports success, so `granted=N` proves nothing. Reading back
is the only real check - exactly what would have caught the armour off-by-one
immediately.

Run it with the game in **normal gameplay, not a menu**.

## Prior art / reference points
- TP Dusklight AP (David's other project) — same shape of problem (PC game,
  external client, memory/state hooks) but via Dolphin + Lua instead of raw
  process memory.
- Archipelago's own docs: `docs/apworld_dev_faq.md` and `docs/world api.md` in
  the Archipelago repo — apworld structure reference.

## Gadget strip/grant — crash saga (2026-08-09, evening)

Goal: post-game, strip all gadgets from the player, then grant them back
individually via AP checks. Straightforward in concept, turned out to be the
hardest part of the session so far.

**Dead end 1 — `DebugGiveAllGadgets`.** Decompiled `RCheatManager.uc` showed
this is just `RestoreAmmo()`, which only refills ammo on gadgets already
owned — doesn't unlock anything new, useless for both directions of this
problem.

**Dead end 2 — ammo/flag zeroing.** Tried setting `bSelectable=false` and
`Ammo=0`/`MaxAmmo=0` directly on each `RInventoryGadget`. Confirmed via
per-gadget logging that this ran correctly on all 14 gadgets, but had no
visible effect for several of them (Grapple Gun, Line Launcher, Batarang)
because those gadgets aren't ammo-gated at all — wrong mechanism, not a
script bug (David caught this: "Some of these do not have ammo so removeing
the max does not matter").

**Real gate identified via decompile:** `RGadgetSelectV2.SelectGadget(int)`
depends on `InvMan.GetGadgetName(Gadget) != 'None'` — i.e. actual presence in
the inventory manager's list, not any flag on the gadget object itself. This
means the only correct way to strip is `RInventoryManager.RemoveFromInventory`
/ `AddInventory`, not property poking.

**Attempt A — remove + re-add the same object reference.** Store the
`RInventoryGadget` object on removal, hand it back to `AddInventory` later.
Crashed with `System.ExecutionEngineException` (a severe unrecoverable CLR
crash, not a normal catchable exception). Theory: Unreal's GC destroys the
native object once unreferenced by the engine, so the stored C# reference
goes dangling.

**Attempt B — remove by storing `Class`, spawn fresh on grant.** Redesigned
`StripGadgets.cs` to store each gadget's `Class` instead of the object, and
on grant (`GrantRandomGadget`, J key) spawn a brand-new instance via
`Game.SpawnActor(gadgetClass, ...)` before `AddInventory`. **Also crashed**
with the same `ExecutionEngineException` — but critically, the log showed
the crash happened **immediately after the strip loop finished** (all 14
"Removed X from inventory" lines logged, then the "Stripped 14 gadgets. Pool
now has 14." summary line logged successfully) — **before J was ever
pressed**. This rules out attempt A's stale-reference-in-AddInventory theory
as the cause of this crash, since `AddInventory`/`SpawnActor` were never
reached in this run.

**Current theory (untested):** the player's *currently equipped/selected*
gadget was one of the 14 removed. If the game's own HUD/gadget-wheel logic
(`RGadgetSelectV2`) still holds a "current gadget" pointer to it, the next
frame/update tick that touches that pointer would be touching a removed
object — plausible source of a delayed native crash right after the strip
loop completes.

**Fix applied, tested, did NOT work:** called `RInventoryManager.UnequipAllGadgets()`
once at the top of the strip routine, before the removal loop. Rebuilt, pressed
H, all 14 gadgets removed and logged again, then the same
`ExecutionEngineException` — identical crash signature, immediately after the
strip loop finishes. Rules out the "dangling selected gadget" theory.

**Isolation test — single gadget removal.** Added a `K` key (`StripOneGadget`)
that removes exactly one gadget via `RemoveFromInventory` and nothing else.
**This also crashed**, same exception. This is an important result: it rules
out "removing many at once" or "emptying the whole inventory" as the trigger —
`RemoveFromInventory` appears unsafe on its own, regardless of quantity.

Call stack captured from Visual Studio's Exception Unhandled dialog (this is
the only useful stack trace we've gotten so far, since these crashes normally
report `<Cannot evaluate the exception stack trace>`):
```
[System.ExecutionEngineException unhandled]
[Managed to Native Transition]
BmSDK.dll!BmSDK.Framework.Loader.EngineTickDetour(nint self) Line 88
```
Crash site is BmSDK's own **engine tick hook**, not our script code and not
even inside the `RemoveFromInventory` call itself — meaning `OnKeyDown`
returns cleanly, and it's the game's *next engine tick* (via BmSDK's own
detour) that faults. Consistent with "something got corrupted/freed by the
removal, and unrelated native tick code touches it shortly after."

**Exported the base `Engine` package** (not just `BmGame`) via UE Explorer to
read the real `RemoveFromInventory`/`AddInventory`/`Destroyed` implementations
directly, instead of guessing. Found the actual CLI syntax for UE Explorer
(undocumented in-app, found via EliotVU's own forum):
`"UEExplorer.exe" "path\to\package.upk" -console -export=scripts` — this
avoids the GUI entirely, no `-newwindow`/etc needed. **Caveat: it can get
stuck.** Exporting `Engine.upk` produced 863 `.uc` files then hung, spamming
one repeated error line (`PropertyTag value size error for
'Engine.PlayerReplicationInfo.self[0x1E4]...'`) forever with zero new files —
had to be killed manually via `Stop-Process`. Files exported before the hang
were still complete and valid, so a stuck export doesn't corrupt what's
already written; if this happens again, kill it and check whether the class
you need already made it out before giving up.

**Findings from `Engine\Classes\InventoryManager.uc` /
`Engine\Classes\Inventory.uc`:**
- `RemoveFromInventory` does **not** call `Destroy()`. It only unlinks the
  item from the `InventoryChain` linked list, calls
  `ItemToRemove.ItemRemovedFromInvManager()`, clears
  `ItemToRemove.Inventory`/`InvManager`, and (if this was the current weapon)
  clears `Instigator.Weapon`. The "stale reference to a GC'd object" theory
  (attempt A, earlier) doesn't hold up at the script level — this function is
  logically benign.
- `ItemRemovedFromInvManager()` is a no-op in base `Inventory`
  (`{ return; }`), and `RInventoryGadget` (extends `Inventory` directly)
  doesn't override it. Ruled out as a crash source.
- Interestingly, the relationship is the *reverse* of what we assumed:
  `Inventory.Destroyed()` calls `InvManager.RemoveFromInventory(self)` as
  cleanup — not the other way around. Destruction triggers removal, removal
  does not trigger destruction.
- `RemoveFromInventory` does contain one call to an **unresolved native
  function** — `ItemToRemove.__NFUN_272__(none);` — decompiled as a
  placeholder because it's implemented in native C++, invisible to
  UnrealScript decompilation. This is the most likely real culprit: some
  native-level detach/attachment logic we can't inspect, which may not be
  safely reentrant with however BmSDK's C#→native call bridge invokes it.

**Checked BmSDK's own GitHub (`Team-BmSDK/BmSDK-AC`) for known issues** — no
existing issue or search hit for `RemoveFromInventory`, `inventory`, or
`ExecutionEngineException`. Their docs site (bmsdk.dev/docs) also has no
coverage of native-call safety/object-lifetime caveats. Their support channel
is a Discord (`discord.gg/FN84a5MRsz`) — not something explorable without
David directly.

**Where this leaves us:** the crash is very likely inside BmSDK's native call
marshaling for this specific function (or the unresolved native call within
it), which is below what decompiled UnrealScript or public issue search can
show us. Options going forward, not yet decided: ask in the BmSDK Discord;
try a different/safer removal approach (e.g. one of the `Weapon`-specific
overrides, or manipulating `InventoryChain` more directly rather than calling
`RemoveFromInventory`); or set stripping aside and confirm whether *granting*
(untested — `SpawnActor` + `AddInventory`) works cleanly on its own, since the
crash so far is 100% on the removal side, never reached during a grant.

**New approach (2026-08-09, current) — `PCSelectableGadgets` array, not
`InventoryChain`.** Traced the *actual* gate for gadget-wheel selectability:
`RInventoryManager.GetGadgetName(int gadget_index)` just reads
`PCSelectableGadgets[gadget_index]` (a `name`/FName array) — completely
separate from the generic `Inventory`/`InventoryChain` system. Found a
built-in function that already does exactly what we want:
`RPawnPlayer.ReplaceGadget(name current_gadget, name new_gadget)` — finds a
matching name entry in both `PCSelectableGadgets` and `BM2SelectableGadgets`
and swaps it. Confirmed real usage sites in vanilla code
(`ReplaceGadget('RHarpoonGun', 'RHarpoonGunLv2')` etc.) prove the array
stores literal gadget **class names**, matching `gadget.Class.Name` we
already use.

Rewrote `StripGadgets.cs` around this: strip = `pawn.ReplaceGadget(gadgetName,
"None")` per gadget (hides it from the wheel by blanking its name), grant =
`pawn.ReplaceGadget("None", gadgetName)` (puts a name back into whatever slot
is first blank). Crucially, this **never touches the actual `RInventoryGadget`
actor** — no `RemoveFromInventory`, no `Destroy`, no `AddInventory`/`SpawnActor`
— just plain `name` array element writes, which is a much smaller/safer
surface than anything tried before.

Known unknown: don't yet have a confirmed example of BmSDK marshaling
UnrealScript's `name` (FName) type to/from C#. Guessed `string` for now
(BmSDK's docs advertise "full IntelliSense" over the UnrealScript API, and
many such generators map FName to `string` for ergonomics) — if wrong, the
C# compiler error in Visual Studio will show the real expected type
immediately, cheap to fix. **Tested — no crash, but a real bug found.** Built and ran: H stripped all 14
(logged correctly), J granted all 14 back one at a time (logged correctly,
names round-tripped perfectly) — **and critically, no crash at all**, first
time either direction has survived. `string` was indeed accepted for the
`name` parameter, no compile error.

However, the in-game wheel showed every single slot as "Batarang", and only
Batarang was actually selectable/usable via the wheel — other gadgets (David
confirmed Grapple Gun) still worked fine when otherwise active, just couldn't
be selected through the wheel. Root cause: `ReplaceGadget('None', name)`
finds the *first* blank slot each time, so granting back in a different order
than stripping reshuffled `PCSelectableGadgets`' index order. Something else
— almost certainly `RPawnPlayer.GadgetList[32]`, the fixed-index array of
actual `RInventoryGadget` object references, which we never touched — still
maps grid position → real object by the *original* index. Once the name
array and the object array disagree on ordering, every position's "real
gadget" lookup falls back to whatever's at index 0 (Batarang).

**Fix applied, not yet tested:** stopped using `ReplaceGadget` (too
ambiguous), switched to direct indexed reads/writes on
`invManager.PCSelectableGadgets` — record each gadget's exact original slot
index via a linear scan *before* stripping, write `"None"` to that exact
index on strip, write the name back to that *same* index on grant. Keeps the
name array's ordering identical to `GadgetList[32]`'s untouched ordering the
entire time. Guessed BmSDK exposes the array with `.Count` and a `[i]`
indexer (both read and write) — unconfirmed, will show as a compile error in
VS if wrong.

**Tested — confirmed indexer writes are silently no-ops.** Rebuilt, pressed
H: log correctly found real distinct slot indices for 12/14 gadgets (2 —
`RGrappleGunBm`, `RBatarang_MultiTarget` — not found in the array at all, a
separate minor loose end not yet investigated) and reported them as hidden.
But in-game, **nothing changed** — every gadget remained fully accessible.
Root cause: `invManager.PCSelectableGadgets` almost certainly returns a fresh
marshaled copy on every property access, not a live view — so `invManager.
PCSelectableGadgets[i] = "None"` was writing into a throwaway snapshot that
got discarded immediately, never touching real native memory. This is a
different failure mode than the `ReplaceGadget` scramble bug: that one
proved real native mutation *does* happen via an actual UnrealScript
function call; this one proves raw indexer assignment on this property does
*not* propagate.

**Fix applied, not yet tested:** copy the array to a local `List<string>`
once (`new List<string>(invManager.PCSelectableGadgets)`), mutate the local
copy freely (as many indexed writes as needed), then explicitly reassign the
whole property at the end (`invManager.PCSelectableGadgets = selectable;`)
to force the setter to serialize the full list back to native memory in one
shot. Applied to both `StripGadgets` (one full read-modify-write per H
press) and `GrantRandomGadget` (same pattern per J press).

**Compile error revealed the real type.** `List<string>` guess was wrong —
VS's build errors gave the exact real type: the property is
`BmSDK.TArray<BmSDK.FName>`, not anything convertible to/from
`List<string>`/`IEnumerable<string>`. Individual element comparisons/
assignment against plain `string` already compiled fine before (implicit
FName↔string conversion works fine at the single-element level) — it was
specifically the whole-collection copy/reassign that needed the exact type.
Rewrote using `TArray<FName>` for the local copy (built manually via
`.Add()` in a loop, since constructor-from-`TArray` wasn't confirmed to
exist) and reassigning that directly to `invManager.PCSelectableGadgets`.

**Tested — still no visible effect.** Even full-property reassignment with
the correct `TArray<FName>` type produced no change in-game. Went hunting
for why, and found the real missing piece — see below. (It's possible the
array write itself was actually fine this time and the real problem was
purely the missing refresh step described next; not conclusively
distinguished, and no longer worth chasing since the whole approach has been
superseded.)

**The real mechanism, found via `RPlayerController.GadgetsUpdated()`
(`RPlayerController.uc` ~line 10816):** This function is what actually
matters, and neither array write nor `ReplaceGadget` alone triggers it.
It iterates `BM2SelectableGadgets` (the "console" array — turns out this is
the *primary* one, not `PCSelectableGadgets`), and for each non-`'None'`
entry calls `InvMan.GetGadgetByName(name, false)` to resolve it to the
actual owned `RInventoryGadget` object. If found, it writes `Gadget.HudSlot`
and (via `JoyPadGadgetToKeyboard`, which cross-references
`PCSelectableGadgets` against `BM2SelectableGadgets` by name)
`Gadget.HudSlotPC` — **these two properties on the gadget object itself are
what the wheel actually reads** for position and selection, not the name
arrays directly. Then it calls `HudMovieNew.GadgetSelects[...].
SendGadgetData(...)` to push the result to the HUD.

Critically, `RPawnPlayer.GiveGadget(...)` calls `Controller.GadgetsUpdated()`
after a real grant (`RPawnPlayer.uc` ~line 3220) — but `ReplaceGadget` does
**not** call it. So test 1's `ReplaceGadget`-only version mutated the name
arrays correctly but never refreshed `HudSlot`/`HudSlotPC` from that new
data — whatever visual change appeared was from some *other*, uncontrolled
resync happening later (e.g. opening/closing the wheel), using our
by-then-scrambled array content. This — not a marshaling bug — is the most
likely full explanation for "everything shows as Batarang."

**Also found a clean fix for the `ReplaceGadget` ambiguity problem**, without
needing to solve raw array-write persistence at all: `GadgetsUpdated()`'s
validity check is `CurrentGadgetName != 'None'` *then* `GetGadgetByName(...)
!= none`. A unique per-gadget placeholder (e.g. `"Stripped_3"`) satisfies
`!= 'None'` but still fails `GetGadgetByName` (no real class has that name),
so it's hidden just as effectively as `'None'` — while being fully
unambiguous for `ReplaceGadget` to find again later, regardless of grant
order. No need for direct indexed array access at all.

**Fix applied, not yet tested:** rewrote `StripGadgets.cs` to strip via
`pawn.ReplaceGadget(realName, $"Stripped_{i}")` per gadget (unique
placeholder, not shared `'None'`), grant via
`pawn.ReplaceGadget(placeholder, realName)` (now always an exact,
unambiguous match), and — the actual missing piece — call
`pc.GadgetsUpdated()` after every strip and every grant to force the
HudSlot/HudSlotPC refresh. Needed `pawn.Controller` cast to
`RPlayerController` to reach it; confirmed via decompile that `Controller`
is a valid property accessed the same way in vanilla `GiveGadget`.

**IT WORKS.** Tested — stripping and granting both function correctly
through the wheel now: correct gadget appears/disappears at the right slot,
correct object is actually selected and usable, granting one at a time
works cleanly. This is the first fully-working strip/grant cycle after the
whole saga above. Core mechanism confirmed for the MVP design:
`RPawnPlayer.ReplaceGadget()` (unique-placeholder version, not shared
`'None'`) + `RPlayerController.GadgetsUpdated()` afterward.

Two follow-up issues found during testing:

1. **Last-equipped gadget stays usable after a full strip.** Whatever was
   actively in Batman's hand when H was pressed remained usable even after
   being hidden from the wheel — makes sense, since `RInventoryManager.
   CurrentGadget`/`DisplayedGadget` is a separate "what's currently equipped"
   pointer, untouched by hiding something from the *selectable* list.
   Granting things back one at a time worked fine (no issue there). **Fix
   applied, not yet tested:** added `invManager.UnequipAllGadgets()` to the
   strip routine, after the replace loop.

2. **Doesn't survive a save.** Saved with everything stripped, and on
   reload nothing was stripped anymore. **Expected, not a bug** — this
   approach never touches the real inventory (`RInventoryGadget` objects /
   `InventoryChain` / `GadgetList[32]`), only the derived wheel-selectable
   state, specifically *because* touching the real inventory objects is what
   caused the `ExecutionEngineException` crashes earlier in this saga. The
   save file's ground truth is the real (untouched, still fully intact)
   inventory, so a reload just rebuilds the wheel from what's actually still
   owned, wiping our runtime-only changes. **Implication for the real
   design:** this is fine — the mod will need to reapply strip/grant state
   on every load anyway, driven by AP's own record of what's been received,
   not by anything in the game's native save data. Not something to "fix" at
   the engine level; needs a hook that reapplies state after a save loads
   (not yet built).

**Scope decision (2026-08-09): Batclaw/quick-Batarang left out of stripping,
accepted.** Tested `UnequipAllGadgets()` — after a full strip, the only thing
still accessible was the Batclaw (Grapple Gun), consistent with it never
having been found in `PCSelectableGadgets` in the first place (see the "not
found, skipping" log lines earlier) — it's bound through some separate,
dedicated mechanism outside the wheel's selectable-name arrays
(`QuickGadgets[EQuickGadgetType]` is the likely candidate, not confirmed),
which this approach doesn't touch at all.

David is fine leaving Batclaw and the quick/basic Batarang throw permanently
accessible, not gated by the AP strip system — **decided, not a bug to
fix**. Separately noted (informational, not yet investigated): targeting/
remote control doesn't currently work for the controllable Batarang variant
(`RBatarang_Controllable`/RC Batarang). Worth revisiting if/when Batarang
variants become relevant again, but not an active task right now.

**Where this leaves the MVP mechanism:** strip/grant via
`RPawnPlayer.ReplaceGadget()` (unique placeholder per gadget) +
`RPlayerController.GadgetsUpdated()` + `RInventoryManager.
UnequipAllGadgets()` on strip is the confirmed-working core, for everything
routed through the wheel (12 of 14 found gadgets — Batclaw and
Batarang_MultiTarget are out of scope, accepted). Remaining known gap:
doesn't persist across save/reload (expected, needs an on-load reapply hook
later, driven by AP's own state — see above).
