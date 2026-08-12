using System;
using System.Collections.Generic;
using System.IO;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Framework;

// Diagnostic: why does StripAll miss some gadgets?
//
// StripAll strips whatever Game.FindObjects<RInventoryGadget>() returns.
// After a full strip the Remote Electrical Charge survived, and it never
// showed up in gadget_pool.csv - so the actor search simply never saw it.
// Same for Freeze Cluster Grenade and Bat-Distract.
//
// The suspicion is that those aren't RInventoryGadget at all. The REC is
// aimed like a weapon, and the SDK exposes RMagneticBlast as its own type
// rather than as a gadget subclass, so a search rooted at RInventoryGadget
// would never reach it.
//
// This dumps the candidate enumeration sources side by side so we can pick
// the right one instead of guessing:
//   1. Game.FindObjects<RInventoryGadget>()   - what StripAll uses today
//   2. InvManager.PCSelectableGadgets         - what the wheel actually shows
//   3. direct searches for the specific missing gadget types
//
// Press N in-game. Writes gadget_sources.txt next to the other dumps.
[Script]
public class DumpGadgetSources : Script
{
    private const string OutputFile = "gadget_sources.txt";
    private static string OutputPath => ApPaths.For(OutputFile);

    public override void OnKeyDown(Keys key)
    {
        if (key != Keys.N) return;

        var pawn = Game.GetPlayerPawn(0) as RPawnPlayer;
        if (pawn == null || pawn.InvManager == null)
        {
            Debug.Log("No player pawn / inventory manager yet.");
            return;
        }

        var invManager = pawn.InvManager as RInventoryManager;
        if (invManager == null)
        {
            Debug.Log("InvManager is not an RInventoryManager.");
            return;
        }

        var lines = new List<string>();
        void Emit(string s)
        {
            lines.Add(s);
            Debug.Log(s);
        }

        Emit("================ GADGET SOURCES ================");

        // --- 1. what StripAll currently enumerates -------------------
        Emit("");
        Emit("[1] Game.FindObjects<RInventoryGadget>()  (what StripAll uses)");
        int n = 0;
        foreach (var g in Game.FindObjects<RInventoryGadget>())
        {
            Emit($"    {n,2}  {g.Class.Name}");
            n++;
        }
        Emit($"    -> {n} found");

        // --- 2. the wheel ------------------------------------------
        // Element type isn't known statically, so this prints the default
        // string form rather than assuming a .Class member exists.
        Emit("");
        Emit("[2] InvManager.PCSelectableGadgets  (what the wheel shows)");
        int w = 0;
        foreach (var entry in invManager.PCSelectableGadgets)
        {
            Emit($"    {w,2}  {entry}");
            w++;
        }
        Emit($"    -> {w} entries");

        // --- 3. the specific gadgets that survived the strip ---------
        Emit("");
        Emit("[3] direct searches for the gadgets that survived");
        DumpType<RMagneticBlast>(Emit, "RMagneticBlast (Remote Electrical Charge)");
        DumpType<RFreezeClusterGrenade>(Emit, "RFreezeClusterGrenade");
        DumpType<RBatarang>(Emit, "RBatarang");
        DumpType<RGrappleGun>(Emit, "RGrappleGun");
        DumpType<RHarpoonGun>(Emit, "RHarpoonGun (Batclaw)");

        Emit("===============================================");

        ApPaths.Write(OutputFile, string.Join(Environment.NewLine, lines));
        Debug.Log($"Wrote {OutputPath}");
    }

    // Reports whether a search rooted at this type finds anything, and what
    // concrete class the instances actually are.
    private static void DumpType<T>(Action<string> emit, string label) where T : GameObject
    {
        int count = 0;
        var classes = new List<string>();
        foreach (var o in Game.FindObjects<T>())
        {
            count++;
            string cn = o.Class.Name.ToString();
            if (!classes.Contains(cn)) classes.Add(cn);
        }
        string detail = count == 0 ? "NONE FOUND" : $"{count} found: {string.Join(", ", classes)}";
        emit($"    {label,-42} {detail}");
    }
}
