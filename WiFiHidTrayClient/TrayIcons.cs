using System.Drawing;

namespace WiFiHidTrayClient;

/// <summary>Small generated tray icons so the app doesn't need to ship an external .ico asset.</summary>
internal static class TrayIcons
{
    public static readonly Icon Idle = CreateIcon(Color.DodgerBlue);
    public static readonly Icon Capturing = CreateIcon(Color.OrangeRed);

    private static Icon CreateIcon(Color color)
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 1, 1, 13, 13);
            using var pen = new Pen(Color.Black, 1);
            g.DrawEllipse(pen, 1, 1, 13, 13);
        }

        // These two icons live for the lifetime of the process (created once at startup),
        // so the underlying HICON is intentionally left for the process to reclaim on exit.
        return Icon.FromHandle(bmp.GetHicon());
    }
}
