using System;
using System.Collections.Generic;
using System.IO;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Framework;

public static class GadgetPool
{
    public static List<(string Placeholder, string RealName)> Stripped = new List<(string, string)>();
    private const string PoolLogFile = "gadget_pool.csv";
    public static string PoolLogPath => ApPaths.For(PoolLogFile);

    // Friendly display name for the in-game popup - mirrors
    // apworld/Items.py's AP_NAME_TO_CLASS_NAME, inverted. Keep in sync by
    // hand for now; small, stable list.
    private static readonly Dictionary<string, string> FriendlyNames = new Dictionary<string, string>
    {
        { "RGooSprayBm", "Explosive Gel" },
        { "RFreezeSprayBm", "Freeze Blast" },
        { "RLineLauncherBm", "Line Launcher" },
        { "RBatarang_Controllable", "Remote-Control Batarang" },
        { "RJammerGadgetBm", "Disruptor" },
        { "RResonatorTunerBm", "Sonic Batarang" },
        { "RMagneticBlastBm", "Magnetic Blast" },
        { "RBatDistract", "Bat-Distract" },
        { "RBatarang_MultiTarget", "Multi-Target Batarang" },
        { "RSmokeBombBm", "Smoke Bomb" },
        { "RFreezeClusterGrenadeBm", "Freeze Cluster Grenade" },
        { "RBatarangBm", "Batarang" },
    };

    // Diagnostic only. Called from inside StripAll(), so it MUST NOT throw:
    // an exception here aborted the strip half-done, leaving the player with
    // every gadget removed and none restored.
    public static void LogPool()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Index,RealName");
        for (int i = 0; i < Stripped.Count; i++)
        {
            sb.AppendLine($"{i},{Stripped[i].RealName}");
        }
        ApPaths.Write(PoolLogFile, sb.ToString());
    }

    // True once the player pawn/controller exist AND the gadget wheel has
    // actually been populated. OnEnterGame fires "after actors begin play"
    // but the inventory/wheel fills in later still - checking only for the
    // pawn let a strip run against an empty wheel and silently do nothing.
    public static bool IsReady()
    {
        var pawn = Game.GetPlayerPawn(0) as RPawnPlayer;
        if (pawn == null || pawn.Controller == null || pawn.InvManager == null) return false;

        var invManager = pawn.InvManager as RInventoryManager;
        if (invManager == null) return false;

        // Wheel arrays get rebuilt from ACP_Details.GadgetsPC during load;
        // until they have entries there is nothing meaningful to strip.
        if (invManager.PCSelectableGadgets.Count == 0) return false;

        // And the gadget actors themselves must exist.
        foreach (var _ in Game.FindObjects<RInventoryGadget>())
        {
            return true;
        }
        return false;
    }

    // Strips every gadget currently in the wheel, from a clean slate.
    // Deliberately discards any previous pool contents first - after a
    // save reload the old placeholders are stale (the game rebuilds the
    // wheel arrays from ACP_Details.GadgetsPC on load), so carrying them
    // over would corrupt the mapping.
    // Main/engine thread only.
    public static int StripAll()
    {
        Stripped.Clear();

        var pawn = (RPawnPlayer)Game.GetPlayerPawn(0);
        var pc = (RPlayerController)pawn.Controller;
        var invManager = (RInventoryManager)pawn.InvManager;
        var gadgets = Game.FindObjects<RInventoryGadget>();

        int i = 0;
        foreach (var gadget in gadgets)
        {
            string gadgetName = gadget.Class.Name;
            string placeholder = $"Stripped_{i}";

            pawn.ReplaceGadget(gadgetName, placeholder);
            Stripped.Add((placeholder, gadgetName));
            i++;
        }

        invManager.UnequipAllGadgets();
        pc.GadgetsUpdated();
        LogPool();
        return i;
    }

    // Converges the game to exactly the gadget set the AP client says the
    // player owns: strip everything, then silently restore the named ones.
    // Idempotent - safe to call repeatedly, and safe after a reload since
    // StripAll rebuilds from scratch. Silent (no popups) because this is a
    // bulk state restore, not a fresh item pickup.
    // Main/engine thread only.
    public static string ApplyDesiredState(List<string> desiredClassNames)
    {
        int stripped = StripAll();

        int restored = 0;
        var missing = new List<string>();
        foreach (var className in desiredClassNames)
        {
            int poolIndex = Stripped.FindIndex(entry => entry.RealName == className);
            if (poolIndex == -1)
            {
                missing.Add(className);
                continue;
            }
            GrantByIndex(poolIndex, showPopup: false);
            restored++;
        }

        string note = missing.Count > 0 ? $" (not found: {string.Join(" ", missing)})" : "";
        return $"STATE_SET,stripped={stripped},restored={restored}{note}";
    }

    // Must only be called from the main/engine thread (touches live game
    // objects) - never call this directly from a network/background thread.
    public static string GrantByIndex(int poolIndex, bool showPopup = true)
    {
        if (poolIndex < 0 || poolIndex >= Stripped.Count)
        {
            return $"ERROR,no gadget at pool index {poolIndex}";
        }

        var (placeholder, gadgetName) = Stripped[poolIndex];
        Stripped.RemoveAt(poolIndex);

        var pawn = (RPawnPlayer)Game.GetPlayerPawn(0);
        var pc = (RPlayerController)pawn.Controller;

        pawn.ReplaceGadget(placeholder, gadgetName);
        pc.GadgetsUpdated();
        LogPool();

        if (showPopup)
        {
            string friendlyName = FriendlyNames.TryGetValue(gadgetName, out var name) ? name : gadgetName;
            pc.QueueObjectiveMessage(4.0f, "Archipelago", $"Received: {friendlyName}", "", 0, false, "", false, false);
        }

        return $"GRANTED,{gadgetName}";
    }

    // Must only be called from the main/engine thread. Grants by real
    // UnrealScript class name (e.g. "RGooSprayBm") - what the real AP
    // client sends, since it knows item names, not arbitrary pool
    // positions. Only works for gadgets that were stripped at some point
    // this session (post-game MVP assumption: everything was already
    // owned before stripping) - can't conjure a gadget that was never
    // owned at all.
    public static string GrantByClassName(string className)
    {
        int poolIndex = Stripped.FindIndex(entry => entry.RealName == className);
        if (poolIndex == -1)
        {
            return $"ERROR,{className} not in stripped pool (already granted, or never stripped)";
        }

        return GrantByIndex(poolIndex);
    }
}

