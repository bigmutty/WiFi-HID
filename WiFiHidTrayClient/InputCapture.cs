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

    private IntPtr _keyboardHookId = IntPtr.Zero;
    private IntPtr _mouseHookId = IntPtr.Zero;
    private MessageWindow? _messageWindow;
    private volatile bool _capturing;
    private bool _toggleKeyHeld;
    private bool _toggleTriggerActive;
    private bool _sendCtrlAltDelKeyHeld;
    private bool _sendCtrlAltDelTriggerActive;

    public event Action<bool>? CapturingChanged;
    public bool IsCapturing => _capturing;

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
        }

        CapturingChanged?.Invoke(_capturing);
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
                    _client.SendMouseButton("LEFT", "buttonDown");
                    break;
                case NativeMethods.WM_LBUTTONUP:
                    _client.SendMouseButton("LEFT", "buttonUp");
                    break;
                case NativeMethods.WM_RBUTTONDOWN:
                    _client.SendMouseButton("RIGHT", "buttonDown");
                    break;
                case NativeMethods.WM_RBUTTONUP:
                    _client.SendMouseButton("RIGHT", "buttonUp");
                    break;
                case NativeMethods.WM_MBUTTONDOWN:
                    _client.SendMouseButton("MIDDLE", "buttonDown");
                    break;
                case NativeMethods.WM_MBUTTONUP:
                    _client.SendMouseButton("MIDDLE", "buttonUp");
                    break;
                case NativeMethods.WM_MOUSEWHEEL:
                {
                    var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                    short delta = (short)((data.mouseData >> 16) & 0xFFFF);
                    int wheel = Math.Sign(delta) * Math.Max(1, Math.Abs(delta) / 120);
                    _client.SendMouseWheel(wheel);
                    break;
                }
                // WM_MOUSEMOVE is swallowed too (movement is forwarded from raw input instead),
                // as are horizontal wheel/X-button events, which have no Pico equivalent.
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
                SendClampedMove(raw.mouse.lLastX, raw.mouse.lLastY);
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
