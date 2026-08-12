using System;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Framework;

// EXPERIMENTAL: suppresses Batman's ability to counter.
//
// Unlike the XP upgrades, base combat moves have no runtime "Unlocked_"
// gate - their flags only drive the upgrade menu. So instead of unlocking
// the player's move, this marks every ENEMY attack as uncounterable, using
// the game's own supported mechanism: RCombatMove_VillainAttack.bCanCounter
// and RCombatMove_VillainCloseAttack.bDisableCounter. The game already does
// this itself (see RBMBehaviour_CombatRifle, which sets bDisableCounter on
// its attack), so we're using a sanctioned path rather than fighting the
// engine.
//
// Attack move objects are created and destroyed constantly during combat,
// so this has to run repeatedly rather than once. It's throttled well below
// per-frame because it's a FindObjects sweep.
public static class CounterLock
{
    // Set by the AP client. When true the player has NOT received Counter
    // yet, so enemy attacks get marked uncounterable.
    public static bool CounterLocked = false;

    private static DateTime lastSweep = DateTime.MinValue;
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMilliseconds(250);

    // Main/engine thread only.
    public static void Tick()
    {
        if (!CounterLocked) return;
        if (DateTime.UtcNow - lastSweep < SweepInterval) return;
        lastSweep = DateTime.UtcNow;

        try
        {
            foreach (var move in Game.FindObjects<RCombatMove_VillainAttack>())
            {
                move.bCanCounter = false;
            }
            foreach (var move in Game.FindObjects<RCombatMove_VillainCloseAttack>())
            {
                move.bDisableCounter = true;
            }
        }
        catch (Exception e)
        {
            Debug.Log($"CounterLock sweep failed: {e.Message}");
        }
    }

    public static string SetLocked(bool locked)
    {
        CounterLocked = locked;
        return $"COUNTER_SET,locked={locked}";
    }
}
