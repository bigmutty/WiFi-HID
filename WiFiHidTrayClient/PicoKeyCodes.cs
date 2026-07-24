using System.Collections.Generic;
using System.Windows.Forms;

namespace WiFiHidTrayClient;

/// <summary>
/// Maps Windows virtual-key codes to the key name strings expected by the Pico's
/// KEY_MAP (built from adafruit_hid.keycode.Keycode attribute names, e.g. "ENTER", "LEFT_CONTROL").
/// </summary>
internal static class PicoKeyCodes
{
    private static readonly Dictionary<Keys, string> Map = new()
    {
        [Keys.D0] = "ZERO",
        [Keys.D1] = "ONE",
        [Keys.D2] = "TWO",
        [Keys.D3] = "THREE",
        [Keys.D4] = "FOUR",
        [Keys.D5] = "FIVE",
        [Keys.D6] = "SIX",
        [Keys.D7] = "SEVEN",
        [Keys.D8] = "EIGHT",
        [Keys.D9] = "NINE",

        [Keys.NumPad0] = "KEYPAD_ZERO",
        [Keys.NumPad1] = "KEYPAD_ONE",
        [Keys.NumPad2] = "KEYPAD_TWO",
        [Keys.NumPad3] = "KEYPAD_THREE",
        [Keys.NumPad4] = "KEYPAD_FOUR",
        [Keys.NumPad5] = "KEYPAD_FIVE",
        [Keys.NumPad6] = "KEYPAD_SIX",
        [Keys.NumPad7] = "KEYPAD_SEVEN",
        [Keys.NumPad8] = "KEYPAD_EIGHT",
        [Keys.NumPad9] = "KEYPAD_NINE",
        [Keys.Multiply] = "KEYPAD_ASTERISK",
        [Keys.Add] = "KEYPAD_PLUS",
        [Keys.Subtract] = "KEYPAD_MINUS",
        [Keys.Decimal] = "KEYPAD_PERIOD",
        [Keys.Divide] = "KEYPAD_FORWARD_SLASH",
        [Keys.NumLock] = "KEYPAD_NUMLOCK",

        [Keys.Return] = "ENTER",
        [Keys.Escape] = "ESCAPE",
        [Keys.Back] = "BACKSPACE",
        [Keys.Tab] = "TAB",
        [Keys.Space] = "SPACE",
        [Keys.Delete] = "DELETE",
        [Keys.Insert] = "INSERT",
        [Keys.Capital] = "CAPS_LOCK",
        [Keys.PrintScreen] = "PRINT_SCREEN",
        [Keys.Scroll] = "SCROLL_LOCK",
        [Keys.Pause] = "PAUSE",
        [Keys.Home] = "HOME",
        [Keys.End] = "END",
        [Keys.PageUp] = "PAGE_UP",
        [Keys.PageDown] = "PAGE_DOWN",
        [Keys.Up] = "UP_ARROW",
        [Keys.Down] = "DOWN_ARROW",
        [Keys.Left] = "LEFT_ARROW",
        [Keys.Right] = "RIGHT_ARROW",

        [Keys.OemPeriod] = "PERIOD",
        [Keys.Oemcomma] = "COMMA",
        [Keys.OemQuestion] = "FORWARD_SLASH",
        [Keys.OemPipe] = "BACKSLASH",
        [Keys.OemSemicolon] = "SEMICOLON",
        [Keys.OemQuotes] = "QUOTE",
        [Keys.OemOpenBrackets] = "LEFT_BRACKET",
        [Keys.OemCloseBrackets] = "RIGHT_BRACKET",
        [Keys.OemMinus] = "MINUS",
        [Keys.Oemplus] = "EQUALS",
        [Keys.Oemtilde] = "GRAVE_ACCENT",
        [Keys.Apps] = "APPLICATION",

        [Keys.LWin] = "LEFT_GUI",
        [Keys.RWin] = "RIGHT_GUI",
        [Keys.LControlKey] = "LEFT_CONTROL",
        [Keys.RControlKey] = "RIGHT_CONTROL",
        [Keys.LShiftKey] = "LEFT_SHIFT",
        [Keys.RShiftKey] = "RIGHT_SHIFT",
        [Keys.LMenu] = "LEFT_ALT",
        [Keys.RMenu] = "RIGHT_ALT",
    };

    /// <summary>
    /// Resolves a raw virtual-key code (as reported by WH_KEYBOARD_LL) to a Pico key name.
    /// Generic Shift/Control/Alt codes are disambiguated into their left/right variants using
    /// the scan code (Shift) or the extended-key flag (Control/Alt), matching how the physical
    /// key was actually pressed.
    /// </summary>
    public static bool TryGetKeyName(int vkCode, uint scanCode, bool extended, out string keyName)
    {
        var key = (Keys)vkCode;

        key = key switch
        {
            Keys.ShiftKey => scanCode == 0x36 ? Keys.RShiftKey : Keys.LShiftKey,
            Keys.ControlKey => extended ? Keys.RControlKey : Keys.LControlKey,
            Keys.Menu => extended ? Keys.RMenu : Keys.LMenu,
            _ => key
        };

        if (Map.TryGetValue(key, out var mapped))
        {
            keyName = mapped;
            return true;
        }

        if (key >= Keys.F1 && key <= Keys.F24)
        {
            keyName = key.ToString().ToUpperInvariant();
            return true;
        }

        if (key >= Keys.A && key <= Keys.Z)
        {
            keyName = key.ToString().ToUpperInvariant();
            return true;
        }

        keyName = string.Empty;
        return false;
    }
}
