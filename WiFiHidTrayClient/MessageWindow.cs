using System;
using System.Windows.Forms;

namespace WiFiHidTrayClient;

/// <summary>
/// A hidden, message-only window used solely as a target for WM_INPUT (raw mouse) messages.
/// </summary>
internal sealed class MessageWindow : NativeWindow, IDisposable
{
    private const int HWND_MESSAGE = -3;
    private readonly Action<IntPtr> _onRawInput;

    public MessageWindow(Action<IntPtr> onRawInput)
    {
        _onRawInput = onRawInput;
        CreateHandle(new CreateParams
        {
            Caption = "WiFiHidTrayClient-RawInput",
            Parent = new IntPtr(HWND_MESSAGE),
        });
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_INPUT)
        {
            _onRawInput(m.LParam);
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero)
        {
            DestroyHandle();
        }
    }
}
