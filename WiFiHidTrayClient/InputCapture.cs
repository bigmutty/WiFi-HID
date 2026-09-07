using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WiFiHidTrayClient;

/// <summary>
/// Installs global low-level keyboard/mouse hooks that swallow every input event so it never
/// reaches the local system, forwards the equivalent Pico command instead, and provides a
/// hotkey combo to toggle capturing on/off so the user can get their local machine back.
///
/// Mouse movement is read via Raw Input (WM_INPUT) rather than the hook's absolute cursor
/// position, because raw input deltas are unaffected by cursor clipping/screen-edge clamping.
/// </summary>
public sealed class InputCapture : IDisposable
{
    private readonly PicoClient _client;
    private readonly AppSettings _settings;
    private readonly NativeMethods.LowLevelProc _keyboardProc;
    private readonly NativeMethods.LowLevelProc _mouseProc;
    private readonly HashSet<int> _heldKeys = new();
    private Keys _toggleKey;
    private Keys _sendCtrlAltDelKey;
    private Keys _joystickToggleKey;

    private IntPtr _keyboardHookId = IntPtr.Zero;
    private IntPtr _mouseHookId = IntPtr.Zero;
    private MessageWindow? _messageWindow;
    private volatile bool _capturing;
    private bool _toggleKeyHeld;
    private bool _toggleTriggerActive;
    private bool _sendCtrlAltDelKeyHeld;
    private bool _sendCtrlAltDelTriggerActive;
    private bool _joystickToggleKeyHeld;
    private bool _joystickToggleTriggerActive;

    // Joystick Mode: mouse movement is accumulated into a spring-centered stick position
    // (decayed toward zero on a timer) instead of being forwarded as cursor-move commands.
    private const double JoystickSensitivity = 2.0;
    private const double JoystickDecayPerTick = 0.80;
    private const int JoystickTickIntervalMs = 20;
    private readonly object _joystickLock = new();
    private volatile bool _joystickMode;
    private double _joystickX;
    private double _joystickY;
    private bool _joystickButton1;
    private bool _joystickButton2;
    private System.Threading.Timer? _joystickTimer;

    public event Action<bool>? CapturingChanged;
    public event Action<bool>? JoystickModeChanged;
    public bool IsCapturing => _capturing;
    public bool IsJoystickMode => _joystickMode;

    public InputCapture(PicoClient client, AppSettings settings)
    {
        _client = client;
        _settings = settings;
        _keyboardProc = KeyboardHookCallback;
        _mouseProc = MouseHookCallback;

        RefreshHotkeys();
    }

    /// <summary>Re-parses the toggle and Ctrl+Alt+Del hotkeys from the (possibly just-edited)
    /// settings object; falls back to F12/Delete if a name doesn't parse.</summary>
    public void RefreshHotkeys()
    {
        if (Enum.TryParse(_settings.ToggleKey, ignoreCase: true, out Keys toggleKey) && toggleKey != Keys.None)
        {
            _toggleKey = toggleKey;
        }
        else if (_toggleKey == default)
        {
            _toggleKey = Keys.F12;
        }

        if (Enum.TryParse(_settings.SendCtrlAltDelKey, ignoreCase: true, out Keys cadKey) && cadKey != Keys.None)
        {
            _sendCtrlAltDelKey = cadKey;
        }
        else if (_sendCtrlAltDelKey == default)
        {
            _sendCtrlAltDelKey = Keys.Delete;
        }

        if (Enum.TryParse(_settings.JoystickToggleKey, ignoreCase: true, out Keys joyKey) && joyKey != Keys.None)
        {
            _joystickToggleKey = joyKey;
        }
        else if (_joystickToggleKey == default)
        {
            _joystickToggleKey = Keys.J;
        }
    }

    /// <summary>Immediately sends a virtual Ctrl+Alt+Del to the Pico, regardless of hotkey state.</summary>
    public void SendCtrlAltDelToRemote() => _client.SendCtrlAltDelete();

