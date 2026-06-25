using System.Drawing;
using System.Drawing.Drawing2D;

namespace SPRecorder.Tray;

internal static class IconFactory
{
    public static Icon CreateCircle(Color color, int size = 32)
    {
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            int pad = size / 8;
            g.FillEllipse(brush, pad, pad, size - 2 * pad, size - 2 * pad);
        }
        IntPtr hIcon = bmp.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    public static Icon CreateCircleWithBadge(Color baseColor, Color badgeColor, int size = 32)
    {
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            int pad = size / 8;
            using (var brush = new SolidBrush(baseColor))
                g.FillEllipse(brush, pad, pad, size - 2 * pad, size - 2 * pad);

            // Warning badge: a filled dot with a thin contrasting outline, lower-right.
            int d = size * 7 / 16;                 // badge diameter (~14px at 32)
            int x = size - d - 1, y = size - d - 1;
            using (var outline = new SolidBrush(Color.FromArgb(40, 40, 40)))
                g.FillEllipse(outline, x - 1, y - 1, d + 2, d + 2);
            using (var badge = new SolidBrush(badgeColor))
                g.FillEllipse(badge, x, y, d, d);
        }
        IntPtr hIcon = bmp.GetHicon();
        return Icon.FromHandle(hIcon);
    }
}
