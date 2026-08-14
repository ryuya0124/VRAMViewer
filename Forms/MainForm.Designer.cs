using System.Drawing;
using System.Windows.Forms;
using VramMonitor.Controls;

namespace VramMonitor.Forms
{
    public sealed partial class MainForm
    {
        private MenuStrip         _menuStrip = null!;
        private ToolStripMenuItem _menuFile = null!;
        private ToolStripMenuItem _menuRefreshNow = null!;
        private ToolStripSeparator _menuSeparatorFile = null!;
        private ToolStripMenuItem _menuExit = null!;

        private ToolStripMenuItem _menuView = null!;
        private ToolStripMenuItem _menuTheme = null!;
        private ToolStripMenuItem _menuThemeAuto = null!;
        private ToolStripMenuItem _menuThemeDark = null!;
        private ToolStripMenuItem _menuThemeLight = null!;
        private ToolStripMenuItem _menuAlwaysOnTop = null!;

        private ToolStripMenuItem _menuSettings = null!;
        private ToolStripMenuItem _menuRefreshInterval = null!;
        private ToolStripMenuItem _menuInterval500 = null!;
        private ToolStripMenuItem _menuInterval1000 = null!;
        private ToolStripMenuItem _menuInterval1500 = null!;
        private ToolStripMenuItem _menuInterval2000 = null!;
        private ToolStripMenuItem _menuInterval3000 = null!;
        private ToolStripMenuItem _menuInterval5000 = null!;
        private ToolStripMenuItem _menuLanguage = null!;

        private ToolStripMenuItem _menuHelp = null!;
        private ToolStripMenuItem _menuNvmlDiag = null!;
        private ToolStripMenuItem _menuAbout = null!;

        private Panel             _headerPanel = null!;
        private Panel             _listPanel = null!;
        private Label             _gpuNameLabel = null!;
        private Button            _themeButton = null!;
        private ComboBox          _gpuSelector = null!;
        private Label             _totalLabel = null!;
        private ModernProgressBar _progressBar = null!;
        private DoubleBufferedListView _listView = null!;
        private ImageList         _imageList = null!;
        private ColumnHeader      _colPid = null!;
        private ColumnHeader      _colName = null!;
        private ColumnHeader      _colDedicated = null!;
        private ColumnHeader      _colShared = null!;
        private Label             _updatedLabel = null!;
        private Panel             _bannerPanel = null!;
        private Label             _bannerLabel = null!;

