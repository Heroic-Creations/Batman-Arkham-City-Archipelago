using System;
using System.IO;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Framework;

public class RiddlerHook
{
    private const string PickupLogFile = "pickup_log.csv";

    [Redirect(typeof(RPlayerController), nameof(RPlayerController.MarkOffRiddlerItem))]
    static void MarkOffRiddlerItemRedirect(RPlayerController self, RPersistentData.ERiddlerLocationName zone, RPlayerController.RiddlerType type, int index)
    {
        Debug.Log($"Riddler item marked off: Zone={zone}, Type={type}, Index={index}");

        ApPaths.Write(PickupLogFile, $"{DateTime.Now:O},{zone},{type},{index}\n", append: true);

        ApBridge.Broadcast($"PICKUP,{zone},{type},{index}");

        self.MarkOffRiddlerItem(zone, type, index);
    }
}

[Script]
public class DisableFocusPause : Script
{
    public override void Main()
    {
        Game.GetEngine().bPauseOnLossOfFocus = false;
        Debug.Log("Focus-pause disabled via script.");
    }
}
