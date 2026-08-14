using System.Drawing;
using System.Windows.Forms;

namespace VramMonitor.Theme
{
    public class ModernMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly bool _isDark;

        public ModernMenuRenderer(bool isDark) : base(new ModernColorTable(isDark))
        {
            _isDark = isDark;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = _isDark ? ThemeManager.Dark.TextPrimary : ThemeManager.Light.TextPrimary;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected && !e.Item.Pressed)
            {
                base.OnRenderMenuItemBackground(e);
                return;
            }

            var g = e.Graphics;
            var bounds = new Rectangle(Point.Empty, e.Item.Size);
            Color highlightColor = _isDark
                ? Color.FromArgb(60, 60, 65)
                : Color.FromArgb(225, 235, 245);

            using var brush = new SolidBrush(highlightColor);
            g.FillRectangle(brush, bounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var g = e.Graphics;
            Color borderColor = _isDark ? ThemeManager.Dark.ControlBorder : Color.FromArgb(215, 215, 215);
            using var pen = new Pen(borderColor);
            if (e.ToolStrip is MenuStrip)
            {
                g.DrawLine(pen, 0, e.ToolStrip.Height - 1, e.ToolStrip.Width, e.ToolStrip.Height - 1);
            }
            else if (e.ToolStrip is ToolStripDropDown)
            {
                g.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            }
        }
    }

    public class ModernColorTable : ProfessionalColorTable
    {
        private readonly bool _isDark;

        public ModernColorTable(bool isDark)
        {
            _isDark = isDark;
            UseSystemColors = false;
        }

        public override Color MenuStripGradientBegin => _isDark ? ThemeManager.Dark.HeaderBg : ThemeManager.Light.HeaderBg;
        public override Color MenuStripGradientEnd => _isDark ? ThemeManager.Dark.HeaderBg : ThemeManager.Light.HeaderBg;

        public override Color ToolStripDropDownBackground => _isDark ? ThemeManager.Dark.ControlBg : Color.FromArgb(250, 250, 250);

        public override Color MenuItemSelected => _isDark ? Color.FromArgb(60, 60, 65) : Color.FromArgb(225, 235, 245);
        public override Color MenuItemSelectedGradientBegin => MenuItemSelected;
        public override Color MenuItemSelectedGradientEnd => MenuItemSelected;

        public override Color MenuItemPressedGradientBegin => _isDark ? Color.FromArgb(50, 50, 55) : Color.FromArgb(210, 220, 235);
        public override Color MenuItemPressedGradientEnd => MenuItemPressedGradientBegin;

        public override Color MenuItemBorder => Color.Transparent;

        public override Color MenuBorder => _isDark ? ThemeManager.Dark.ControlBorder : Color.FromArgb(200, 200, 200);

        public override Color ImageMarginGradientBegin => _isDark ? ThemeManager.Dark.ControlBg : Color.FromArgb(245, 245, 245);
        public override Color ImageMarginGradientMiddle => ImageMarginGradientBegin;
        public override Color ImageMarginGradientEnd => ImageMarginGradientBegin;

        public override Color CheckBackground => _isDark ? Color.FromArgb(70, 70, 75) : Color.FromArgb(210, 230, 250);
        public override Color CheckSelectedBackground => CheckBackground;
        public override Color CheckPressedBackground => CheckBackground;

        public override Color SeparatorDark => _isDark ? ThemeManager.Dark.ControlBorder : Color.FromArgb(220, 220, 220);
        public override Color SeparatorLight => Color.Transparent;
    }
}
