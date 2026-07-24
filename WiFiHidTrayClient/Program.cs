using System;
using System.Threading;
using System.Windows.Forms;

namespace WiFiHidTrayClient;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Ensure only one instance runs at a time, since two instances would both
        // install global hooks and fight over the Pico connection.
        using var singleInstanceMutex = new Mutex(true, "Global\\WiFiHidTrayClient-SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("WiFi-HID Client is already running (check the system tray).",
                "WiFi-HID Client", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.Run(new TrayApplicationContext());

        GC.KeepAlive(singleInstanceMutex);
    }
}
