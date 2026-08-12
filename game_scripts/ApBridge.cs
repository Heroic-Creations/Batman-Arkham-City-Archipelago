using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Framework;

// A local TCP bridge so an external process can receive live pickup events
// and send grant commands, instead of polling files. The network threads
// only ever touch the thread-safe queue below - all actual game-object
// access happens on the main/engine thread via Script.Tick, same as
// everything else in these scripts.
public static class ApBridge
{
    private const int Port = 7777;

    private static TcpListener listener;
    private static readonly List<TcpClient> Clients = new List<TcpClient>();
    private static readonly object ClientsLock = new object();
    private static readonly ConcurrentQueue<string> IncomingCommands = new ConcurrentQueue<string>();
    private static bool started = false;

    public static void Start()
    {
        if (started) return;
        started = true;

        listener = new TcpListener(IPAddress.Loopback, Port);
        listener.Start();

        var acceptThread = new Thread(AcceptLoop);
        acceptThread.IsBackground = true;
        acceptThread.Start();

        Debug.Log($"ApBridge: listening on 127.0.0.1:{Port}");
    }

    private static void AcceptLoop()
    {
        while (true)
        {
            try
            {
                var client = listener.AcceptTcpClient();
                lock (ClientsLock) { Clients.Add(client); }

                var readThread = new Thread(() => ClientReadLoop(client));
                readThread.IsBackground = true;
                readThread.Start();
            }
            catch
            {
                return;
            }
        }
    }

