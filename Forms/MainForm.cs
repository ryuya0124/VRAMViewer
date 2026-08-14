using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using VramMonitor.Models;
using VramMonitor.Native;
using VramMonitor.Services;
using VramMonitor.Theme;

namespace VramMonitor.Forms
{
    public sealed partial class MainForm : Form
    {
        private const int DefaultRefreshIntervalMs = 1500;
        private const int WM_SETTINGCHANGE         = 0x001A;
        private const int WM_THEMECHANGED          = 0x031A;

        // --- Services & State ---
        private readonly System.Windows.Forms.Timer _timer;
        private readonly GpuProcessCollector        _collector = new();

        private AppTheme          _themeMode = AppTheme.Auto;
        private bool              _isDarkMode;
        private int               _refreshInterval = DefaultRefreshIntervalMs;
        private IntPtr            _device;
        private bool              _nvmlReady;
        private NvmlStatus        _nvmlStatus = NvmlStatus.NotAttempted;
        private string            _nvmlStatusMessage = "";
        private List<AdapterInfo> _adapters = new();
        private AdapterInfo?      _selectedAdapter;
        private int               _sortColumn = 2; // デフォルト: 専用 VRAM
        private SortOrder         _sortOrder  = SortOrder.Descending;

        public MainForm()
        {
            InitializeComponent();

            DoubleBuffered = true;
            SetStyle(
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.AllPaintingInWmPaint,
                true);

            _timer = new System.Windows.Forms.Timer { Interval = _refreshInterval };
            _timer.Tick += (_, _) => RefreshData();

            Load        += OnLoad;
            FormClosing += OnFormClosing;

            I18n.LanguageChanged += OnLanguageChanged;
        }

        // ----------------------------------------------------------------
        // Localization Management
        // ----------------------------------------------------------------

        private void SetLanguage(string cultureCode)
        {
            I18n.SelectedLanguageCode = cultureCode;
        }

        private void OnLanguageChanged()
        {
            ApplyLocalization();
            ApplyTheme(); // テーマボタン等のテキスト更新を含む
            UpdateBannerStatus();
            RefreshData();
        }

        private void BuildLanguageMenu()
        {
            _menuLanguage.DropDownItems.Clear();

            // 1. 自動検出
            var autoItem = new ToolStripMenuItem(I18n.T("LangAuto"))
            {
                Checked = I18n.SelectedLanguageCode == I18n.AutoLanguageCode
            };
            autoItem.Click += (_, _) => SetLanguage(I18n.AutoLanguageCode);
            _menuLanguage.DropDownItems.Add(autoItem);

            _menuLanguage.DropDownItems.Add(new ToolStripSeparator());

            // 2. 検出された各言語パック
            foreach (var pack in I18n.AvailableLanguages)
            {
                var langItem = new ToolStripMenuItem(pack.DisplayName)
                {
                    Checked = I18n.SelectedLanguageCode.Equals(pack.CultureCode, StringComparison.OrdinalIgnoreCase)
                };
                string code = pack.CultureCode;
                langItem.Click += (_, _) => SetLanguage(code);
                _menuLanguage.DropDownItems.Add(langItem);
            }

            _menuLanguage.DropDownItems.Add(new ToolStripSeparator());

            // 3. 言語フォルダを開く
            var openFolderItem = new ToolStripMenuItem(I18n.T("MenuOpenLanguagesFolder"));
            openFolderItem.Click += (_, _) =>
            {
                I18n.OpenLanguagesFolder();
                // フォルダオープン後、メニュー再展開時などに反映できるようにリロード
                I18n.ReloadLanguages();
            };
            _menuLanguage.DropDownItems.Add(openFolderItem);
        }

