using System;
using System.Collections.Generic;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Framework;

// Locks and grants Batman's XP-purchasable upgrades.
//
// Upgrades are gated by global flags named "Unlocked_<ItemName>", or
// "Unlocked_<ItemName><stage>" for staged ones - the same FlagManager
// system used for trophy pickup state. Confirmed by RCheatManager's
// unlock-all, which sets exactly these flags.
//
// Only *buyable* upgrades are listed. Base combat moves (Counter, Strike,
// Evade, Beatdown, ...) also have Unlocked_ flags, but nothing at runtime
// reads them - they only drive the upgrade menu, so clearing them would
// change no actual behaviour.
public static class UpgradePool
{
    // flag suffix -> number of stages (0 = single unlock)
    public static readonly Dictionary<string, int> BuyableUpgrades = new Dictionary<string, int>
    {
        // Batsuit - armour is this game's health system
        { "BallisticArmour", 4 },
        { "MeleeArmour", 4 },
        { "Shockwave", 0 },
        { "GlideBoostAttack", 0 },
        { "HeatSignatureMask", 0 },
        // Gadget upgrades
        { "BatclawDisarm", 0 },
        { "SonicBatarang", 0 },
        { "SonicBatarangShock", 0 },
        { "LineLauncherTightrope", 0 },
        { "FreezeBlastProximity", 0 },
        { "ResonatorRange", 0 },
        { "ResonatorEasy", 0 },
        { "JammerWeaponJam", 0 },
        // Combat
        { "Batswarm", 0 },
        { "MultiGroundTakedown", 0 },
        { "DisarmAndDestroy", 0 },
        { "DoublePowerCombo", 0 },
        { "SpecialMoveCost", 0 },
        { "SuperComboMode", 0 },
        { "SuperComboGadgets", 0 },
        { "SuperBladeComboCounter", 0 },
    };

    // Every individual flag name, expanded from the table above.
    public static IEnumerable<string> AllFlagNames()
    {
        foreach (var kvp in BuyableUpgrades)
        {
            if (kvp.Value == 0)
            {
                yield return kvp.Key;
            }
            else
            {
                // Staged flags are ONE-based: buying the first armour rank in
                // the menu sets Unlocked_BallisticArmour1, and the package
                // contains Unlocked_BallisticArmour4 / Unlocked_MeleeArmour4,
                // so the real range is 1..stages.
                //
                // This was 0-based, which meant we set a bogus
                // Unlocked_BallisticArmour0 that nothing reads (SetGlobalFlag
                // happily creates unknown flags and reports success, so it
                // looked like it worked) and never set the top rank at all.
                // That's why granting armour by flag appeared to do nothing.
                for (int stage = 1; stage <= kvp.Value; stage++)
                {
                    yield return kvp.Key + stage;
                }
            }
        }
    }

    private static RFlagManager GetFlagManager()
    {
        return ((RGameRI)Game.GetWorldInfo().GRI).FlagManager;
    }

    // When AP owns the upgrades, the in-game upgrade menu must not be able
    // to hand them out too. PersistentShared.UnlockablesToSpend is the
    // spendable-points currency: the menu decrements it on purchase, and
    // levelling up increments it. Holding it at zero means nothing is ever
    // affordable, while XP and levelling keep working normally.
    //
    // This is a value the game itself pokes - RCheatManager sets it to 0 -
    // so it's a supported thing to write. It also drives the "upgrades
    // available" nag prompt, which conveniently disappears too.
    //
    // Defaults to TRUE deliberately. This is a static, so it resets on every
    // script rebuild - with a false default that silently handed the player
    // free upgrade points until the client happened to push state again,
    // which is exactly what happened during testing. Defaulting to suppressed
    // fails safe: the worst case is upgrade points are withheld for a moment
    // longer than needed, instead of the player buying upgrades AP is meant
    // to be distributing.
    //
    // The client turns this back off (SET_SUPPRESS_UPGRADE_POINTS,0) whenever
    // the seed isn't randomizing upgrades, so vanilla games are unaffected as
    // soon as it connects.
    public static bool SuppressUpgradePoints = true;

