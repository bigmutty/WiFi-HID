using System;
using System.Diagnostics;

namespace WiFiHidTrayClient;

/// <summary>
/// Reads the SSID (and saved password, if available) of the WiFi network this PC is currently
/// connected to, via the `netsh wlan` CLI. This avoids the more involved native WLAN API and
/// works without elevation for the current user's own saved profiles.
/// </summary>
internal static class WindowsWifi
{
    public static bool TryGetCurrentNetwork(out string ssid, out string password, out string? error)
    {
        ssid = string.Empty;
        password = string.Empty;
        error = null;

        if (!TryRunNetsh("wlan show interfaces", out var interfacesOutput, out error))
        {
            return false;
        }

        var foundSsid = ParseValue(interfacesOutput, "SSID");
        if (string.IsNullOrEmpty(foundSsid))
        {
            error = "No connected WiFi network was found. Make sure this PC is connected via WiFi (not Ethernet).";
            return false;
        }

        ssid = foundSsid;

        // Best-effort: if we can't read the saved password (e.g. no profile, or restricted by
        // policy), still report success with just the SSID so the caller can fill it in manually.
        if (TryRunNetsh($"wlan show profile name=\"{ssid}\" key=clear", out var profileOutput, out _))
        {
            password = ParseValue(profileOutput, "Key Content") ?? string.Empty;
        }

        return true;
    }

    private static bool TryRunNetsh(string arguments, out string output, out string? error)
    {
        output = string.Empty;
        error = null;

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };

            process.Start();
            output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to run netsh: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Finds a "Key   : Value" line (netsh's colon-aligned output) by exact key name and
    /// returns the trimmed value (surrounding quotes stripped), or null if not found.
    /// </summary>
    private static string? ParseValue(string output, string key)
    {
        foreach (var rawLine in output.Split('\n'))
        {
            var trimmed = rawLine.TrimEnd('\r').TrimStart();
            if (!trimmed.StartsWith(key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Make sure this is the exact field name (followed by optional whitespace then ':'),
            // not a different field that merely shares a prefix.
            var afterKey = trimmed.Substring(key.Length);
            var colonIndex = afterKey.IndexOf(':');
            if (colonIndex < 0 || afterKey.Substring(0, colonIndex).Trim().Length > 0)
            {
                continue;
            }

            return afterKey.Substring(colonIndex + 1).Trim().Trim('"');
        }

        return null;
    }
}
