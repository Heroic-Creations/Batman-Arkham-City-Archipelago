using System;
using System.Collections.Generic;
using System.IO;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Framework;

[Script]
public class DumpRiddlerTrophies : Script
{
    private const string OutputFile = "trophy_map.csv";
    private static string OutputPath => ApPaths.For(OutputFile);
    private static readonly HashSet<string> Seen = new HashSet<string>();
    private static bool Loaded = false;

    public override void OnKeyDown(Keys key)
    {
        if (key != Keys.L) return;

        if (!Loaded)
        {
            if (File.Exists(OutputPath))
            {
                foreach (var line in File.ReadAllLines(OutputPath))
                {
                    var parts = line.Split(',');
                    if (parts.Length >= 2 && parts[0] != "Zone")
                    {
                        Seen.Add($"{parts[0]}_{parts[1]}");
                    }
                }
            }
            Loaded = true;
            Debug.Log($"Loaded {Seen.Count} existing entries from {OutputPath} to avoid re-logging duplicates.");
        }

        var trophies = Game.FindObjects<RPickup_Riddler>();
        int newCount = 0;
        int skipped = 0;

        if (OutputPath == null)
        {
            Debug.Log("No writable log folder - trophy dump skipped.");
            return;
        }

        using (var writer = new StreamWriter(OutputPath, append: true))
        {
            foreach (var t in trophies)
            {
                string dedupeKey = $"{t.Zone}_{t.PickupIndex}";
                if (Seen.Contains(dedupeKey))
                {
                    skipped++;
                    continue;
                }
                Seen.Add(dedupeKey);

                writer.WriteLine($"{t.Zone},{t.PickupIndex},{t.PickupName},{t.Location.X},{t.Location.Y},{t.Location.Z}");
                newCount++;
            }
        }

        Debug.Log($"Dumped {newCount} new trophies ({skipped} already seen) to {OutputPath}. Total so far: {Seen.Count}.");
    }
}

[Script]
public class ForceLoadAllLevels : Script
{
    public override void OnKeyDown(Keys key)
    {
        if (key != Keys.K) return;

        var worldInfo = Game.GetWorldInfo();
        int count = 0;
        foreach (var level in worldInfo.StreamingLevels)
        {
            level.bShouldBeLoaded = true;
            level.bShouldBeVisible = true;
            count++;
        }
        Debug.Log($"Forced {count} streaming levels to load. Give it a few seconds, then press L (maybe a few times) to dump trophies as they stream in.");
    }
}