    private static DateTime lastPointsSweep = DateTime.MinValue;
    private static readonly TimeSpan PointsSweepInterval = TimeSpan.FromMilliseconds(500);

    // Main/engine thread only. Must run repeatedly, not once - levelling up
    // grants new points.
    public static void Tick()
    {
        if (!SuppressUpgradePoints) return;
        if (DateTime.UtcNow - lastPointsSweep < PointsSweepInterval) return;
        lastPointsSweep = DateTime.UtcNow;

        try
        {
            var gameInfo = Game.GetWorldInfo().Game as RGameInfoBase;
            if (gameInfo == null) return;
            var shared = gameInfo.PersistentShared;
            if (shared == null) return;

            if (shared.UnlockablesToSpend != 0)
            {
                shared.UnlockablesToSpend = 0;
            }
        }
        catch (Exception e)
        {
            Debug.Log($"Upgrade point suppression failed: {e.Message}");
        }
    }

    // Hands the player spendable upgrade points so an upgrade can be bought
    // through the normal in-game menu.
    //
    // Diagnostic: setting Unlocked_BallisticArmour0 directly did NOT actually
    // give Batman the armour, so that flag may only drive the menu rather than
    // the stat behind it. Buying one the normal way lets us observe what the
    // game itself changes instead of guessing.
    //
    // Caller must clear SuppressUpgradePoints first, or Tick() zeroes this
    // again within 500ms.
    // Main/engine thread only.
    public static string SetUpgradePoints(int points)
    {
        try
        {
            var gameInfo = Game.GetWorldInfo().Game as RGameInfoBase;
            if (gameInfo == null) return "ERROR,no game info";
            var shared = gameInfo.PersistentShared;
            if (shared == null) return "ERROR,no persistent shared";

            shared.UnlockablesToSpend = points;
            return $"UPGRADE_POINTS_SET,{points}";
        }
        catch (Exception e)
        {
            return $"ERROR,SetUpgradePoints failed: {e.Message}";
        }
    }

    // Reports the current spendable points and which Unlocked_ flags are set,
    // so a before/after around an in-menu purchase shows what really changed.
    // Main/engine thread only.
    public static string DumpUpgradeState()
    {
        try
        {
            var gameInfo = Game.GetWorldInfo().Game as RGameInfoBase;
            var shared = gameInfo?.PersistentShared;
            int points = shared != null ? shared.UnlockablesToSpend : -1;

            var flagManager = GetFlagManager();
            var set = new List<string>();
            if (flagManager != null)
            {
                foreach (string suffix in AllFlagNames())
                {
                    if (flagManager.GetGlobalFlag("Unlocked_" + suffix)) set.Add(suffix);
                }
            }

            string flags = set.Count > 0 ? string.Join(" ", set) : "(none)";
            return $"UPGRADE_STATE,points={points},unlocked={flags}";
        }
        catch (Exception e)
        {
            return $"ERROR,DumpUpgradeState failed: {e.Message}";
        }
    }

    // Main/engine thread only.
    public static string ApplyDesiredUpgrades(List<string> ownedFlagSuffixes)
    {
        var flagManager = GetFlagManager();
        if (flagManager == null) return "ERROR,no flag manager";

        var owned = new HashSet<string>(ownedFlagSuffixes);

        int locked = 0, granted = 0;
        foreach (string suffix in AllFlagNames())
        {
            bool shouldHave = owned.Contains(suffix);
            flagManager.SetGlobalFlag("Unlocked_" + suffix, shouldHave);
            if (shouldHave) granted++; else locked++;
        }

        return $"UPGRADES_SET,granted={granted},locked={locked}";
    }
}