        private void InitializeComponent()
        {
            Text          = "VRAM Monitor";
            Width         = 780;
            Height        = 620;
            MinimumSize   = new Size(580, 440);
            StartPosition = FormStartPosition.CenterScreen;
            Font          = new Font("Segoe UI", 9F);

            // ---- MenuStrip ----
            _menuStrip = new MenuStrip
            {
                Dock = DockStyle.Top,
                Font = new Font("Segoe UI", 9F),
            };

            // File Menu
            _menuFile = new ToolStripMenuItem();
            _menuRefreshNow = new ToolStripMenuItem { ShortcutKeys = Keys.F5 };
            _menuRefreshNow.Click += (_, _) => RefreshData();
            _menuSeparatorFile = new ToolStripSeparator();
            _menuExit = new ToolStripMenuItem { ShortcutKeys = Keys.Alt | Keys.F4 };
            _menuExit.Click += (_, _) => Close();
            _menuFile.DropDownItems.AddRange(new ToolStripItem[] { _menuRefreshNow, _menuSeparatorFile, _menuExit });

            // View Menu
            _menuView = new ToolStripMenuItem();
            _menuTheme = new ToolStripMenuItem();
            _menuThemeAuto = new ToolStripMenuItem();
            _menuThemeAuto.Click += (_, _) => SetTheme(Theme.AppTheme.Auto);
            _menuThemeDark = new ToolStripMenuItem();
            _menuThemeDark.Click += (_, _) => SetTheme(Theme.AppTheme.Dark);
            _menuThemeLight = new ToolStripMenuItem();
            _menuThemeLight.Click += (_, _) => SetTheme(Theme.AppTheme.Light);
            _menuTheme.DropDownItems.AddRange(new ToolStripItem[] { _menuThemeAuto, _menuThemeDark, _menuThemeLight });

            _menuAlwaysOnTop = new ToolStripMenuItem { CheckOnClick = true };
            _menuAlwaysOnTop.Click += OnAlwaysOnTopClick;

            _menuView.DropDownItems.AddRange(new ToolStripItem[] { _menuTheme, _menuAlwaysOnTop });

            // Settings Menu
            _menuSettings = new ToolStripMenuItem();
            _menuRefreshInterval = new ToolStripMenuItem();
            _menuInterval500 = new ToolStripMenuItem();
            _menuInterval500.Click += (_, _) => SetRefreshInterval(500);
            _menuInterval1000 = new ToolStripMenuItem();
            _menuInterval1000.Click += (_, _) => SetRefreshInterval(1000);
            _menuInterval1500 = new ToolStripMenuItem();
            _menuInterval1500.Click += (_, _) => SetRefreshInterval(1500);
            _menuInterval2000 = new ToolStripMenuItem();
            _menuInterval2000.Click += (_, _) => SetRefreshInterval(2000);
            _menuInterval3000 = new ToolStripMenuItem();
            _menuInterval3000.Click += (_, _) => SetRefreshInterval(3000);
            _menuInterval5000 = new ToolStripMenuItem();
            _menuInterval5000.Click += (_, _) => SetRefreshInterval(5000);
            _menuRefreshInterval.DropDownItems.AddRange(new ToolStripItem[] {
                _menuInterval500, _menuInterval1000, _menuInterval1500,
                _menuInterval2000, _menuInterval3000, _menuInterval5000
            });

            _menuLanguage = new ToolStripMenuItem();
            _menuSettings.DropDownItems.AddRange(new ToolStripItem[] { _menuRefreshInterval, _menuLanguage });

            // Help Menu
            _menuHelp = new ToolStripMenuItem();
            _menuNvmlDiag = new ToolStripMenuItem();
            _menuNvmlDiag.Click += OnBannerClick;
            _menuAbout = new ToolStripMenuItem();
            _menuAbout.Click += OnMenuAboutClick;
            _menuHelp.DropDownItems.AddRange(new ToolStripItem[] { _menuNvmlDiag, _menuAbout });

            _menuStrip.Items.AddRange(new ToolStripItem[] { _menuFile, _menuView, _menuSettings, _menuHelp });
            MainMenuStrip = _menuStrip;

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
                Width     = 90,
                Height    = 26,
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Segoe UI", 8F),
                Cursor    = Cursors.Hand,
            };
            _themeButton.FlatAppearance.BorderSize = 1;
            _themeButton.Click += OnThemeButtonClick;

            _gpuNameLabel = new Label
            {
                Text     = "",
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
            _imageList = new ImageList
            {
                ImageSize = new Size(16, 16),
                ColorDepth = ColorDepth.Depth32Bit
            };

            _listView = new DoubleBufferedListView
            {
                Dock           = DockStyle.Fill,
                View           = View.Details,
                FullRowSelect  = true,
                GridLines      = true,
                BorderStyle    = BorderStyle.None,
                OwnerDraw      = true,
                SmallImageList = _imageList,
            };
            _colPid       = new ColumnHeader { Text = "PID", Width = 70 };
            _colName      = new ColumnHeader { Text = "Process Name", Width = 330 };
            _colDedicated = new ColumnHeader { Text = "Dedicated VRAM", Width = 150, TextAlign = HorizontalAlignment.Right };
            _colShared    = new ColumnHeader { Text = "Shared VRAM", Width = 150, TextAlign = HorizontalAlignment.Right };

            _listView.Columns.AddRange(new[] { _colPid, _colName, _colDedicated, _colShared });
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

            // Controls 階層順序
            Controls.Add(_listPanel);
            Controls.Add(_updatedLabel);
            Controls.Add(_bannerPanel);
            Controls.Add(_headerPanel);
            Controls.Add(_menuStrip);
        }
    }
}