    public void Start()
    {
        _messageWindow = new MessageWindow(OnRawInput);
        RegisterRawMouse(_messageWindow.Handle);

        var hMod = NativeMethods.GetModuleHandle(null);
        _keyboardHookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardProc, hMod, 0);
        _mouseHookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, hMod, 0);

        if (_keyboardHookId == IntPtr.Zero || _mouseHookId == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to install global input hooks.");
        }
    }

    public void ToggleCapturing() => SetCapturing(!_capturing);

    public void ToggleJoystickMode() => SetJoystickMode(!_joystickMode);

    /// <summary>Immediately releases capturing (e.g. because the Pico connection was lost), so
    /// the local keyboard/mouse isn't left stuck swallowed while there's no one to forward to.
    /// A no-op if not currently capturing.</summary>
    public void StopCapturing() => SetCapturing(false);

    private static void RegisterRawMouse(IntPtr hwnd)
    {
        var device = new NativeMethods.RAWINPUTDEVICE
        {
            usUsagePage = NativeMethods.HID_USAGE_PAGE_GENERIC,
            usUsage = NativeMethods.HID_USAGE_GENERIC_MOUSE,
            dwFlags = NativeMethods.RIDEV_INPUTSINK,
            hwndTarget = hwnd
        };
        NativeMethods.RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>());
    }

    private void SetCapturing(bool value)
    {
        if (_capturing == value)
        {
            return;
        }

        _capturing = value;
        _heldKeys.Clear();

        if (value)
        {
            ClipCursorToPoint();
            ClearLocalModifiersAndButtons();
        }
        else
        {
            NativeMethods.ClipCursorNull(IntPtr.Zero);
            _client.SendReleaseAllKeysAndButtons();
            if (_joystickMode)
            {
                SetJoystickMode(false);
            }
        }

        CapturingChanged?.Invoke(_capturing);
    }

    private void SetJoystickMode(bool value)
    {
        if (_joystickMode == value)
        {
            return;
        }

        _joystickMode = value;

        if (value)
        {
            lock (_joystickLock)
            {
                _joystickX = 0;
                _joystickY = 0;
                _joystickButton1 = false;
                _joystickButton2 = false;
            }

            _joystickTimer = new System.Threading.Timer(_ => JoystickTick(), null, 0, JoystickTickIntervalMs);
        }
        else
        {
            _joystickTimer?.Dispose();
            _joystickTimer = null;
            _client.SendJoystick(0, 0, false, false); // release the stick to its resting position
        }

        JoystickModeChanged?.Invoke(_joystickMode);
    }

    /// <summary>Runs on a timer while Joystick Mode is on: decays the stick position toward
    /// center and sends the current full state to the Pico (see PicoClient.SendJoystick).</summary>
    private void JoystickTick()
    {
        int x, y;
        bool button1, button2;
        lock (_joystickLock)
        {
            _joystickX *= JoystickDecayPerTick;
            _joystickY *= JoystickDecayPerTick;
            if (Math.Abs(_joystickX) < 0.5) _joystickX = 0;
            if (Math.Abs(_joystickY) < 0.5) _joystickY = 0;
            x = (int)Math.Round(_joystickX);
            y = (int)Math.Round(_joystickY);
            button1 = _joystickButton1;
            button2 = _joystickButton2;
        }

        _client.SendJoystick(x, y, button1, button2);
    }

    private void AddJoystickImpulse(int dx, int dy)
    {
        lock (_joystickLock)
        {
            _joystickX = Math.Clamp(_joystickX + dx * JoystickSensitivity, -127, 127);
            _joystickY = Math.Clamp(_joystickY + dy * JoystickSensitivity, -127, 127);
        }
    }

    private void SetJoystickButton1(bool pressed)
    {
        lock (_joystickLock) { _joystickButton1 = pressed; }
    }

    private void SetJoystickButton2(bool pressed)
    {
        lock (_joystickLock) { _joystickButton2 = pressed; }
    }

    private static void ClipCursorToPoint()
    {
        if (!NativeMethods.GetCursorPos(out var pt))
        {
            return;
        }

        var rect = new NativeMethods.RECT { Left = pt.x, Top = pt.y, Right = pt.x + 1, Bottom = pt.y + 1 };
        NativeMethods.ClipCursor(ref rect);
    }

    /// <summary>
    /// Synthesizes key-up/button-up events for the local OS so a modifier key or mouse button
    /// that was physically held down at the moment of toggling doesn't appear stuck locally
    /// (its real "up" event will be captured and forwarded to the Pico instead of the OS).
    /// </summary>
    private static void ClearLocalModifiersAndButtons()
    {
        Span<byte> modifierVks = stackalloc byte[]
        {
            NativeMethods.VK_LSHIFT, NativeMethods.VK_RSHIFT,
            NativeMethods.VK_LCONTROL, NativeMethods.VK_RCONTROL,
            NativeMethods.VK_LMENU, NativeMethods.VK_RMENU,
            NativeMethods.VK_LWIN, NativeMethods.VK_RWIN,
        };
        foreach (var vk in modifierVks)
        {
            NativeMethods.keybd_event(vk, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
        NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_MIDDLEUP, 0, 0, 0, UIntPtr.Zero);
    }

    private bool ToggleModifiersSatisfied()
    {
        bool ctrl = !_settings.ToggleRequiresControl || (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0;
        bool alt = !_settings.ToggleRequiresAlt || (NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU) & 0x8000) != 0;
        bool shift = !_settings.ToggleRequiresShift || (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0;
        return ctrl && alt && shift;
    }

    private bool CtrlAltDelModifiersSatisfied()
    {
        bool ctrl = !_settings.SendCtrlAltDelRequiresControl || (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0;
        bool alt = !_settings.SendCtrlAltDelRequiresAlt || (NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU) & 0x8000) != 0;
        bool shift = !_settings.SendCtrlAltDelRequiresShift || (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0;
        return ctrl && alt && shift;
    }

    private bool JoystickModifiersSatisfied()
    {
        bool ctrl = !_settings.JoystickRequiresControl || (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0;
        bool alt = !_settings.JoystickRequiresAlt || (NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU) & 0x8000) != 0;
        bool shift = !_settings.JoystickRequiresShift || (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0;
        return ctrl && alt && shift;
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            int msg = wParam.ToInt32();
            bool isDown = msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN;
            bool isUp = msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP;

            if (isDown || isUp)
            {
                var key = (Keys)data.vkCode;

                // The trigger key of the toggle combo is always intercepted so it can flip
                // capturing on/off; if the required modifiers aren't held it's treated as a
                // normal key instead (falls through below).
                if (key == _toggleKey)
                {
                    if (isDown)
                    {
                        bool wasAlreadyDown = _toggleKeyHeld;
                        _toggleKeyHeld = true;
                        if (!wasAlreadyDown && ToggleModifiersSatisfied())
                        {
                            _toggleTriggerActive = true;
                            ToggleCapturing();
                        }
                    }
                    else
                    {
                        _toggleKeyHeld = false;
                    }

                    if (_toggleTriggerActive)
                    {
                        if (isUp)
                        {
                            _toggleTriggerActive = false;
                        }

                        return (IntPtr)1;
                    }
                }

                // Ctrl+Alt+Shift+Delete sends a virtual Ctrl+Alt+Del to the Pico, since the real
                // Ctrl+Alt+Del is a Secure Attention Sequence that Windows never delivers to hooks.
                // Only armed while capturing, so the combo behaves as a normal keystroke otherwise.
                if (key == _sendCtrlAltDelKey)
                {
                    if (isDown)
                    {
                        bool wasAlreadyDown = _sendCtrlAltDelKeyHeld;
                        _sendCtrlAltDelKeyHeld = true;
                        if (!wasAlreadyDown && _capturing && CtrlAltDelModifiersSatisfied())
                        {
                            _sendCtrlAltDelTriggerActive = true;
                            _client.SendCtrlAltDelete();
                        }
                    }
                    else
                    {
                        _sendCtrlAltDelKeyHeld = false;
                    }

                    if (_sendCtrlAltDelTriggerActive)
                    {
                        if (isUp)
                        {
                            _sendCtrlAltDelTriggerActive = false;
                        }

                        return (IntPtr)1;
                    }
                }

                // Ctrl+Alt+Shift+J toggles Joystick Mode (mouse movement/buttons become
                // simulated joystick input instead of cursor movement/clicks). Only armed while
                // capturing, same rationale as the Ctrl+Alt+Del hotkey above.
                if (key == _joystickToggleKey)
                {
                    if (isDown)
                    {
                        bool wasAlreadyDown = _joystickToggleKeyHeld;
                        _joystickToggleKeyHeld = true;
                        if (!wasAlreadyDown && _capturing && JoystickModifiersSatisfied())
                        {
                            _joystickToggleTriggerActive = true;
                            ToggleJoystickMode();
                        }
                    }
                    else
                    {
                        _joystickToggleKeyHeld = false;
                    }

                    if (_joystickToggleTriggerActive)
                    {
                        if (isUp)
                        {
                            _joystickToggleTriggerActive = false;
                        }

                        return (IntPtr)1;
                    }
                }

                if (_capturing)
                {
                    bool extended = (data.flags & NativeMethods.LLKHF_EXTENDED) != 0;
                    if (PicoKeyCodes.TryGetKeyName((int)data.vkCode, data.scanCode, extended, out var keyName))
                    {
                        if (isDown)
                        {
                            if (_heldKeys.Add((int)data.vkCode))
                            {
                                _client.SendKeyAction(keyName, "keyDown");
                            }
                        }
                        else
                        {
                            _heldKeys.Remove((int)data.vkCode);
                            _client.SendKeyAction(keyName, "keyUp");
                        }
                    }

                    return (IntPtr)1;
                }
            }
        }

        return NativeMethods.CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _capturing)
        {
            int msg = wParam.ToInt32();
            switch (msg)
            {
                case NativeMethods.WM_LBUTTONDOWN:
                    if (_joystickMode) SetJoystickButton1(true); else _client.SendMouseButton("LEFT", "buttonDown");
                    break;
                case NativeMethods.WM_LBUTTONUP:
                    if (_joystickMode) SetJoystickButton1(false); else _client.SendMouseButton("LEFT", "buttonUp");
                    break;
                case NativeMethods.WM_RBUTTONDOWN:
                    if (_joystickMode) SetJoystickButton2(true); else _client.SendMouseButton("RIGHT", "buttonDown");
                    break;
                case NativeMethods.WM_RBUTTONUP:
                    if (_joystickMode) SetJoystickButton2(false); else _client.SendMouseButton("RIGHT", "buttonUp");
                    break;
                case NativeMethods.WM_MBUTTONDOWN:
                    if (!_joystickMode) _client.SendMouseButton("MIDDLE", "buttonDown");
                    break;
                case NativeMethods.WM_MBUTTONUP:
                    if (!_joystickMode) _client.SendMouseButton("MIDDLE", "buttonUp");
                    break;
                case NativeMethods.WM_MOUSEWHEEL:
                {
                    if (!_joystickMode)
                    {
                        var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                        short delta = (short)((data.mouseData >> 16) & 0xFFFF);
                        int wheel = Math.Sign(delta) * Math.Max(1, Math.Abs(delta) / 120);
                        _client.SendMouseWheel(wheel);
                    }
                    break;
                }
                // WM_MOUSEMOVE is swallowed too (movement is forwarded from raw input instead),
                // as are horizontal wheel/X-button events, which have no Pico equivalent. The
                // wheel and middle button also have no joystick equivalent, so they're dropped
                // entirely (rather than forwarded as mouse actions) while Joystick Mode is on.
            }

            return (IntPtr)1;
        }

        return NativeMethods.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    private void OnRawInput(IntPtr lParam)
    {
        if (!_capturing)
        {
            return;
        }

        uint headerSize = (uint)Marshal.SizeOf<NativeMethods.RAWINPUTHEADER>();
        uint dwSize = 0;
        NativeMethods.GetRawInputData(lParam, NativeMethods.RID_INPUT, IntPtr.Zero, ref dwSize, headerSize);
        if (dwSize == 0)
        {
            return;
        }

        IntPtr buffer = Marshal.AllocHGlobal((int)dwSize);
        try
        {
            if (NativeMethods.GetRawInputData(lParam, NativeMethods.RID_INPUT, buffer, ref dwSize, headerSize) != dwSize)
            {
                return;
            }

            var raw = Marshal.PtrToStructure<NativeMethods.RAWINPUT>(buffer);
            if (raw.header.dwType == NativeMethods.RIM_TYPEMOUSE && (raw.mouse.lLastX != 0 || raw.mouse.lLastY != 0))
            {
                if (_joystickMode)
                {
                    AddJoystickImpulse(raw.mouse.lLastX, raw.mouse.lLastY);
                }
                else
                {
                    SendClampedMove(raw.mouse.lLastX, raw.mouse.lLastY);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// The Pico's HID mouse report only carries signed-byte deltas per axis (-127..127, see
    /// code.py's mouse.move call), so larger raw input deltas are split into multiple reports
    /// instead of being clamped/truncated, to avoid losing fast mouse motion.
    /// </summary>
    private void SendClampedMove(int dx, int dy)
    {
        while (dx != 0 || dy != 0)
        {
            int stepX = Math.Clamp(dx, -127, 127);
            int stepY = Math.Clamp(dy, -127, 127);
            _client.SendMouseMove(stepX, stepY);
            dx -= stepX;
            dy -= stepY;
        }
    }

    public void Stop()
    {
        SetCapturing(false);

        if (_keyboardHookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHookId);
            _keyboardHookId = IntPtr.Zero;
        }

        if (_mouseHookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHookId);
            _mouseHookId = IntPtr.Zero;
        }

        _messageWindow?.Dispose();
        _messageWindow = null;
    }

    public void Dispose() => Stop();
}