        private void ApplyLocalization()
        {
            Text = I18n.T("AppTitle");

            // メニュー - File
            _menuFile.Text       = I18n.T("MenuFile");
            _menuRefreshNow.Text = I18n.T("MenuRefreshNow");
            _menuExit.Text       = I18n.T("MenuExit");

            // メニュー - View
            _menuView.Text        = I18n.T("MenuView");
            _menuTheme.Text       = I18n.T("MenuTheme");
            _menuThemeAuto.Text   = I18n.T("ThemeAuto");
            _menuThemeDark.Text   = I18n.T("ThemeDark");
            _menuThemeLight.Text  = I18n.T("ThemeLight");
            _menuAlwaysOnTop.Text = I18n.T("MenuAlwaysOnTop");

            // メニュー - Settings
            _menuSettings.Text        = I18n.T("MenuSettings");
            _menuRefreshInterval.Text = I18n.T("MenuRefreshInterval");
            _menuInterval500.Text     = I18n.T("Interval500ms");
            _menuInterval1000.Text    = I18n.T("Interval1000ms");
            _menuInterval1500.Text    = I18n.T("Interval1500ms");
            _menuInterval2000.Text    = I18n.T("Interval2000ms");
            _menuInterval3000.Text    = I18n.T("Interval3000ms");
            _menuInterval5000.Text    = I18n.T("Interval5000ms");

            _menuLanguage.Text = I18n.T("MenuLanguage");
            BuildLanguageMenu();

            // メニュー - Help
            _menuHelp.Text     = I18n.T("MenuHelp");
            _menuNvmlDiag.Text = I18n.T("MenuNvmlDiag");
            _menuAbout.Text    = I18n.T("MenuAbout");

            // ListView カラムヘッダー
            _colPid.Text       = I18n.T("ColPid");
            _colName.Text      = I18n.T("ColProcessName");
            _colDedicated.Text = I18n.T("ColDedicatedVram");
            _colShared.Text    = I18n.T("ColSharedVram");

            // チェックマーク同期
            UpdateMenuCheckStates();
        }

        private void UpdateMenuCheckStates()
        {
            // テーマ
            _menuThemeAuto.Checked  = _themeMode == AppTheme.Auto;
            _menuThemeDark.Checked  = _themeMode == AppTheme.Dark;
            _menuThemeLight.Checked = _themeMode == AppTheme.Light;

            // 更新頻度
            _menuInterval500.Checked  = _refreshInterval == 500;
            _menuInterval1000.Checked = _refreshInterval == 1000;
            _menuInterval1500.Checked = _refreshInterval == 1500;
            _menuInterval2000.Checked = _refreshInterval == 2000;
            _menuInterval3000.Checked = _refreshInterval == 3000;
            _menuInterval5000.Checked = _refreshInterval == 5000;

            // 最前面
            _menuAlwaysOnTop.Checked = TopMost;
        }

        private void SetRefreshInterval(int ms)
        {
            _refreshInterval = ms;
            _timer.Interval = _refreshInterval;
            UpdateMenuCheckStates();
        }

        private void SetTheme(AppTheme theme)
        {
            _themeMode = theme;
            ApplyTheme();
        }

        private void OnAlwaysOnTopClick(object? sender, EventArgs e)
        {
            TopMost = !TopMost;
            UpdateMenuCheckStates();
        }

