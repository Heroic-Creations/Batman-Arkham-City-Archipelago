from dataclasses import dataclass

from Options import Range, Toggle, PerGameCommonOptions


class TrophyGoal(Range):
    """How many Riddler Trophies you need to receive (not just find) to complete the goal."""
    display_name = "Trophy Goal"
    range_start = 100
    range_end = 234  # matches the confirmed count in trophy_map.csv as of 2026-08-10
    default = 100


class RandomizeStartingKit(Toggle):
    """Vanilla Batman starts with the basic Batarang and Remote-Control Batarang already
    unlocked. If enabled, both are removed from your starting inventory and placed into the
    item pool instead, meaning you'll need to receive them like any other item."""
    display_name = "Randomize Starting Kit"


class RandomizeUpgrades(Toggle):
    """Shuffle Batman's XP-purchasable upgrades into the item pool: armour (which is this
    game's health system), combat upgrades like Batswarm and Double Power Combo, and gadget
    upgrades like Line Launcher Tightrope. You start with none of them and receive them as
    items. 27 unlocks in total.

    Note: base combat moves such as Counter and Beatdown are NOT affected - the game has no
    runtime gate for them, so they cannot be locked this way."""
    display_name = "Randomize Upgrades"


class RandomizeCounter(Toggle):
    """EXPERIMENTAL - read this before enabling.

    Adds Counter to the item pool. Until you receive it, Batman cannot counter attacks.

    WARNING - this can seriously degrade the play experience:

    - Counter is THE core defensive mechanic of Arkham combat. Without it, any fight with
      three or more enemies is close to unwinnable, and there is no substitute (Evade is
      also an ungated base move, and dodging alone will not carry most encounters).
    - Trophies guarded by enemies may become effectively unreachable until Counter arrives.
      The logic does NOT model this, so a seed can be technically completable while being
      practically miserable.
    - The game has no built-in support for disabling the player's counter. This works by
      continuously marking every enemy attack as uncounterable, which is a heavier-handed
      mechanism than the flag-based upgrade locks and is less thoroughly tested.

    Randomizing armour (randomize_upgrades) already makes early combat considerably harder.
    Try that first - you may find you do not need this at all."""
    display_name = "Randomize Counter (experimental)"


@dataclass
class BatmanACOptions(PerGameCommonOptions):
    trophy_goal: TrophyGoal
    randomize_starting_kit: RandomizeStartingKit
    randomize_upgrades: RandomizeUpgrades
    randomize_counter: RandomizeCounter
