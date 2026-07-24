using System;
using System.IO;

namespace WiFiHidTrayClient;

/// <summary>
/// Detects whether a Pico running this project's code.py is currently connected via USB by
/// looking for a removable drive with the CIRCUITPY volume label (the name CircuitPython boards
/// mount as).
/// </summary>
internal static class PicoUsbDevice
{
    public static bool TryFindCircuitPyDrive(out string rootPath)
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || !string.Equals(drive.VolumeLabel, "CIRCUITPY", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var root = drive.RootDirectory.FullName;

                // Sanity check: a genuine CircuitPython board exposes boot_out.txt and/or code.py
                // at the drive root, distinguishing it from an unrelated drive someone happened
                // to label "CIRCUITPY".
                if (File.Exists(Path.Combine(root, "boot_out.txt")) || File.Exists(Path.Combine(root, "code.py")))
                {
                    rootPath = root;
                    return true;
                }
            }
            catch (IOException)
            {
                // Drive became unavailable mid-enumeration (e.g. unplugged); skip it.
            }
            catch (UnauthorizedAccessException)
            {
                // No permission to query this drive; skip it.
            }
        }

        rootPath = string.Empty;
        return false;
    }
}