    private static void ClientReadLoop(TcpClient client)
    {
        try
        {
            var stream = client.GetStream();
            var buffer = new byte[1024];
            var lineBuilder = new StringBuilder();

            int bytesRead;
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                string chunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                foreach (char c in chunk)
                {
                    if (c == '\n')
                    {
                        string line = lineBuilder.ToString().Trim();
                        if (line.Length > 0) IncomingCommands.Enqueue(line);
                        lineBuilder.Clear();
                    }
                    else
                    {
                        lineBuilder.Append(c);
                    }
                }
            }
        }
        catch
        {
            // client disconnected or errored, fall through to cleanup
        }
        finally
        {
            lock (ClientsLock) { Clients.Remove(client); }
        }
    }

    // Safe to call from the main thread only (e.g. from inside a Redirect
    // hook, which runs on the game's own thread).
    public static void Broadcast(string message)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(message + "\n");
        lock (ClientsLock)
        {
            foreach (var client in Clients)
            {
                try { client.GetStream().Write(bytes, 0, bytes.Length); }
                catch { /* ignore broken clients, cleaned up by their read loop */ }
            }
        }
    }

    // How long the banner stays on screen, in seconds.
    private const float ToastHoldSeconds = 4.0f;

    // Centre-screen objective banner, headed "Archipelago" with the item name
    // under it.
    //
    // This was briefly moved to the compact XP-message widget to be less
    // intrusive, but playtesters preferred the centre banner - the Archipelago
    // heading and the item name read much better there, and an item arriving
    // from another world is a big enough event to earn the screen space.
    // Same call GrantByIndex uses for a fresh pickup, so both paths look alike.
    // Main/engine thread only.
    public static void ShowToast(string message)
    {
        if (!GadgetPool.IsReady()) return;

        try
        {
            var pawn = (RPawnPlayer)Game.GetPlayerPawn(0);
            var pc = (RPlayerController)pawn.Controller;
            if (pc == null) return;

            pc.QueueObjectiveMessage(ToastHoldSeconds, "Archipelago", message,
                                     "", 0, false, "", false, false);
        }
        catch (Exception e)
        {
            Debug.Log($"ShowToast failed: {e.Message}");
        }
    }

    // Desired gadget state the client last told us about, held until the
    // player pawn actually exists. OnEnterGame can fire before everything
    // is spawned, and the client replies almost instantly over localhost,
    // so a SET_GADGETS can easily arrive too early to act on.
    private static List<string> pendingDesiredState = null;

    // Even once IsReady() passes, the wheel can still be filling in. Let
    // things settle briefly before applying, so we don't strip a partially
    // populated inventory and miss gadgets that arrive a moment later.
    private static DateTime pendingReadySince = DateTime.MinValue;
    private static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(1.5);

    // Upgrade flags the client last told us about. Same deferred-apply
    // treatment as gadgets - the flag manager isn't up immediately on load.
    private static List<string> pendingUpgrades = null;

    // Called once per frame from the main thread - drains anything the
    // network threads queued up and actually acts on it here, safely.
    public static void ProcessQueuedCommands()
    {
        while (IncomingCommands.TryDequeue(out string line))
        {
            Debug.Log($"ApBridge received: {line}");
            var parts = line.Split(',');

            if (parts.Length >= 2 && parts[0] == "GRANT" && int.TryParse(parts[1], out int poolIndex))
            {
                string result = GadgetPool.GrantByIndex(poolIndex);
                Broadcast(result);
            }
            else if (parts.Length >= 2 && parts[0] == "GRANT_NAMED")
            {
                string result = GadgetPool.GrantByClassName(parts[1]);
                Broadcast(result);
            }
            else if (parts[0] == "TOAST")
            {
                // Everything after "TOAST," is the message - taken as a
                // substring rather than from Split(), so commas inside the
                // message survive intact.
                string message = line.Substring("TOAST,".Length);
                ShowToast(message);
            }
            else if (parts.Length >= 2 && parts[0] == "SET_SUPPRESS_UPGRADE_POINTS")
            {
                UpgradePool.SuppressUpgradePoints = parts[1].Trim() == "1";
                Broadcast($"UPGRADE_POINTS_SUPPRESSED,{UpgradePool.SuppressUpgradePoints}");
            }
            else if (parts.Length >= 2 && parts[0] == "SET_COUNTER_LOCKED")
            {
                bool locked = parts[1].Trim() == "1";
                Broadcast(CounterLock.SetLocked(locked));
            }
            else if (parts.Length >= 2 && parts[0] == "SET_UPGRADE_POINTS"
                     && int.TryParse(parts[1].Trim(), out int points))
            {
                // Diagnostic only. Suppression has to come off first or the
                // sweep zeroes this within 500ms - and that same sweep means
                // the next state push from the client wipes it again, so this
                // can't leave the save in a cheated state.
                UpgradePool.SuppressUpgradePoints = false;
                Broadcast(UpgradePool.SetUpgradePoints(points));
            }
            else if (parts[0] == "DUMP_UPGRADE_STATE")
            {
                Broadcast(UpgradePool.DumpUpgradeState());
            }
            else if (parts[0] == "SET_UPGRADES")
            {
                var owned = new List<string>();
                for (int i = 1; i < parts.Length; i++)
                {
                    string s = parts[i].Trim();
                    if (s.Length > 0) owned.Add(s);
                }
                pendingUpgrades = owned;
                Broadcast($"UPGRADES_QUEUED,{owned.Count}");
            }
            else if (parts[0] == "SET_GADGETS")
            {
                // Everything after the command word is the full desired
                // gadget list. May legitimately be empty (= strip all).
                var desired = new List<string>();
                for (int i = 1; i < parts.Length; i++)
                {
                    string name = parts[i].Trim();
                    if (name.Length > 0) desired.Add(name);
                }
                pendingDesiredState = desired;
                pendingReadySince = DateTime.MinValue;
                Broadcast($"STATE_QUEUED,{desired.Count}");
            }
            else
            {
                Broadcast($"ERROR,unrecognized command: {line}");
            }
        }

        // Apply any queued desired state once the player is ready AND the
        // inventory has had a moment to settle.
        if (pendingDesiredState != null)
        {
            if (!GadgetPool.IsReady())
            {
                // Not ready - restart the settle timer so the delay is
                // measured from when things actually became ready.
                pendingReadySince = DateTime.MinValue;
            }
            else
            {
                if (pendingReadySince == DateTime.MinValue)
                {
                    pendingReadySince = DateTime.UtcNow;
                    Debug.Log("Player ready - letting inventory settle before applying gadget state.");
                }
                else if (DateTime.UtcNow - pendingReadySince >= SettleDelay)
                {
                    var desired = pendingDesiredState;
                    pendingDesiredState = null;
                    pendingReadySince = DateTime.MinValue;
                    string result = GadgetPool.ApplyDesiredState(desired);
                    Debug.Log(result);
                    Broadcast(result);
                }
            }
        }

        // Same pattern for upgrades.
        if (pendingUpgrades != null)
        {
            if (GadgetPool.IsReady())
            {
                var owned = pendingUpgrades;
                pendingUpgrades = null;
                string result = UpgradePool.ApplyDesiredUpgrades(owned);
                Debug.Log(result);
                Broadcast(result);
            }
        }
    }
}

[Script]
public class ApBridgeStarter : Script
{
    public override void Main()
    {
        ApBridge.Start();
    }

    // Fires every time a world finishes loading (new game, save load,
    // level transition). Tells the client to re-send the authoritative
    // gadget state, since a reload restores every gadget the player
    // "owns" in the save regardless of AP progress.
    public override void OnEnterGame()
    {
        Debug.Log("World loaded - requesting gadget state from AP client.");
        ApBridge.Broadcast("GAME_LOADED");
    }

    public override void OnTick()
    {
        ApBridge.ProcessQueuedCommands();
        CounterLock.Tick();
        UpgradePool.Tick();
    }
}