        private void OnMenuAboutClick(object? sender, EventArgs e)
        {
            MessageBox.Show(
                I18n.T("AboutDialogMessage"),
                I18n.T("AboutDialogTitle"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        // ----------------------------------------------------------------
        // Theme Management
        // ----------------------------------------------------------------

        private void ApplyTheme()
        {
            _isDarkMode = ThemeManager.ResolveIsDark(_themeMode);

            _themeButton.Text = _themeMode switch
            {
                AppTheme.Auto  => I18n.T("ThemeAuto"),
                AppTheme.Dark  => I18n.T("ThemeDark"),
                AppTheme.Light => I18n.T("ThemeLight"),
                _ => I18n.T("ThemeAuto")
            };

            // MenuStrip のカスタムレンダラー
            _menuStrip.Renderer = new ModernMenuRenderer(_isDarkMode);

            if (IsHandleCreated)
            {
                ThemeManager.SetWindowDarkMode(Handle, _isDarkMode);
                ThemeManager.SetWindowTheme(_listView.Handle, _isDarkMode ? "DarkMode_Explorer" : "Explorer", null);
                ThemeManager.SetWindowTheme(_gpuSelector.Handle, _isDarkMode ? "DarkMode_CFD" : "Explorer", null);
            }

            if (_isDarkMode)
            {
                BackColor               = ThemeManager.Dark.WindowBg;
                _menuStrip.BackColor    = ThemeManager.Dark.HeaderBg;
                _headerPanel.BackColor  = ThemeManager.Dark.WindowBg;
                _listPanel.BackColor    = ThemeManager.Dark.WindowBg;

                _gpuNameLabel.ForeColor = ThemeManager.Dark.TextPrimary;
                _totalLabel.ForeColor   = ThemeManager.Dark.TextPrimary;
                _updatedLabel.ForeColor = ThemeManager.Dark.TextSecondary;

                _themeButton.BackColor  = ThemeManager.Dark.ControlBg;
                _themeButton.ForeColor  = ThemeManager.Dark.TextPrimary;
                _themeButton.FlatAppearance.BorderColor = ThemeManager.Dark.ControlBorder;

                _gpuSelector.BackColor  = ThemeManager.Dark.ControlBg;
                _gpuSelector.ForeColor  = ThemeManager.Dark.TextPrimary;

                _progressBar.TrackColor = ThemeManager.Dark.ProgressTrack;
                _progressBar.FillColor  = ThemeManager.Dark.ProgressFill;

                _listView.BackColor     = ThemeManager.Dark.ListBg;
                _listView.ForeColor     = ThemeManager.Dark.ListText;

                _bannerPanel.BackColor  = ThemeManager.Dark.BannerBg;
                _bannerLabel.ForeColor  = ThemeManager.Dark.BannerText;
            }
            else
            {
                BackColor               = ThemeManager.Light.WindowBg;
                _menuStrip.BackColor    = ThemeManager.Light.HeaderBg;
                _headerPanel.BackColor  = ThemeManager.Light.HeaderBg;
                _listPanel.BackColor    = ThemeManager.Light.WindowBg;

                _gpuNameLabel.ForeColor = ThemeManager.Light.TextPrimary;
                _totalLabel.ForeColor   = ThemeManager.Light.TextPrimary;
                _updatedLabel.ForeColor = ThemeManager.Light.TextSecondary;

                _themeButton.BackColor  = ThemeManager.Light.ControlBg;
                _themeButton.ForeColor  = ThemeManager.Light.TextPrimary;
                _themeButton.FlatAppearance.BorderColor = ThemeManager.Light.ControlBorder;

                _gpuSelector.BackColor  = ThemeManager.Light.ControlBg;
                _gpuSelector.ForeColor  = ThemeManager.Light.TextPrimary;

                _progressBar.TrackColor = ThemeManager.Light.ProgressTrack;
                _progressBar.FillColor  = ThemeManager.Light.ProgressFill;

                _listView.BackColor     = ThemeManager.Light.ListBg;
                _listView.ForeColor     = ThemeManager.Light.ListText;

                _bannerPanel.BackColor  = ThemeManager.Light.BannerBg;
                _bannerLabel.ForeColor  = ThemeManager.Light.BannerText;
            }

            UpdateMenuCheckStates();
            _menuStrip.Invalidate();
            _gpuSelector.Invalidate();
            _listView.Invalidate();
            RefreshData();
        }

        private void OnThemeButtonClick(object? sender, EventArgs e)
        {
            _themeMode = _themeMode switch
            {
                AppTheme.Auto  => AppTheme.Dark,
                AppTheme.Dark  => AppTheme.Light,
                AppTheme.Light => AppTheme.Auto,
                _ => AppTheme.Auto
            };

            ApplyTheme();
        }

        private void OnGpuSelectorDrawItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _gpuSelector.Items.Count) return;

            bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            var itemText = _gpuSelector.Items[e.Index]?.ToString() ?? "";

            Color bg = _isDarkMode
                ? (isSelected ? Color.FromArgb(60, 60, 65) : ThemeManager.Dark.ControlBg)
                : (isSelected ? SystemColors.Highlight : ThemeManager.Light.ControlBg);

            Color fg = _isDarkMode
                ? ThemeManager.Dark.TextPrimary
                : (isSelected ? SystemColors.HighlightText : ThemeManager.Light.TextPrimary);

            using var brushBg = new SolidBrush(bg);
            e.Graphics.FillRectangle(brushBg, e.Bounds);

            var textRect = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 12), e.Bounds.Height);
            var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(e.Graphics, itemText, e.Font ?? Font, textRect, fg, flags);