[Script]
public class StripGadgets : Script
{
    public override void OnKeyDown(Keys key)
    {
        if (key != Keys.H) return;

        if (!GadgetPool.IsReady())
        {
            Debug.Log("Player not ready yet, can't strip.");
            return;
        }

        int count = GadgetPool.StripAll();
        Debug.Log($"Stripped {count} gadgets from selection. Logged to {GadgetPool.PoolLogPath}.");
    }
}

[Script]
public class GrantSpecificGadget : Script
{
    private static readonly Keys[] NumberKeys =
    {
        Keys.D0, Keys.D1, Keys.D2, Keys.D3, Keys.D4,
        Keys.D5, Keys.D6, Keys.D7, Keys.D8, Keys.D9,
    };

    public override void OnKeyDown(Keys key)
    {
        int poolIndex = Array.IndexOf(NumberKeys, key);
        if (poolIndex == -1) return;

        string result = GadgetPool.GrantByIndex(poolIndex);
        Debug.Log(result);
    }
}

[Script]
public class ResetNearbyTrophy : Script
{
    public override void OnKeyDown(Keys key)
    {
        if (key != Keys.R) return;

        var pawn = (RPawnPlayer)Game.GetPlayerPawn(0);
        var flagManager = ((RGameRI)Game.GetWorldInfo().GRI).FlagManager;
        var persistentShared = ((RGameInfoBase)Game.GetWorldInfo().Game).PersistentShared;
        var trophies = Game.FindObjects<RPickup_Riddler>();

        RPickup_Riddler closest = null;
        float closestDistSq = float.MaxValue;
        int total = 0;
        int flaggedTrue = 0;

        // Diagnostic: dump the actual flag key + result for the first few
        // trophies found, regardless of outcome, so we can see real data
        // instead of just a count.
        int loggedSamples = 0;

        foreach (var t in trophies)
        {
            total++;
            var flagKey = t.GetPickedUpName();
            bool isFlagged = flagManager.GetGlobalFlag(flagKey);
            if (isFlagged) flaggedTrue++;

            if (loggedSamples < 5)
            {
                Debug.Log($"  Sample: Zone={t.Zone} Index={t.PickupIndex} Key=\"{flagKey}\" Flagged={isFlagged} bHasBeenPickedUp={t.HasBeenPickedUp()}");
                loggedSamples++;
            }

            if (!isFlagged) continue;

            float dx = t.Location.X - pawn.Location.X;
            float dy = t.Location.Y - pawn.Location.Y;
            float dz = t.Location.Z - pawn.Location.Z;
            float distSq = dx * dx + dy * dy + dz * dz;

            if (distSq < closestDistSq)
            {
                closestDistSq = distSq;
                closest = t;
            }
        }

        Debug.Log($"Checked {total} trophies total, {flaggedTrue} flagged as already-collected.");

        if (closest == null)
        {
            Debug.Log("No already-collected trophy found nearby.");
            return;
        }

        flagManager.SetGlobalFlag(closest.GetPickedUpName(), false);
        persistentShared.SetSharedFlag(closest.GetPickedUpName(), false);
        closest.bHasBeenPickedUp = false;
        closest.bPendingDelete = false;
        Debug.Log($"Reset trophy: Zone={closest.Zone}, Index={closest.PickupIndex}, Distance={Math.Sqrt(closestDistSq):F0}. Go pick it up again.");
    }
}

[Script]
public class ResetAllTrophies : Script
{
    public override void OnKeyDown(Keys key)
    {
        if (key != Keys.T) return;

        var flagManager = ((RGameRI)Game.GetWorldInfo().GRI).FlagManager;
        var persistentShared = ((RGameInfoBase)Game.GetWorldInfo().Game).PersistentShared;
        var trophies = Game.FindObjects<RPickup_Riddler>();
        int count = 0;

        foreach (var t in trophies)
        {
            if (!flagManager.GetGlobalFlag(t.GetPickedUpName())) continue;

            flagManager.SetGlobalFlag(t.GetPickedUpName(), false);
            persistentShared.SetSharedFlag(t.GetPickedUpName(), false);
            t.bHasBeenPickedUp = false;
            t.bPendingDelete = false;
            count++;
        }

        Debug.Log($"Reset {count} already-collected trophies (in currently loaded/streamed area only - fly around and press T again to catch more). Save afterward to persist.");
    }
}
