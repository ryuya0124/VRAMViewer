using System.Drawing;
using System.Windows.Forms;
using VramMonitor.Controls;

namespace VramMonitor.Forms
{
    public sealed partial class MainForm
    {
        private Panel             _headerPanel = null!;
        private Panel             _listPanel = null!;
        private Label             _gpuNameLabel = null!;
        private Button            _themeButton = null!;
        private ComboBox          _gpuSelector = null!;
        private Label             _totalLabel = null!;
        private ModernProgressBar _progressBar = null!;
        private DoubleBufferedListView _listView = null!;
        private Label             _updatedLabel = null!;
        private Panel             _bannerPanel = null!;
        private Label             _bannerLabel = null!;

        private void InitializeComponent()
        {
            Text          = "VRAM Monitor";
            Width         = 780;
            Height        = 600;
            MinimumSize   = new Size(580, 420);
            StartPosition = FormStartPosition.CenterScreen;
            Font          = new Font("Segoe UI", 9F);

            // ---- Banner panel (Warning / Status) ----
            _bannerPanel = new Panel
            {
                Dock    = DockStyle.Top,
                Height  = 30,
                Padding = new Padding(12, 6, 12, 6),
                Visible = false,
                Cursor  = Cursors.Hand,
            };
            _bannerLabel = new Label
            {
                Dock   = DockStyle.Fill,
                Font   = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Text   = "",
                Cursor = Cursors.Hand,
            };
            _bannerPanel.Controls.Add(_bannerLabel);
            _bannerPanel.Click += OnBannerClick;
            _bannerLabel.Click += OnBannerClick;

            // ---- Header panel ----
            _headerPanel = new Panel
            {
                Dock    = DockStyle.Top,
                Height  = 130,
                Padding = new Padding(12, 10, 12, 8),
            };

            // Top row container inside header: GPU Name (left) & Theme Toggle Button (right)
            var topRowPanel = new Panel
            {
                Dock   = DockStyle.Top,
                Height = 28,
            };

            _themeButton = new Button
            {
                Dock      = DockStyle.Right,
                Width     = 84,
                Height    = 26,
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Segoe UI", 8F),
                Cursor    = Cursors.Hand,
            };
            _themeButton.FlatAppearance.BorderSize = 1;
            _themeButton.Click += OnThemeButtonClick;

            _gpuNameLabel = new Label
            {
                Text     = "初期化中...",
                Font     = new Font("Segoe UI", 11.5F, FontStyle.Bold),
                Dock     = DockStyle.Fill,
                AutoSize = false,
            };

            topRowPanel.Controls.Add(_gpuNameLabel);
            topRowPanel.Controls.Add(_themeButton);

            _gpuSelector = new ComboBox
            {
                Dock          = DockStyle.Top,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DrawMode      = DrawMode.OwnerDrawFixed,
                ItemHeight    = 24,
                Height        = 30,
                Font          = new Font("Segoe UI", 9F),
                FlatStyle     = FlatStyle.Flat,
            };
            _gpuSelector.DrawItem += OnGpuSelectorDrawItem;

            _totalLabel = new Label
            {
                Text   = "",
                Dock   = DockStyle.Top,
                Height = 24,
            };

            _progressBar = new ModernProgressBar
            {
                Dock    = DockStyle.Top,
                Height  = 18,
                Minimum = 0,
                Maximum = 100,
            };

            // Stacked in reverse order (DockStyle.Top)
            _headerPanel.Controls.Add(_progressBar);
            _headerPanel.Controls.Add(_totalLabel);
            _headerPanel.Controls.Add(_gpuSelector);
            _headerPanel.Controls.Add(topRowPanel);

            // ---- Process list ----
            _listView = new DoubleBufferedListView
            {
                Dock          = DockStyle.Fill,
                View          = View.Details,
                FullRowSelect = true,
                GridLines     = true,
                BorderStyle   = BorderStyle.None,
                OwnerDraw     = true,
            };
            _listView.Columns.Add("PID",       70);
            _listView.Columns.Add("プロセス名", 330);
            _listView.Columns.Add("専用 VRAM",  150, HorizontalAlignment.Right);
            _listView.Columns.Add("共有 VRAM",  150, HorizontalAlignment.Right);
            _listView.ColumnClick      += OnListViewColumnClick;
            _listView.DrawColumnHeader += OnListViewDrawColumnHeader;
            _listView.DrawItem         += OnListViewDrawItem;
            _listView.DrawSubItem      += OnListViewDrawSubItem;
            _listView.Resize           += OnListViewResize;

            _listPanel = new Panel
            {
                Dock    = DockStyle.Fill,
                Padding = new Padding(12, 0, 12, 0)
            };
            _listPanel.Controls.Add(_listView);

            _updatedLabel = new Label
            {
                Text    = "",
                Dock    = DockStyle.Bottom,
                Height  = 24,
                Padding = new Padding(12, 0, 0, 6),
                Font    = new Font("Segoe UI", 8F),
            };

            Controls.Add(_listPanel);
            Controls.Add(_updatedLabel);
            Controls.Add(_bannerPanel);
            Controls.Add(_headerPanel);
        }
    }
}
