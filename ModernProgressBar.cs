using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VramMonitor
{
    public sealed class ModernProgressBar : Control
    {
        private int _value;
        private int _minimum = 0;
        private int _maximum = 100;
        private Color _trackColor = Color.FromArgb(50, 50, 50);
        private Color _fillColor = Color.FromArgb(118, 185, 0);

        public ModernProgressBar()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);
            BackColor = Color.Transparent;
            Height = 18;
        }

        public int Minimum
        {
            get => _minimum;
            set
            {
                _minimum = value;
                if (_value < _minimum) _value = _minimum;
                Invalidate();
            }
        }

        public int Value
        {
            get => _value;
            set
            {
                int clamped = Math.Max(_minimum, Math.Min(_maximum, value));
                if (_value != clamped)
                {
                    _value = clamped;
                    Invalidate();
                }
            }
        }

        public int Maximum
        {
            get => _maximum;
            set
            {
                if (value > 0 && _maximum != value)
                {
                    _maximum = value;
                    if (_value > _maximum) _value = _maximum;
                    Invalidate();
                }
            }
        }

        public Color TrackColor
        {
            get => _trackColor;
            set
            {
                if (_trackColor != value)
                {
                    _trackColor = value;
                    Invalidate();
                }
            }
        }

        public Color FillColor
        {
            get => _fillColor;
            set
            {
                if (_fillColor != value)
                {
                    _fillColor = value;
                    Invalidate();
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = ClientRectangle;
            if (rect.Width <= 0 || rect.Height <= 0) return;

            // 角丸の半径
            float cornerRadius = 4f;
            using (var trackPath = GetRoundedRectanglePath(new RectangleF(rect.X, rect.Y, rect.Width - 1, rect.Height - 1), cornerRadius))
            using (var trackBrush = new SolidBrush(_trackColor))
            {
                g.FillPath(trackBrush, trackPath);
            }

            if (_value > 0 && _maximum > 0)
            {
                float percent = (float)_value / _maximum;
                float fillWidth = Math.Max(cornerRadius * 2, (rect.Width - 1) * percent);
                if (fillWidth > rect.Width - 1) fillWidth = rect.Width - 1;

                var fillRect = new RectangleF(rect.X, rect.Y, fillWidth, rect.Height - 1);
                using (var fillPath = GetRoundedRectanglePath(fillRect, cornerRadius))
                using (var fillBrush = new SolidBrush(_fillColor))
                {
                    g.FillPath(fillBrush, fillPath);
                }
            }
        }

        private static GraphicsPath GetRoundedRectanglePath(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float diameter = radius * 2;
            if (rect.Width < diameter || rect.Height < diameter)
            {
                path.AddRectangle(rect);
                return path;
            }

            var arc = new RectangleF(rect.X, rect.Y, diameter, diameter);
            // Top-left
            path.AddArc(arc, 180, 90);
            // Top-right
            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);
            // Bottom-right
            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            // Bottom-left
            arc.X = rect.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
