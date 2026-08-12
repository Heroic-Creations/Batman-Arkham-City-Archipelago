using System;
using System.IO;
using BmSDK.Framework;

// Where the scripts write their logs and data dumps.
//
// These used to be absolute paths on the original author's machine. That was
// fine for development and fatal for anyone else: LogPool() is called from
// inside StripAll(), so on a machine where the directory didn't exist the
// write threw, StripAll never returned, and ApplyDesiredState never got as
// far as restoring the gadgets the player owns - leaving them with nothing.
//
// Everything now lands in an ArchipelagoLogs folder next to the game, and
// every write is guarded. A failed log must never break gameplay.
public static class ApPaths
{
    private static string logDir;
    private static bool initialised;

    public static string LogDir
    {
        get
        {
            if (!initialised)
            {
                initialised = true;
                try
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    if (string.IsNullOrEmpty(baseDir)) baseDir = Directory.GetCurrentDirectory();
                    string dir = Path.Combine(baseDir, "ArchipelagoLogs");
                    Directory.CreateDirectory(dir);
                    logDir = dir;
                    Debug.Log($"Archipelago logs: {dir}");
                }
                catch (Exception e)
                {
                    logDir = null;
                    Debug.Log($"Could not create the Archipelago log folder ({e.Message}) - "
                              + "logging is disabled, gameplay is unaffected.");
                }
            }
            return logDir;
        }
    }

    /// Full path for a log file, or null if logging isn't available.
    public static string For(string fileName)
    {
        string dir = LogDir;
        return dir == null ? null : Path.Combine(dir, fileName);
    }

    /// Write text to a log file. Never throws - callers are gameplay paths.
    public static void Write(string fileName, string contents, bool append = false)
    {
        try
        {
            string path = For(fileName);
            if (path == null) return;
            if (append) File.AppendAllText(path, contents);
            else File.WriteAllText(path, contents);
        }
        catch (Exception e)
        {
            Debug.Log($"Could not write {fileName}: {e.Message}");
        }
    }
}
