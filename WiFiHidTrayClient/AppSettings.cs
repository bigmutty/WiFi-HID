using System;
using System.IO;
using System.Text.Json;

namespace WiFiHidTrayClient;

/// <summary>
/// Persisted configuration for the tray client. Stored as settings.json next to the executable.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Hostname or IP address of the Pico WiFi-HID server.</summary>
    public string Host { get; set; } = "WiFi-HID.local";

    /// <summary>TCP port the Pico's socket server is listening on (see code.py).</summary>
    public int Port { get; set; } = 5005;

    /// <summary>Modifier requirements for the capture toggle hotkey.</summary>
    public bool ToggleRequiresControl { get; set; } = true;
    public bool ToggleRequiresAlt { get; set; } = true;
    public bool ToggleRequiresShift { get; set; } = true;

    /// <summary>Non-modifier trigger key for the capture toggle hotkey (parsed as System.Windows.Forms.Keys).</summary>
    public string ToggleKey { get; set; } = "F12";

    /// <summary>
    /// Modifier requirements and trigger key for the "send Ctrl+Alt+Del to the Pico" hotkey.
    /// The real Ctrl+Alt+Del is a Secure Attention Sequence handled by Winlogon and can never be
    /// captured/forwarded, so this separate combo is used to send it to the remote machine while
    /// capturing is active. Only recognized while capturing is on.
    /// </summary>
    public bool SendCtrlAltDelRequiresControl { get; set; } = true;
    public bool SendCtrlAltDelRequiresAlt { get; set; } = true;
    public bool SendCtrlAltDelRequiresShift { get; set; } = true;
    public string SendCtrlAltDelKey { get; set; } = "Delete";

    /// <summary>
    /// Modifier requirements and trigger key for the "toggle Joystick Mode" hotkey. While
    /// active, mouse movement is translated into simulated joystick axis movement (see
    /// code.py's Joystick class) instead of cursor-move commands, and the left/right mouse
    /// buttons become joystick buttons 1/2. Only recognized while capturing is on.
    /// </summary>
    public bool JoystickRequiresControl { get; set; } = true;
    public bool JoystickRequiresAlt { get; set; } = true;
    public bool JoystickRequiresShift { get; set; } = true;
    public string JoystickToggleKey { get; set; } = "J";

    private static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                {
                    return loaded;
                }
            }
        }
        catch
        {
            // Fall back to defaults if the file is missing, unreadable, or invalid.
        }

        var defaults = new AppSettings();
        defaults.Save();
        return defaults;
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Ignore write failures (e.g. read-only install directory).
        }
    }
}
