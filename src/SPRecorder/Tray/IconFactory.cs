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
}