            if ((e.State & DrawItemState.Focus) == DrawItemState.Focus && !isSelected)
            {
                e.DrawFocusRectangle();
            }
        }

        private void OnListViewColumnClick(object? sender, ColumnClickEventArgs e)
        {
            if (e.Column == _sortColumn)
            {
                _sortOrder = _sortOrder == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
            }
            else
            {
                _sortColumn = e.Column;
                // 専用VRAM(2)、共有VRAM(3)は降順から、PID(0)、プロセス名(1)は昇順から開始
                _sortOrder = (e.Column == 2 || e.Column == 3) ? SortOrder.Descending : SortOrder.Ascending;
            }

            _listView.Invalidate();
            RefreshData();
        }

        private void OnListViewDrawColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
        {
            Color headerBg    = _isDarkMode ? Color.FromArgb(42, 42, 45) : SystemColors.Control;
            Color headerFg    = _isDarkMode ? ThemeManager.Dark.TextPrimary : SystemColors.ControlText;
            Color borderColor = _isDarkMode ? ThemeManager.Dark.ControlBorder : SystemColors.ControlDark;

            // 背景描画
            using var brushBg = new SolidBrush(headerBg);
            e.Graphics.FillRectangle(brushBg, e.Bounds);

            // 右側の区切り線と下線を描画
            using var penBorder = new Pen(borderColor);
            e.Graphics.DrawLine(penBorder, e.Bounds.Right - 1, e.Bounds.Top + 2, e.Bounds.Right - 1, e.Bounds.Bottom - 3);
            e.Graphics.DrawLine(penBorder, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);

            bool isSorted = e.ColumnIndex == _sortColumn && _sortOrder != SortOrder.None;

            // ソート矢印を描画
            if (isSorted)
            {
                int arrowSize = 6;
                int arrowX = e.Bounds.Right - 16;
                int arrowY = e.Bounds.Y + (e.Bounds.Height - arrowSize) / 2;

                Point[] points;
                if (_sortOrder == SortOrder.Ascending)
                {
                    points = new[]
                    {
                        new Point(arrowX + arrowSize / 2, arrowY),
                        new Point(arrowX + arrowSize, arrowY + arrowSize),
                        new Point(arrowX, arrowY + arrowSize)
                    };
                }
                else
                {
                    points = new[]
                    {
                        new Point(arrowX, arrowY),
                        new Point(arrowX + arrowSize, arrowY),
                        new Point(arrowX + arrowSize / 2, arrowY + arrowSize)
                    };
                }

                using var arrowBrush = new SolidBrush(headerFg);
                e.Graphics.FillPolygon(arrowBrush, points);
            }

            // ヘッダーテキスト描画
            var align = e.Header?.TextAlign ?? HorizontalAlignment.Left;
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            flags |= align switch
            {
                HorizontalAlignment.Right  => TextFormatFlags.Right,
                HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter,
                _                          => TextFormatFlags.Left
            };

            int paddingRight = isSorted ? 22 : 12;
            var textRect = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, Math.Max(0, e.Bounds.Width - paddingRight - 6), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", e.Font ?? _listView.Font, textRect, headerFg, flags);
        }

        private void OnListViewDrawItem(object? sender, DrawListViewItemEventArgs e)
        {
            // View.Details では DrawSubItem で各列を描画
        }

