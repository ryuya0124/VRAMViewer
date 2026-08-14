using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace VramMonitor
{
    public sealed class MainForm : Form
    {
        private const int RefreshIntervalMs = 1500;
        private const int WM_SETTINGCHANGE  = 0x001A;
        private const int WM_THEMECHANGED   = 0x031A;

        // --- Controls ---
        private readonly Panel             _headerPanel;
        private readonly Panel             _listPanel;
        private readonly Label             _gpuNameLabel;
        private readonly Button            _themeButton;
        private readonly ComboBox          _gpuSelector;
        private readonly Label             _totalLabel;
        private readonly ModernProgressBar _progressBar;
        private readonly ListView          _listView;
        private readonly Label             _updatedLabel;
        private readonly System.Windows.Forms.Timer _timer;
        private readonly Panel             _bannerPanel;
        private readonly Label             _bannerLabel;

        // --- Theme State ---
        private AppTheme _themeMode = AppTheme.Auto;
        private bool     _isDarkMode;

        // --- State ---
        private IntPtr            _device;
        private bool              _nvmlReady;
        private NvmlStatus        _nvmlStatus = NvmlStatus.NotAttempted;
        private string            _nvmlStatusMessage = "";
        private List<AdapterInfo> _adapters = new();
        private AdapterInfo?      _selectedAdapter;
        private ulong             _lastNvmlUsed;
        private readonly Dictionary<uint, string> _processNameCache = new();

        public MainForm()
        {
            Text            = "VRAM Monitor";
            Width           = 780;
            Height          = 600;
            MinimumSize     = new Size(580, 420);
            StartPosition   = FormStartPosition.CenterScreen;
            Font            = new Font("Segoe UI", 9F);

            // ---- Banner panel (Warning / Status) ----
            _bannerPanel = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 30,
                Padding   = new Padding(12, 6, 12, 6),
                Visible   = false,
                Cursor    = Cursors.Hand,
            };
            _bannerLabel = new Label
            {
                Dock      = DockStyle.Fill,
                Font      = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Text      = "",
                Cursor    = Cursors.Hand,
            };
            _bannerPanel.Controls.Add(_bannerLabel);
            _bannerPanel.Click += OnBannerClick;
            _bannerLabel.Click += OnBannerClick;

            // ---- Header panel ----
            _headerPanel = new Panel
            {
                Dock    = DockStyle.Top,
                Height  = 120,
                Padding = new Padding(12, 10, 12, 6),
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
                ItemHeight    = 20,
                Height        = 26,
                FlatStyle     = FlatStyle.Flat,
            };
            _gpuSelector.DrawItem += OnGpuSelectorDrawItem;

            _totalLabel = new Label
            {
                Text   = "",
                Dock   = DockStyle.Top,
                Height = 22,
            };

            _progressBar = new ModernProgressBar
            {
                Dock    = DockStyle.Top,
                Height  = 18,
                Minimum = 0,
                Maximum = 100,
            };

            // Controls added bottom-to-top (DockStyle.Top stacks in reverse insertion order)
            _headerPanel.Controls.Add(_progressBar);
            _headerPanel.Controls.Add(_totalLabel);
            _headerPanel.Controls.Add(_gpuSelector);
            _headerPanel.Controls.Add(topRowPanel);

            // ---- Process list ----
            _listView = new ListView
            {
                Dock          = DockStyle.Fill,
                View          = View.Details,
                FullRowSelect = true,
                GridLines     = true,
                BorderStyle   = BorderStyle.None,
            };
            _listView.Columns.Add("PID",       70);
            _listView.Columns.Add("プロセス名", 270);
            _listView.Columns.Add("専用 VRAM",  145, HorizontalAlignment.Right);
            _listView.Columns.Add("共有 VRAM",  145, HorizontalAlignment.Right);

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

            // Update Theme button text
            _themeButton.Text = _themeMode switch
            {
                AppTheme.Auto  => "💻 自動",
                AppTheme.Dark  => "🌙 ダーク",
                AppTheme.Light => "☀️ ライト",
                _ => "💻 自動"
            };

            // Window titlebar dark mode (DWM)
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
            // Cycle: Auto -> Dark -> Light -> Auto
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
            using var brushFg = new SolidBrush(fg);

            e.Graphics.FillRectangle(brushBg, e.Bounds);

            var textRect = new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 2, e.Bounds.Width - 8, e.Bounds.Height - 4);
            var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            TextRenderer.DrawText(e.Graphics, itemText, e.Font ?? Font, textRect, fg, flags);

            if ((e.State & DrawItemState.Focus) == DrawItemState.Focus && !isSelected)
            {
                e.DrawFocusRectangle();
            }
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            // OS のダーク/ライトテーマ切り替え通知を検知
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

            // ComboBox のイベントを一時停止してアダプター一覧を投入
            _gpuSelector.SelectedIndexChanged -= OnGpuSelected;

            _adapters = DxgiHelper.GetAllAdapters();
            _gpuSelector.BeginUpdate();
            foreach (var a in _adapters)
                _gpuSelector.Items.Add(a);
            _gpuSelector.EndUpdate();

            _gpuSelector.SelectedIndexChanged += OnGpuSelected;

            // NVML を初期化 (NVIDIA GPU 専用。自動探索リゾルバ付き)
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

            // NVIDIA GPU を優先、なければ先頭を選択 (OnGpuSelected → RefreshData が走る)
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
            _processNameCache.Clear();
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
                // --- プロセス一覧 ---
                var rows = CollectProcesses(_selectedAdapter);

                // --- ヘッダー (GPU 名 + メモリ使用量バー) ---
                UpdateHeader(rows);

                // システム/カーネル行: (NVML の合計使用量) - プロセス合計
                ulong totalUsed = GetTotalUsedBytes();
                if (totalUsed > 0)
                {
                    ulong processSum = 0;
                    foreach (var r in rows) processSum += r.LocalBytes;

                    if (totalUsed > processSum)
                    {
                        ulong sysVram = totalUsed - processSum;
                        if (sysVram > 512 * 1024)                          // 512 KB 未満は誤差として無視
                            rows.Add(new ProcessRow(0, sysVram, 0, isSystem: true));
                    }
                }

                // --- ListView 更新 ---
                _listView.BeginUpdate();
                _listView.Items.Clear();

                Color systemColor = _isDarkMode ? ThemeManager.Dark.SystemRowText : ThemeManager.Light.SystemRowText;

                foreach (var row in rows)
                {
                    string pidText = row.IsSystem ? "-" : row.Pid.ToString();
                    string name    = row.IsSystem
                        ? "システム/カーネル (ドライバ・その他)"
                        : GetProcessName(row.Pid);

                    var item = new ListViewItem(new[]
                    {
                        pidText,
                        name,
                        FormatVram(row.LocalBytes),
                        FormatVram(row.NonLocalBytes),
                    });

                    if (row.IsSystem)
                        item.ForeColor = systemColor;

                    _listView.Items.Add(item);
                }

                _listView.EndUpdate();
                _updatedLabel.Text = $"最終更新: {DateTime.Now:HH:mm:ss}";
            }
            catch (NvmlException ex)
            {
                _totalLabel.Text = ex.Message;
            }
        }

        private void UpdateHeader(List<ProcessRow> rows)
        {
            if (_selectedAdapter == null) return;

            _gpuNameLabel.Text = _selectedAdapter.Name;

            // NVIDIA + NVML が利用可能な場合は NVML を優先 (より正確)
            if (_selectedAdapter.IsNvidia && _nvmlReady)
            {
                var r = Nvml.DeviceGetMemoryInfo(_device, out var mem);
                if (r == Nvml.NvmlReturn.Success)
                {
                    double usedGb  = mem.Used  / 1024.0 / 1024.0 / 1024.0;
                    double totalGb = mem.Total / 1024.0 / 1024.0 / 1024.0;
                    double pct     = mem.Total > 0 ? (double)mem.Used / mem.Total * 100.0 : 0.0;
                    _lastNvmlUsed  = mem.Used;

                    string shared = "";
                    if (_selectedAdapter.SharedSystemMemory > 0)
                    {
                        ulong nvSharedSum = 0;
                        foreach (var row in rows)
                        {
                            if (!row.IsSystem) nvSharedSum += row.NonLocalBytes;
                        }
                        double sharedUsedGb  = nvSharedSum / 1024.0 / 1024.0 / 1024.0;
                        double sharedTotalGb = _selectedAdapter.SharedSystemMemory / 1024.0 / 1024.0 / 1024.0;
                        shared = $"  共有: {sharedUsedGb:0.00} GB / {sharedTotalGb:0.00} GB";
                    }

                    _totalLabel.Text = $"専用: {usedGb:0.00} GB / {totalGb:0.00} GB ({pct:0}%){shared}  ※ NVML";
                    _progressBar.Value = Clamp100(pct);
                    return;
                }
            }

            // それ以外 (iGPU / AMD / Intel 等) → パフォーマンスカウンターのプロセス集計値を使用
            _lastNvmlUsed = 0;

            ulong processLocalSum = 0;
            ulong processNonLocalSum = 0;
            foreach (var row in rows)
            {
                if (!row.IsSystem)
                {
                    processLocalSum += row.LocalBytes;
                    processNonLocalSum += row.NonLocalBytes;
                }
            }

            ulong totalBytes = _selectedAdapter.DedicatedVideoMemory > 0
                ? _selectedAdapter.DedicatedVideoMemory
                : _selectedAdapter.SharedSystemMemory;

            if (totalBytes > 0)
            {
                double usedGb  = processLocalSum / 1024.0 / 1024.0 / 1024.0;
                double totalGb = totalBytes / 1024.0 / 1024.0 / 1024.0;
                double pct     = totalBytes > 0
                    ? (double)processLocalSum / totalBytes * 100.0
                    : 0.0;

                string shared = "";
                if (_selectedAdapter.SharedSystemMemory > 0)
                {
                    double sharedUsedGb  = processNonLocalSum / 1024.0 / 1024.0 / 1024.0;
                    double sharedTotalGb = _selectedAdapter.SharedSystemMemory / 1024.0 / 1024.0 / 1024.0;
                    shared = $"  共有: {sharedUsedGb:0.00} GB / {sharedTotalGb:0.00} GB";
                }

                _totalLabel.Text = $"専用: {usedGb:0.00} GB / {totalGb:0.00} GB ({pct:0}%){shared}";
                _progressBar.Value = Clamp100(pct);
            }
            else
            {
                _totalLabel.Text   = "GPU メモリ情報を取得できません";
                _progressBar.Value = 0;
            }
        }

        /// <summary>システム/カーネル行計算用の合計使用量を返す。</summary>
        private ulong GetTotalUsedBytes()
        {
            if (_selectedAdapter != null && _selectedAdapter.IsNvidia && _nvmlReady && _lastNvmlUsed > 0)
                return _lastNvmlUsed;

            return 0;
        }

        // ----------------------------------------------------------------
        // Process collection
        // ----------------------------------------------------------------

        private readonly struct ProcessRow
        {
            public ProcessRow(uint pid, ulong local, ulong nonLocal, bool isSystem = false)
            {
                Pid           = pid;
                LocalBytes    = local;
                NonLocalBytes = nonLocal;
                IsSystem      = isSystem;
            }

            public uint  Pid           { get; }
            public ulong LocalBytes    { get; }
            public ulong NonLocalBytes { get; }
            public bool  IsSystem      { get; }
            public ulong TotalBytes    => LocalBytes + NonLocalBytes;
        }

        /// <summary>
        /// Windows パフォーマンスカウンター "GPU Process Memory" から
        /// プロセスごとの専用 (Local) / 共有 (Shared / Non-Local) VRAM 使用量を取得する。
        /// </summary>
        private static List<ProcessRow> CollectProcesses(AdapterInfo? adapter)
        {
            var pidLocal    = new Dictionary<uint, ulong>();
            var pidNonLocal = new Dictionary<uint, ulong>();

            // adapter.LuidFilter は perf counter の実データとクロスマッチ済みの文字列
            string? luidFilter = adapter?.LuidFilter;

            try
            {
                var category = new PerformanceCounterCategory("GPU Process Memory");
                string[] instances = category.GetInstanceNames();

                foreach (var inst in instances)
                {
                    // LUID フィルタリング
                    if (luidFilter != null &&
                        !inst.Contains(luidFilter, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!TryParsePid(inst, out uint pid))
                        continue;

                    long lv = 0;
                    long nlv = 0;

                    // 専用 VRAM (Local Usage または Dedicated Usage)
                    try
                    {
                        using var lc = new PerformanceCounter(
                            "GPU Process Memory", "Local Usage", inst, readOnly: true);
                        lv = lc.RawValue;
                    }
                    catch { /* インスタンスが消えた場合など */ }

                    if (lv <= 0)
                    {
                        try
                        {
                            using var dc = new PerformanceCounter(
                                "GPU Process Memory", "Dedicated Usage", inst, readOnly: true);
                            lv = dc.RawValue;
                        }
                        catch { }
                    }

                    // 共有 VRAM (Shared Usage または Non Local Usage)
                    try
                    {
                        using var sc = new PerformanceCounter(
                            "GPU Process Memory", "Shared Usage", inst, readOnly: true);
                        nlv = sc.RawValue;
                    }
                    catch { }

                    if (nlv <= 0)
                    {
                        try
                        {
                            using var nlc = new PerformanceCounter(
                                "GPU Process Memory", "Non Local Usage", inst, readOnly: true);
                            nlv = nlc.RawValue;
                        }
                        catch { }
                    }

                    if (lv > 0)
                    {
                        ulong b = (ulong)lv;
                        pidLocal[pid] = pidLocal.TryGetValue(pid, out var prev) ? prev + b : b;
                    }
                    if (nlv > 0)
                    {
                        ulong b = (ulong)nlv;
                        pidNonLocal[pid] = pidNonLocal.TryGetValue(pid, out var prev) ? prev + b : b;
                    }
                }
            }
            catch { /* カテゴリが存在しない環境 */ }

            // 両辞書のキーを統合してリスト化
            var allPids = new HashSet<uint>(pidLocal.Keys);
            allPids.UnionWith(pidNonLocal.Keys);

            var rows = new List<ProcessRow>(allPids.Count);
            foreach (var pid in allPids)
            {
                pidLocal.TryGetValue(pid,    out var l);
                pidNonLocal.TryGetValue(pid, out var nl);
                rows.Add(new ProcessRow(pid, l, nl));
            }

            // 専用 + 共有の合計でソート
            rows.Sort((a, b) => b.TotalBytes.CompareTo(a.TotalBytes));
            return rows;
        }

        /// <summary>
        /// GPU Process Memory インスタンス名 "pid_PPPP_luid_..." から PID を取り出す。
        /// </summary>
        private static bool TryParsePid(string instance, out uint pid)
        {
            pid = 0;
            if (!instance.StartsWith("pid_", StringComparison.OrdinalIgnoreCase))
                return false;
            var rest = instance.AsSpan(4);
            int sep  = rest.IndexOf('_');
            var span = sep >= 0 ? rest.Slice(0, sep) : rest;
            return uint.TryParse(span, out pid);
        }

        // ----------------------------------------------------------------
        // Utilities
        // ----------------------------------------------------------------

        private static string FormatVram(ulong bytes)
        {
            if (bytes == 0) return "-";
            const double gb = 1024.0 * 1024.0 * 1024.0;
            const double mb = 1024.0 * 1024.0;
            return bytes >= gb
                ? $"{bytes / gb:0.00} GB"
                : $"{bytes / mb:0.0} MB";
        }

        private static int Clamp100(double value)
            => Math.Max(0, Math.Min(100, (int)Math.Round(value)));

        private string GetProcessName(uint pid)
        {
            if (_processNameCache.TryGetValue(pid, out var cached))
                return cached;

            string name;
            try
            {
                using var p = Process.GetProcessById((int)pid);
                name = p.ProcessName;
            }
            catch (ArgumentException)         { name = $"(PID {pid})"; }
            catch (InvalidOperationException) { name = $"(PID {pid})"; }

            _processNameCache[pid] = name;
            return name;
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
