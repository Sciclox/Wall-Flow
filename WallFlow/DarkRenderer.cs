using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WallFlow;

internal class DarkRenderer : ToolStripProfessionalRenderer
{
    private const int BorderRadius = 8;
    private const int ItemRadius = 6;

    public DarkRenderer() : base(new DarkColorTable()) { }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = Color.White;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        using var path = GetRoundedRect(rect, BorderRadius);
        using var pen = new Pen(Color.FromArgb(60, 60, 60), 1);
        g.DrawPath(pen, path);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(2, 1, e.Item.ContentRectangle.Width - 4, e.Item.ContentRectangle.Height - 2);

        using var path = GetRoundedRect(rect, ItemRadius);
        using var brush = e.Item.Selected
            ? new SolidBrush(Color.FromArgb(60, 60, 60))
            : new SolidBrush(Color.Transparent);
        g.FillPath(brush, path);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var r = e.ImageRectangle;
        g.FillRectangle(new SolidBrush(Color.FromArgb(42, 42, 42)), r);

        using var pen = new Pen(Color.White, 2);
        var cx = r.X + r.Width / 2;
        var cy = r.Y + r.Height / 2;

        g.DrawLine(pen, cx - 4, cy, cx - 1, cy + 3);
        g.DrawLine(pen, cx - 1, cy + 3, cx + 4, cy - 2);
    }

    private static GraphicsPath GetRoundedRect(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;

        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }
}
