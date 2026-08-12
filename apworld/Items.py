from BaseClasses import Item, ItemClassification

# Arbitrary private base offset for this world's item/location IDs.
# Only needs to be unique within a given multiworld seed, not globally
# registered anywhere.
BASE_ID = 3000000

# "Grapple Gun" deliberately excluded - confirmed to be a base ability
# like Batclaw, never wheel-gated, never an AP item.

# Gadgets confirmed (via full read-through of a GamesRadar Riddler trophy
# guide, see notes.md) to actually gate physical trophy locations.
GATING_GADGETS = {
    "Explosive Gel": BASE_ID + 1,
    "Freeze Blast": BASE_ID + 2,
    "Line Launcher": BASE_ID + 3,
    "Remote-Control Batarang": BASE_ID + 4,
    "Disruptor": BASE_ID + 5,
    # Moved here from NON_GATING_GADGETS on 2026-08-11 (keeps its original
    # id so existing ids stay stable). It gates 25 locations in Rules.py, but
    # only GATING_GADGETS is classified progression - so while it sat in the
    # non-gating list every one of those 25 was unreachable in logic, and
    # generation failed its accessibility check on every seed.
    "Magnetic Blast": BASE_ID + 11,
}

# Real, grantable gadgets that never came up as a requirement for any of
# the ~229 physical trophies read through - still valid items, just don't
# gate any of this dataset's locations.
#
# "Magnetic Blast" is the game's internal name for the Remote Electrical
# Charge (confirmed by MetAct_Gadg_RemoteElecCharge in BmGame.upk). Kept as
# the internal name deliberately - renaming it would invalidate seeds.
#
# Bat-Distract (BASE_ID + 12) and Freeze Cluster Grenade (BASE_ID + 15) were
# REMOVED on 2026-08-11. They appear in InvManager.PCSelectableGadgets but
# have no RInventoryGadget actor behind them and no Unlocked_ flag either, so
# neither the strip/grant mechanism nor the upgrade-flag mechanism can touch
# them - and they aren't usable by the player in-game regardless. Leaving
# them in the pool would only have produced items that silently do nothing.
# See gadget_sources.txt (the N-key dump) for the evidence.
# Their IDs are burned, not reused, so old seeds stay unambiguous.
NON_GATING_GADGETS = {
    "Sonic Batarang": BASE_ID + 10,
    "Multi-Target Batarang": BASE_ID + 13,
    "Smoke Bomb": BASE_ID + 14,
}

# Basic Batarang - always in the pool as a normal grantable item, separate
# from the optional starting-kit toggle below (which controls whether it
# starts in your inventory or has to be found).
BASE_BATARANG = {
    "Batarang": BASE_ID + 20,
}

# Real UnrealScript class name (BmGame.Scripts.StripGadgets.cs / GadgetPool)
# for every grantable gadget item - the client needs this to send the
# right name to the game's ApBridge GRANT command. Single source of truth,
# also consumed by client/generate_game_data.py.
AP_NAME_TO_CLASS_NAME = {
    "Explosive Gel": "RGooSprayBm",
    "Freeze Blast": "RFreezeSprayBm",
    "Line Launcher": "RLineLauncherBm",
    "Remote-Control Batarang": "RBatarang_Controllable",
    "Disruptor": "RJammerGadgetBm",
    "Sonic Batarang": "RResonatorTunerBm",
    "Magnetic Blast": "RMagneticBlastBm",
    "Multi-Target Batarang": "RBatarang_MultiTarget",
    "Smoke Bomb": "RSmokeBombBm",
    "Batarang": "RBatarangBm",
}

# XP-purchasable upgrades, from DefaultGame.ini's UpgradeItems list.
#
# These are unlocked in-game by a global flag named "Unlocked_<ItemName>"
# (or "Unlocked_<ItemName><stage>" for staged ones), which is the same
# FlagManager mechanism used elsewhere in this project.
#
# IMPORTANT: only the *buyable* upgrades are here. Base combat moves
# (Counter, Strike, Evade, Beatdown, ...) also have Unlocked_ flags, but
# nothing at runtime ever reads them - those flags only drive the upgrade
# menu display, so clearing them would grey out a menu entry and change no
# actual behaviour. Verified by searching every runtime reference.
#
# Armour is Arkham City's health system; there is no separate health
# upgrade. Both armour tracks have 4 stages, handled as progressive items.
PROGRESSIVE_UPGRADES = {
    "Progressive Ballistic Armour": (BASE_ID + 30, "BallisticArmour", 4),
    "Progressive Melee Armour": (BASE_ID + 31, "MeleeArmour", 4),
}

