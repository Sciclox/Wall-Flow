using System;
using System.Drawing;

namespace WallFlow;

internal static class MenuIcons
{
    public static Bitmap CreateGearIcon()
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

        using var pen = new Pen(Color.White, 1.5f);

        var cx = 8; var cy = 8;
        g.DrawEllipse(pen, cx - 3, cy - 3, 6, 6);

        for (var i = 0; i < 4; i++)
        {
            var angle = i * 45;
            var rad = angle * Math.PI / 180;
            var x1 = cx + (float)(4.5 * Math.Cos(rad));
            var y1 = cy + (float)(4.5 * Math.Sin(rad));
            var x2 = cx + (float)(7 * Math.Cos(rad));
            var y2 = cy + (float)(7 * Math.Sin(rad));
            g.DrawLine(pen, x1, y1, x2, y2);
        }

        return bmp;
    }

    public static Bitmap CreatePowerIcon()
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

        using var pen = new Pen(Color.White, 1.5f);

        g.DrawArc(pen, 3, 4, 10, 10, -110, 220);

        g.DrawLine(pen, 8, 2, 8, 7);

        return bmp;
    }
}