        private void OnListViewDrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
        {
            if (e.Item == null) return;

            bool isSelected = e.Item.Selected;
            bool isSystem = e.Item.Tag is string key && key == "SYSTEM";

            // 背景色の決定
            Color bgColor;
            if (isSelected)
            {
                bgColor = _isDarkMode
                    ? Color.FromArgb(55, 55, 62)
                    : Color.FromArgb(204, 232, 255);
            }
            else
            {
                bgColor = _isDarkMode ? ThemeManager.Dark.ListBg : ThemeManager.Light.ListBg;
            }

            using (var brush = new SolidBrush(bgColor))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }

            // 前景色（テキスト色）の決定
            Color fgColor;
            if (isSelected)
            {
                fgColor = _isDarkMode ? Color.White : Color.Black;
            }
            else
            {
                if (isSystem)
                {
                    fgColor = _isDarkMode ? ThemeManager.Dark.SystemRowText : ThemeManager.Light.SystemRowText;
                }
                else
                {
                    fgColor = _isDarkMode ? ThemeManager.Dark.ListText : ThemeManager.Light.ListText;
                }
            }

            // アライメントの決定
            var align = e.Header?.TextAlign ?? HorizontalAlignment.Left;
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            flags |= align switch
            {
                HorizontalAlignment.Right  => TextFormatFlags.Right,
                HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter,
                _                          => TextFormatFlags.Left
            };

