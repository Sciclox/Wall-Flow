using System.Drawing;
using System.Windows.Forms;

namespace WallFlow;

internal class DarkColorTable : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Color.FromArgb(42, 42, 42);
    public override Color ImageMarginGradientBegin => Color.FromArgb(42, 42, 42);
    public override Color ImageMarginGradientMiddle => Color.FromArgb(42, 42, 42);
    public override Color ImageMarginGradientEnd => Color.FromArgb(42, 42, 42);
    public override Color MenuItemSelected => Color.FromArgb(60, 60, 60);
    public override Color MenuItemSelectedGradientBegin => Color.FromArgb(60, 60, 60);
    public override Color MenuItemSelectedGradientEnd => Color.FromArgb(60, 60, 60);
    public override Color MenuItemBorder => Color.FromArgb(85, 85, 85);
    public override Color SeparatorDark => Color.FromArgb(80, 80, 80);
    public override Color SeparatorLight => Color.FromArgb(80, 80, 80);
    public override Color CheckBackground => Color.FromArgb(50, 50, 50);
    public override Color CheckSelectedBackground => Color.FromArgb(60, 60, 60);
    public override Color MenuBorder => Color.FromArgb(60, 60, 60);
}
