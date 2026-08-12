using System;
using System.Collections.Generic;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Framework;

// "Which RiddlerLoc_* zone am I standing in?"
//
// Needed to map the game's internal zone codes (OWA..OWE, Underworld) onto
// real district names (Park Row, Subway, Wonder City, ...). The level name
// turned out to be just the zone code again, so the player supplies the
// district name and this reports the code.
//
// Works on a 100% save: pickup actors still exist once collected, so this
// doesn't require any trophy to be collectable.
[Script]
public class WhereAmI : Script
{
    public override void OnKeyDown(Keys key)
    {
        if (key != Keys.P) return;

        var pawn = Game.GetPlayerPawn(0);
        if (pawn == null)
        {
            Debug.Log("No player pawn yet.");
            return;
        }

        var trophies = Game.FindObjects<RPickup_Riddler>();

        RPickup_Riddler nearest = null;
        float nearestDistSq = float.MaxValue;
        var counts = new Dictionary<string, int>();

        foreach (var t in trophies)
        {
            string zone = t.Zone.ToString();
            counts[zone] = counts.TryGetValue(zone, out int c) ? c + 1 : 1;

            float dx = t.Location.X - pawn.Location.X;
            float dy = t.Location.Y - pawn.Location.Y;
            float dz = t.Location.Z - pawn.Location.Z;
            float distSq = dx * dx + dy * dy + dz * dz;

            if (distSq < nearestDistSq)
            {
                nearestDistSq = distSq;
                nearest = t;
            }
        }

        if (nearest == null)
        {
            Debug.Log("No Riddler pickups loaded nearby - try moving further into the district.");
            return;
        }

        Debug.Log("================ WHERE AM I ================");
        Debug.Log($"  NEAREST ZONE: {nearest.Zone}  (index {nearest.PickupIndex}, "
                  + $"{Math.Sqrt(nearestDistSq):F0} units away)");
        Debug.Log($"  Player at X={pawn.Location.X:F0} Y={pawn.Location.Y:F0} Z={pawn.Location.Z:F0}");
        Debug.Log("  All zones currently loaded (higher count = more likely the one you're in):");
        foreach (var kvp in counts)
        {
            Debug.Log($"    {kvp.Key}: {kvp.Value} trophies loaded");
        }
        Debug.Log("============================================");
    }
}
