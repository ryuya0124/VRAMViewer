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
        private const int RefreshIntervalMs = 1500;
        private const int WM_SETTINGCHANGE  = 0x001A;
        private const int WM_THEMECHANGED   = 0x031A;

        // --- Services & State ---
        private readonly System.Windows.Forms.Timer _timer;
        private readonly GpuProcessCollector        _collector = new();

        private AppTheme          _themeMode = AppTheme.Auto;
        private bool              _isDarkMode;
        private IntPtr            _device;
        private bool              _nvmlReady;
        private NvmlStatus        _nvmlStatus = NvmlStatus.NotAttempted;
        private string            _nvmlStatusMessage = "";
        private List<AdapterInfo> _adapters = new();
        private AdapterInfo?      _selectedAdapter;

        public MainForm()
        {
            InitializeComponent();

            DoubleBuffered = true;
            SetStyle(
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.AllPaintingInWmPaint,
                true);

            _timer = new System.Windows.Forms.Timer { Interval = RefreshIntervalMs };
            _timer.Tick += (_, _) => RefreshData();

            Load        += OnLoad;
            FormClosing += OnFormClosing;
        }

        // ----------------------------------------------------------------
        // Theme Management
        // ----------------------------------------------------------------

        private void ApplyTheme()
        {
            _isDarkMode = ThemeManager.ResolveIsDark(_themeMode);

            _themeButton.Text = _themeMode switch
            {
                AppTheme.Auto  => "💻 自動",
                AppTheme.Dark  => "🌙 ダーク",
                AppTheme.Light => "☀️ ライト",
                _ => "💻 自動"
            };

            if (IsHandleCreated)
            {
                ThemeManager.SetWindowDarkMode(Handle, _isDarkMode);
                ThemeManager.SetWindowTheme(_listView.Handle, _isDarkMode ? "DarkMode_Explorer" : "Explorer", null);
                ThemeManager.SetWindowTheme(_gpuSelector.Handle, _isDarkMode ? "DarkMode_CFD" : "Explorer", null);
            }

            if (_isDarkMode)
            {
                BackColor               = ThemeManager.Dark.WindowBg;
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

            // ヘッダーテキスト描画
            var align = e.Header?.TextAlign ?? HorizontalAlignment.Left;
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            flags |= align switch
            {
                HorizontalAlignment.Right  => TextFormatFlags.Right,
                HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter,
                _                          => TextFormatFlags.Left
            };

            var textRect = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 12), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", e.Font ?? _listView.Font, textRect, headerFg, flags);
        }

        private void OnListViewDrawItem(object? sender, DrawListViewItemEventArgs e)
        {
            // サブアイテム描画に委託
        }

        private void OnListViewDrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
        {
            e.DrawDefault = true;
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
                    "NVML (NVIDIA Management Library) 診断情報",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        // ----------------------------------------------------------------
        // Initialization
        // ----------------------------------------------------------------

        private void OnLoad(object? sender, EventArgs e)
        {
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
                _gpuNameLabel.Text = "GPU が見つかりません";
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
                    _bannerLabel.Text = "⚠️ nvml.dll が見つかりません (DXGIフォールバック動作中 - クリックで解決手順を表示)";
                    _bannerPanel.Visible = true;
                }
                else if (_nvmlStatus == NvmlStatus.DriverNotLoaded)
                {
                    _bannerLabel.Text = "⚠️ NVIDIA ドライバが読み込まれていません (DXGIフォールバック動作中 - クリックで詳細)";
                    _bannerPanel.Visible = true;
                }
                else if (_nvmlStatus != NvmlStatus.Ready)
                {
                    _bannerLabel.Text = "⚠️ NVML 初期化エラー (DXGIフォールバック動作中 - クリックで詳細)";
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

                // 5. システム行も含めて全体を使用量 (TotalBytes) 降順でソート
                rows.Sort((a, b) => b.TotalBytes.CompareTo(a.TotalBytes));

                // 6. ヘッダーおよびリストの差分更新
                UpdateHeader(rows, totalDedicated, maxDedicated, isNvmlActive);
                UpdateListViewRows(rows);

                string updatedText = $"最終更新: {DateTime.Now:HH:mm:ss}";
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

        private void UpdateListViewRows(List<ProcessRow> rows)
        {
            Color systemColor = _isDarkMode ? ThemeManager.Dark.SystemRowText : ThemeManager.Light.SystemRowText;
            Color defaultColor = _isDarkMode ? ThemeManager.Dark.ListText : ThemeManager.Light.ListText;

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
                    ? "システム/カーネル (ドライバ・その他)"
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
                shared = $"  共有: {sharedUsedGb:0.00} GB / {sharedTotalGb:0.00} GB";
            }

            string sourceTag = isNvmlActive ? "  ※ NVML" : "";
            string headerText = maxDedicatedBytes > 0
                ? $"専用: {usedGb:0.00} GB / {totalGb:0.00} GB ({pct:0}%){shared}{sourceTag}"
                : "GPU メモリ情報を取得できません";

            if (_totalLabel.Text != headerText)
                _totalLabel.Text = headerText;

            _progressBar.Value = maxDedicatedBytes > 0 ? FormatHelper.ClampPercent(pct) : 0;
        }

        // ----------------------------------------------------------------
        // Cleanup
        // ----------------------------------------------------------------

        private void OnFormClosing(object? sender, FormClosingEventArgs e)
        {
            _timer.Stop();
            if (_nvmlReady)
            {
                try { Nvml.Shutdown(); }
                catch { }
            }
        }
    }
}