            // パディング（左右にマージンを持たせて描画）
            var textRect = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 12), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? "", e.SubItem?.Font ?? e.Item.Font, textRect, fgColor, flags);
        }

        private void OnListViewResize(object? sender, EventArgs e)
        {
            AdjustColumnWidths();
        }

        private void AdjustColumnWidths()
        {
            if (_listView.Columns.Count < 4) return;

            int clientWidth = _listView.ClientSize.Width;
            if (clientWidth <= 0) return;

            int pidWidth = 70;
            int dedicatedWidth = 150;
            int sharedWidth = 150;
            int nameWidth = clientWidth - (pidWidth + dedicatedWidth + sharedWidth);

            if (nameWidth < 120) nameWidth = 120;

            _listView.Columns[0].Width = pidWidth;
            _listView.Columns[1].Width = nameWidth;
            _listView.Columns[2].Width = dedicatedWidth;
            _listView.Columns[3].Width = sharedWidth;
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg == WM_SETTINGCHANGE || m.Msg == WM_THEMECHANGED)
            {
                if (_themeMode == AppTheme.Auto)
                {
                    ApplyTheme();
                }
            }
        }

        private void OnBannerClick(object? sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(_nvmlStatusMessage))
            {
                MessageBox.Show(
                    _nvmlStatusMessage,
                    I18n.T("NvmlDiagDialogTitle"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            else
            {
                MessageBox.Show(
                    _nvmlReady ? "NVML is initialized and operational." : "NVML is not active.",
                    I18n.T("NvmlDiagDialogTitle"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        // ----------------------------------------------------------------
        // Initialization
        // ----------------------------------------------------------------

        private void OnLoad(object? sender, EventArgs e)
        {
            ApplyLocalization();
            ApplyTheme();
            AdjustColumnWidths();

            _gpuSelector.SelectedIndexChanged -= OnGpuSelected;

            _adapters = DxgiHelper.GetAllAdapters();
            _gpuSelector.BeginUpdate();
            foreach (var a in _adapters)
                _gpuSelector.Items.Add(a);
            _gpuSelector.EndUpdate();

            _gpuSelector.SelectedIndexChanged += OnGpuSelected;

            var (status, message, device) = Nvml.TryInitialize();
            _nvmlStatus = status;
            _nvmlStatusMessage = message;
            if (status == NvmlStatus.Ready)
            {
                _nvmlReady = true;
                _device = device;
            }

            if (_adapters.Count == 0)
            {
                _gpuNameLabel.Text = I18n.T("GpuNotFound");
                return;
            }

            var preferred = _adapters.FirstOrDefault(a => a.IsNvidia) ?? _adapters[0];
            _gpuSelector.SelectedItem = preferred;

            UpdateBannerStatus();
            _timer.Start();
        }

        private void UpdateBannerStatus()
        {
            if (_selectedAdapter != null && _selectedAdapter.IsNvidia && !_nvmlReady)
            {
                if (_nvmlStatus == NvmlStatus.DllNotFound)
                {
                    _bannerLabel.Text = I18n.T("BannerNvmlDllNotFound");
                    _bannerPanel.Visible = true;
                }
                else if (_nvmlStatus == NvmlStatus.DriverNotLoaded)
                {
                    _bannerLabel.Text = I18n.T("BannerNvmlDriverNotLoaded");
                    _bannerPanel.Visible = true;
                }
                else if (_nvmlStatus != NvmlStatus.Ready)
                {
                    _bannerLabel.Text = I18n.T("BannerNvmlError");
                    _bannerPanel.Visible = true;
                }
            }
            else
            {
                _bannerPanel.Visible = false;
            }
        }

        private void OnGpuSelected(object? sender, EventArgs e)
        {
            _selectedAdapter = _gpuSelector.SelectedItem as AdapterInfo;
            if (_selectedAdapter != null && _selectedAdapter.IsNvidia && _nvmlReady)
            {
                _device = Nvml.GetDeviceHandleForAdapter(_selectedAdapter);
            }
            else
            {
                _device = IntPtr.Zero;
            }

            _collector.ClearCache();
            UpdateBannerStatus();
            RefreshData();
        }

        // ----------------------------------------------------------------
        // Data refresh
        // ----------------------------------------------------------------

        private void RefreshData()
        {
            if (_selectedAdapter == null) return;

            try
            {
                var rows = GpuProcessCollector.CollectProcesses(_selectedAdapter);

                ulong totalDedicated = 0;
                ulong maxDedicated = _selectedAdapter.DedicatedVideoMemory > 0
                    ? _selectedAdapter.DedicatedVideoMemory
                    : _selectedAdapter.SharedSystemMemory;

                bool isNvmlActive = false;

                // 1. NVIDIA + NVML から総専用VRAM使用量を正確に取得
                if (_selectedAdapter.IsNvidia && _nvmlReady && _device != IntPtr.Zero)
                {
                    if (Nvml.DeviceGetMemoryInfo(_device, out var mem) == Nvml.NvmlReturn.Success)
                    {
                        totalDedicated = mem.Used;
                        if (mem.Total > 0) maxDedicated = mem.Total;
                        isNvmlActive = true;
                    }
                }

                // 2. NVML が利用できない場合は DXGI から総専用使用量を取得
                if (!isNvmlActive)
                {
                    var seg = DxgiHelper.QueryVideoMemory(_selectedAdapter.LuidLow, _selectedAdapter.LuidHigh, 0);
                    if (seg.HasValue && seg.Value.CurrentUsage > 0)
                    {
                        totalDedicated = seg.Value.CurrentUsage;
                        if (seg.Value.Budget > 0) maxDedicated = seg.Value.Budget;
                    }
                }

                // 3. 各プロセスの専用VRAM合計を算出
                ulong processDedicatedSum = 0;
                foreach (var r in rows)
                {
                    processDedicatedSum += r.LocalBytes;
                }

                // 4. ドライバ・システム/カーネル予約領域 (差分) を算出
                if (totalDedicated > processDedicatedSum)
                {
                    ulong sysDedicated = totalDedicated - processDedicatedSum;
                    if (sysDedicated > 0)
                    {
                        rows.Add(new ProcessRow(0, sysDedicated, 0, isSystem: true));
                    }
                }
                else if (totalDedicated == 0)
                {
                    // 総使用量が直接取得できない場合はプロセス合計を総使用量とする
                    totalDedicated = processDedicatedSum;
                }

                // 5. ソート設定に従ってプロセス行を並び替え
                SortRows(rows);

                // 6. ヘッダーおよびリストの差分更新
                UpdateHeader(rows, totalDedicated, maxDedicated, isNvmlActive);
                UpdateListViewRows(rows);

                string updatedText = I18n.T("LastUpdated", DateTime.Now.ToString("HH:mm:ss"));
                if (_updatedLabel.Text != updatedText)
                {
                    _updatedLabel.Text = updatedText;
                }
            }
            catch (NvmlException ex)
            {
                if (_totalLabel.Text != ex.Message)
                    _totalLabel.Text = ex.Message;
            }
        }

        private void SortRows(List<ProcessRow> rows)
        {
            string systemProcessName = I18n.T("SystemKernelProcess");

            rows.Sort((a, b) =>
            {
                int cmp = 0;
                switch (_sortColumn)
                {
                    case 0: // PID
                        cmp = a.Pid.CompareTo(b.Pid);
                        break;
                    case 1: // プロセス名
                        string nameA = a.IsSystem ? systemProcessName : _collector.GetProcessName(a.Pid);
                        string nameB = b.IsSystem ? systemProcessName : _collector.GetProcessName(b.Pid);
                        cmp = string.Compare(nameA, nameB, StringComparison.CurrentCultureIgnoreCase);
                        break;
                    case 2: // 専用 VRAM
                        cmp = a.LocalBytes.CompareTo(b.LocalBytes);
                        break;
                    case 3: // 共有 VRAM
                        cmp = a.NonLocalBytes.CompareTo(b.NonLocalBytes);
                        break;
                    default:
                        cmp = a.TotalBytes.CompareTo(b.TotalBytes);
                        break;
                }

                if (_sortOrder == SortOrder.Descending)
                {
                    cmp = -cmp;
                }

                // タイブレーク（同値の場合の安定順序）
                if (cmp == 0)
                {
                    cmp = b.TotalBytes.CompareTo(a.TotalBytes);
                    if (cmp == 0)
                    {
                        cmp = a.Pid.CompareTo(b.Pid);
                    }
                }

                return cmp;
            });
        }

        private void UpdateListViewRows(List<ProcessRow> rows)
        {
            Color systemColor = _isDarkMode ? ThemeManager.Dark.SystemRowText : ThemeManager.Light.SystemRowText;
            Color defaultColor = _isDarkMode ? ThemeManager.Dark.ListText : ThemeManager.Light.ListText;
            string systemProcessName = I18n.T("SystemKernelProcess");

            // 既存アイテムを Tag (キー) でマップ化
            var existingItems = new Dictionary<string, ListViewItem>(_listView.Items.Count);
            foreach (ListViewItem item in _listView.Items)
            {
                if (item.Tag is string key)
                {
                    existingItems[key] = item;
                }
            }

            var targetKeys = new HashSet<string>(rows.Count);
            foreach (var r in rows)
            {
                targetKeys.Add(r.IsSystem ? "SYSTEM" : r.Pid.ToString());
            }

            bool structureChanged = false;

            // 1. 存在しなくなった行を削除 (後ろから走査)
            for (int i = _listView.Items.Count - 1; i >= 0; i--)
            {
                var item = _listView.Items[i];
                if (item.Tag is string key && !targetKeys.Contains(key))
                {
                    if (!structureChanged)
                    {
                        _listView.BeginUpdate();
                        structureChanged = true;
                    }
                    _listView.Items.RemoveAt(i);
                    existingItems.Remove(key);
                }
            }

            // 2. 順序の調整およびセル単位の部分差分更新
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                string key = row.IsSystem ? "SYSTEM" : row.Pid.ToString();
                string pidText = row.IsSystem ? "-" : row.Pid.ToString();
                string name = row.IsSystem
                    ? systemProcessName
                    : _collector.GetProcessName(row.Pid);
                string dedicated = FormatHelper.FormatVram(row.LocalBytes);
                string shared = FormatHelper.FormatVram(row.NonLocalBytes);
                Color rowColor = row.IsSystem ? systemColor : defaultColor;

                if (i < _listView.Items.Count && (string?)_listView.Items[i].Tag == key)
                {
                    // 同じ位置にある既存行 -> テキストが変わったセルのみ部分更新
                    var item = _listView.Items[i];
                    UpdateSubItem(item, 0, pidText);
                    UpdateSubItem(item, 1, name);
                    UpdateSubItem(item, 2, dedicated);
                    UpdateSubItem(item, 3, shared);
                    if (item.ForeColor != rowColor) item.ForeColor = rowColor;
                }
                else
                {
                    // 位置が変わった、または新規行
                    if (!structureChanged)
                    {
                        _listView.BeginUpdate();
                        structureChanged = true;
                    }

                    if (existingItems.TryGetValue(key, out var existingItem))
                    {
                        _listView.Items.Remove(existingItem);
                        UpdateSubItem(existingItem, 0, pidText);
                        UpdateSubItem(existingItem, 1, name);
                        UpdateSubItem(existingItem, 2, dedicated);
                        UpdateSubItem(existingItem, 3, shared);
                        if (existingItem.ForeColor != rowColor) existingItem.ForeColor = rowColor;
                        _listView.Items.Insert(i, existingItem);
                    }
                    else
                    {
                        var newItem = new ListViewItem(new[] { pidText, name, dedicated, shared })
                        {
                            Tag = key,
                            ForeColor = rowColor
                        };
                        _listView.Items.Insert(i, newItem);
                        existingItems[key] = newItem;
                    }
                }
            }

            if (structureChanged)
            {
                _listView.EndUpdate();
            }
        }

        private static void UpdateSubItem(ListViewItem item, int index, string text)
        {
            if (index < item.SubItems.Count)
            {
                if (item.SubItems[index].Text != text)
                {
                    item.SubItems[index].Text = text;
                }
            }
        }

        private void UpdateHeader(List<ProcessRow> rows, ulong totalDedicatedUsed, ulong maxDedicatedBytes, bool isNvmlActive)
        {
            if (_selectedAdapter == null) return;

            if (_gpuNameLabel.Text != _selectedAdapter.Name)
                _gpuNameLabel.Text = _selectedAdapter.Name;

            double usedGb  = totalDedicatedUsed / 1024.0 / 1024.0 / 1024.0;
            double totalGb = maxDedicatedBytes  / 1024.0 / 1024.0 / 1024.0;
            double pct     = maxDedicatedBytes > 0 ? (double)totalDedicatedUsed / maxDedicatedBytes * 100.0 : 0.0;

            string dedicatedLabel = I18n.T("HeaderDedicated");
            string sharedLabel = I18n.T("HeaderShared");

            string shared = "";
            if (_selectedAdapter.SharedSystemMemory > 0)
            {
                ulong sharedSum = 0;
                foreach (var row in rows)
                {
                    if (!row.IsSystem) sharedSum += row.NonLocalBytes;
                }
                double sharedUsedGb  = sharedSum / 1024.0 / 1024.0 / 1024.0;
                double sharedTotalGb = _selectedAdapter.SharedSystemMemory / 1024.0 / 1024.0 / 1024.0;
                shared = $"  {sharedLabel}: {sharedUsedGb:0.00} GB / {sharedTotalGb:0.00} GB";
            }

            string sourceTag = isNvmlActive ? "  ※ NVML" : "";
            string headerText = maxDedicatedBytes > 0
                ? $"{dedicatedLabel}: {usedGb:0.00} GB / {totalGb:0.00} GB ({pct:0}%){shared}{sourceTag}"
                : I18n.T("NoGpuMemoryInfo");

            if (_totalLabel.Text != headerText)
                _totalLabel.Text = headerText;

            _progressBar.Value = maxDedicatedBytes > 0 ? FormatHelper.ClampPercent(pct) : 0;
        }

        // ----------------------------------------------------------------
        // Cleanup
        // ----------------------------------------------------------------

        private void OnFormClosing(object? sender, FormClosingEventArgs e)
        {
            I18n.LanguageChanged -= OnLanguageChanged;
            _timer.Stop();
            if (_nvmlReady)
            {
                try { Nvml.Shutdown(); }
                catch { }
            }
        }
    }
}
