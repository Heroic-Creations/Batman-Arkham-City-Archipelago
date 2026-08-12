using BmSDK;
using BmSDK.BmGame;
using BmSDK.Framework;

[Script]
public class UnlockTest : Script
{
    public override void OnKeyDown(Keys key)
    {
        if (key != Keys.G) return;

        var pawn = (RPawnPlayer)Game.GetPlayerPawn(0);
        pawn.DebugGiveAllGadgets();
        Debug.Log("DebugGiveAllGadgets called on pawn directly.");
    }
}