# AP item name -> the flag suffix used by the game
SINGLE_UPGRADES = {
    "Shockwave Attack": (BASE_ID + 40, "Shockwave"),
    "Glide Boost Attack": (BASE_ID + 41, "GlideBoostAttack"),
    "Heat Signature Mask": (BASE_ID + 42, "HeatSignatureMask"),
    "Batclaw Disarm": (BASE_ID + 43, "BatclawDisarm"),
    "Sonic Batarang Upgrade": (BASE_ID + 44, "SonicBatarang"),
    "Sonic Batarang Shock": (BASE_ID + 45, "SonicBatarangShock"),
    "Line Launcher Tightrope": (BASE_ID + 46, "LineLauncherTightrope"),
    "Freeze Blast Proximity": (BASE_ID + 47, "FreezeBlastProximity"),
    "Sequencer Range Amplifier": (BASE_ID + 48, "ResonatorRange"),
    "Sequencer Accuracy": (BASE_ID + 49, "ResonatorEasy"),
    "Disruptor Weapon Jam": (BASE_ID + 50, "JammerWeaponJam"),
    "Batswarm": (BASE_ID + 51, "Batswarm"),
    "Multi Ground Takedown": (BASE_ID + 52, "MultiGroundTakedown"),
    "Disarm and Destroy": (BASE_ID + 53, "DisarmAndDestroy"),
    "Double Power Combo": (BASE_ID + 54, "DoublePowerCombo"),
    "Reduced Special Move Cost": (BASE_ID + 55, "SpecialMoveCost"),
    "Super Combo Mode": (BASE_ID + 56, "SuperComboMode"),
    "Super Combo Gadgets": (BASE_ID + 57, "SuperComboGadgets"),
    "Super Blade Combo Counter": (BASE_ID + 58, "SuperBladeComboCounter"),
}

# AP item name -> ("SINGLE", flagBase) or ("PROGRESSIVE", flagBase, stages)
# Consumed by the client to build the right flag names to send the game.
UPGRADE_FLAG_INFO = {
    **{name: ("PROGRESSIVE", flag, stages)
       for name, (_id, flag, stages) in PROGRESSIVE_UPGRADES.items()},
    **{name: ("SINGLE", flag) for name, (_id, flag) in SINGLE_UPGRADES.items()},
}

# Base combat moves. These have no runtime Unlocked_ flag, so they can't be
# locked the way upgrades are - the game must be told to suppress them
# directly. Only Counter is supported, and only as an opt-in experiment.
COMBAT_MOVE_ITEMS = {
    "Counter": BASE_ID + 60,
}

# Vanilla starting kit: you normally already own both of these at the very
# start of a new game. The `randomize_starting_kit` YAML option (see
# Options.py) controls whether they stay as a free starting inventory
# (default) or get pulled into the real item pool instead. Both are
# confirmed wheel-gated gadgets with the proven strip/grant mechanism -
# Cryptographic Sequencer was also a candidate for this but is excluded
# entirely from the AP item system (confirmed in-game to be a
# story-progression unlock, not present from the start, and not part of
# the wheel-gated gadget list at all).
STARTING_KIT_ITEM_NAMES = ["Batarang", "Remote-Control Batarang"]

item_table = {
    **GATING_GADGETS,
    **NON_GATING_GADGETS,
    **BASE_BATARANG,
    **{name: iid for name, (iid, _f, _s) in PROGRESSIVE_UPGRADES.items()},
    **{name: iid for name, (iid, _f) in SINGLE_UPGRADES.items()},
    **COMBAT_MOVE_ITEMS,
    "Riddler Trophy": BASE_ID + 100,  # not a gadget - the decoupled counter item
}


class BatmanACItem(Item):
    game = "Batman: Arkham City"


def create_item_classification(name: str) -> ItemClassification:
    if name == "Riddler Trophy":
        return ItemClassification.progression_skip_balancing
    if name in GATING_GADGETS or name in STARTING_KIT_ITEM_NAMES:
        return ItemClassification.progression
    # Upgrades are real power increases but never gate a trophy location,
    # so they're useful rather than progression.
    return ItemClassification.useful
