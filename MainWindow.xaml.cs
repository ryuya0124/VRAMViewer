using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using VramMonitor.Models;
using VramMonitor.Native;
using VramMonitor.Services;
using Windows.Storage.Streams;

namespace VramMonitor;

public sealed partial class MainWindow : Window
{
    private readonly ObservableCollection<ProcessDisplayRow> _rows = new();
    private readonly GpuProcessCollector _collector = new();
    private readonly Dictionary<string, ImageSource> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherQueueTimer _timer;
    private AdapterInfo? _adapter;
    private NvmlStatus _nvmlStatus;
    private string _nvmlMessage = "";
    private IntPtr _device;
    private bool _refreshing, _closed, _disposed, _dialogOpen;
    private int _generation, _sortColumn = 2;
    private bool _descending = true;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(WindowTitleBar);
        Root.ActualThemeChanged += (_, _) => UpdateCaptionColors();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 850));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.PreferredMinimumWidth = 820;
        ProcessList.ItemsSource = _rows;
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(1500);
        _timer.Tick += async (_, _) => await RefreshAsync();
        I18n.LanguageChanged += LanguageChanged;
        ApplyLocalization();
        Root.Loaded += Loaded;
        Closed += (_, _) =>
        {
            _closed = true;
            _timer.Stop();
            I18n.LanguageChanged -= LanguageChanged;
            if (!_refreshing) DisposeServices();
        };
    }

    private async void Loaded(object sender, RoutedEventArgs e)
    {
        Root.Loaded -= Loaded;
        UpdateCaptionColors();
        double scale = Root.XamlRoot.RasterizationScale;
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(Math.Min(workArea.Width, (int)(1024 * scale)), Math.Min(workArea.Height, (int)(800 * scale))));
        try
        {
            var adapters = await Task.Run(DxgiHelper.GetAllAdapters);
            var init = await Task.Run(Nvml.TryInitialize);
            _nvmlStatus = init.Status;
            _nvmlMessage = init.Message;
            if (_closed) { if (init.Status == NvmlStatus.Ready) Nvml.Shutdown(); return; }
            GpuSelector.ItemsSource = adapters;
            GpuSelector.SelectedItem = adapters.FirstOrDefault(a => a.IsNvidia) ?? adapters.FirstOrDefault();
            if (adapters.Count == 0) ShowStatus(I18n.T("GpuNotFound"), InfoBarSeverity.Warning);
            _timer.Start();
        }
        catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); }
    }

    private void LanguageChanged()
    {
        ApplyLocalization();
        _generation++;
        _ = RefreshAsync();
    }

    private void UpdateCaptionColors()
    {
        bool dark = Root.ActualTheme == ElementTheme.Dark;
        AppWindow.TitleBar.ButtonForegroundColor = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        AppWindow.TitleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 128, 128, 128);
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
    }

    private static string MenuText(string key) => System.Text.RegularExpressions.Regex.Replace(I18n.T(key).Replace("&", ""), @"\([A-Z]\)", "");

    private void ApplyLocalization()
    {
        Title = I18n.T("AppTitle");
        Subtitle.Text = I18n.T("MonitorSubtitle");
        RefreshText.Text = MenuText("MenuRefreshNow");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(RefreshButton, RefreshText.Text);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SettingsButton, MenuText("MenuSettings"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(GpuSelector, I18n.T("SelectGpu"));
        ThemeMenu.Text = MenuText("MenuTheme");
        ThemeAuto.Text = I18n.T("ThemeAuto"); ThemeLight.Text = I18n.T("ThemeLight"); ThemeDark.Text = I18n.T("ThemeDark");
        IntervalMenu.Text = MenuText("MenuRefreshInterval");
        LanguageMenu.Text = MenuText("MenuLanguage");
        LanguageMenu.Items.Clear();
        var auto = new MenuFlyoutItem { Text = I18n.T("LangAuto") };
        auto.Click += (_, _) => I18n.SelectedLanguageCode = I18n.AutoLanguageCode;
        LanguageMenu.Items.Add(auto);
        foreach (var pack in I18n.AvailableLanguages)
        {
            string code = pack.CultureCode;
            var item = new MenuFlyoutItem { Text = pack.DisplayName };
            item.Click += (_, _) => I18n.SelectedLanguageCode = code;
            LanguageMenu.Items.Add(item);
        }
        var openLanguages = new MenuFlyoutItem { Text = MenuText("MenuOpenLanguagesFolder") };
        openLanguages.Click += (_, _) => { I18n.OpenLanguagesFolder(); I18n.ReloadLanguages(); ApplyLocalization(); };
        LanguageMenu.Items.Add(openLanguages);
        AlwaysOnTopMenu.Text = MenuText("MenuAlwaysOnTop");
        DiagnosticsMenu.Text = MenuText("MenuNvmlDiag");
        AboutMenu.Text = MenuText("MenuAbout");
        DedicatedTitle.Text = I18n.T("HeaderDedicated"); SharedTitle.Text = I18n.T("HeaderShared");
        UpdateSortHeaders();
    }

    private async void Gpu_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _adapter = GpuSelector.SelectedItem as AdapterInfo;
        _device = _adapter?.IsNvidia == true && _nvmlStatus == NvmlStatus.Ready
            ? Nvml.GetDeviceHandleForAdapter(_adapter) : IntPtr.Zero;
        _generation++;
        _rows.Clear();
        DedicatedValue.Text = SharedValue.Text = "—";
        await RefreshAsync();
    }

    private sealed record Entry(ProcessRow Memory, string Name, string? Path, DateTime? StartUtc, byte[]? Icon);
    private sealed record Sample(GpuProcessCollector.MemorySnapshot Memory, List<Entry> Entries, Nvml.NvmlMemory? NvidiaMemory);

    private Sample Capture(AdapterInfo adapter, IntPtr device, HashSet<string> knownIcons, string systemName)
    {
        var snapshot = GpuProcessCollector.CollectSnapshot(adapter);
        Nvml.NvmlMemory? nvidia = null;
        if (device != IntPtr.Zero && Nvml.DeviceGetMemoryInfo(device, out var memory) == Nvml.NvmlReturn.Success)
            nvidia = memory;
        var summary = GpuMemorySummary.Create(snapshot, adapter, nvidia);
        if (summary.Unattributed > 0) snapshot.Processes.Add(new ProcessRow(0, summary.Unattributed, 0, true));
        var entries = new List<Entry>();
        foreach (var row in snapshot.Processes)
        {
            string name = row.IsSystem ? systemName : _collector.GetProcessName(row.Pid);
            string? path = row.IsSystem ? null : _collector.IconService.GetProcessExecutablePath(row.Pid);
            DateTime? start = null;
            if (!row.IsSystem)
            {
                try { using var process = Process.GetProcessById((int)row.Pid); start = process.StartTime.ToUniversalTime(); }
                catch { }
            }
            string iconKey = row.IsSystem ? "SYSTEM" : path ?? "DEFAULT";
            byte[]? png = null;
            if (knownIcons.Add(iconKey))
            {
                var icon = _collector.GetProcessIcon(row.Pid, row.IsSystem);
                using var stream = new MemoryStream();
                icon.Save(stream, ImageFormat.Png);
                png = stream.ToArray();
            }
            entries.Add(new Entry(row, name, path, start, png));
        }
        return new Sample(snapshot, entries, nvidia);
    }

    private async Task RefreshAsync()
    {
        if (_refreshing || _closed || _adapter == null) return;
        _refreshing = true;
        int generation = _generation;
        var adapter = _adapter;
        var device = _device;
        try
        {
            var known = new HashSet<string>(_icons.Keys, StringComparer.OrdinalIgnoreCase);
            string systemName = I18n.T("SystemKernelProcess");
            var sample = await Task.Run(() => Capture(adapter, device, known, systemName));
            if (_closed || generation != _generation) return;
            if (_icons.Count > 256) _icons.Clear();
            foreach (var entry in sample.Entries.Where(entry => entry.Icon != null))
            {
                var image = new BitmapImage();
                using var stream = new InMemoryRandomAccessStream();
                await stream.WriteAsync(entry.Icon!.AsBuffer());
                stream.Seek(0);
                await image.SetSourceAsync(stream);
                _icons[entry.Memory.IsSystem ? "SYSTEM" : entry.Path ?? "DEFAULT"] = image;
            }
            if (_closed || generation != _generation) return;
            UpdateRows(sample.Entries);
            var summary = GpuMemorySummary.Create(sample.Memory, adapter, sample.NvidiaMemory);
            DedicatedValue.Text = $"{FormatHelper.FormatVram(summary.DedicatedUsed)} / {FormatHelper.FormatVram(summary.DedicatedCapacity)}";
            SharedValue.Text = $"{FormatHelper.FormatVram(summary.SharedUsed)} / {FormatHelper.FormatVram(adapter.SharedSystemMemory)}";
            MemoryProgress.Value = summary.DedicatedCapacity > 0 ? Math.Min(100, 100.0 * summary.DedicatedUsed / summary.DedicatedCapacity) : 0;
            SourceLabel.Text = I18n.T(sample.NvidiaMemory.HasValue ? "SourceNvml" : sample.Memory.DedicatedBytes.HasValue ? "SourceWindows" : "ProcessSumEstimate");
            if (adapter.IsAlpha) SourceLabel.Text += " · " + I18n.T("ExperimentalGpu");
            UpdatedLabel.Text = I18n.T("LastUpdated", DateTime.Now.ToString("HH:mm:ss"));
        }
        catch (Exception ex) { if (!_closed) ShowStatus(ex.Message, InfoBarSeverity.Error); }
        finally
        {
            _refreshing = false;
            if (_closed) DisposeServices();
            else if (generation != _generation) _ = RefreshAsync();
        }
    }

    private void UpdateRows(List<Entry> entries)
    {
        entries.Sort((a, b) =>
        {
            int cmp = _sortColumn switch
            {
                0 => a.Memory.Pid.CompareTo(b.Memory.Pid),
                1 => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase),
                3 => a.Memory.NonLocalBytes.CompareTo(b.Memory.NonLocalBytes),
                _ => a.Memory.LocalBytes.CompareTo(b.Memory.LocalBytes)
            };
            return cmp == 0 ? a.Memory.Pid.CompareTo(b.Memory.Pid) : _descending ? -cmp : cmp;
        });
        var existing = _rows.ToDictionary(row => row.Key);
        var active = entries.Select(entry => entry.Memory.IsSystem ? "SYSTEM" : entry.Memory.Pid.ToString()).ToHashSet();
        for (int i = _rows.Count - 1; i >= 0; i--) if (!active.Contains(_rows[i].Key)) _rows.RemoveAt(i);
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            string key = entry.Memory.IsSystem ? "SYSTEM" : entry.Memory.Pid.ToString();
            if (!existing.TryGetValue(key, out var row))
            {
                row = new() { Pid = entry.Memory.Pid, IsSystem = entry.Memory.IsSystem };
                _rows.Insert(i, row);
            }
            else { int index = _rows.IndexOf(row); if (index != i) _rows.Move(index, i); }
            row.Update(entry.Name, entry.Path, entry.StartUtc, entry.Memory.LocalBytes, entry.Memory.NonLocalBytes);
            if (_icons.TryGetValue(entry.Memory.IsSystem ? "SYSTEM" : entry.Path ?? "DEFAULT", out var icon)) row.SetIcon(icon);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void Theme_Click(object sender, RoutedEventArgs e) => Root.RequestedTheme = Enum.Parse<ElementTheme>((string)((MenuFlyoutItem)sender).Tag);
    private void Interval_Click(object sender, RoutedEventArgs e) => _timer.Interval = TimeSpan.FromMilliseconds(int.Parse((string)((MenuFlyoutItem)sender).Tag));
    private void AlwaysOnTop_Click(object sender, RoutedEventArgs e)
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.IsAlwaysOnTop = AlwaysOnTopMenu.IsChecked;
    }
    private void Sort_Click(object sender, RoutedEventArgs e)
    {
        int column = int.Parse((string)((Button)sender).Tag);
        _descending = column == _sortColumn ? !_descending : column >= 2;
        _sortColumn = column; UpdateSortHeaders(); _ = RefreshAsync();
    }
    private void UpdateSortHeaders()
    {
        var buttons = new[] { PidColumn, NameColumn, DedicatedColumn, SharedColumn };
        var keys = new[] { "ColPid", "ColProcessName", "ColDedicatedVram", "ColSharedVram" };
        for (int i = 0; i < buttons.Length; i++) buttons[i].Content = I18n.T(keys[i]) + (i == _sortColumn ? _descending ? " ↓" : " ↑" : "");
    }

    private void ProcessList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var element = e.OriginalSource as DependencyObject;
        while (element is FrameworkElement framework && framework.DataContext is not ProcessDisplayRow) element = VisualTreeHelper.GetParent(element);
        if (element is not FrameworkElement { DataContext: ProcessDisplayRow row }) return;
        ProcessList.SelectedItem = row;
        var menu = new MenuFlyout();
        void Add(string key, Action action)
        {
            var item = new MenuFlyoutItem { Text = MenuText(key), IsEnabled = !row.IsSystem && row.Pid > 4 };
            item.Click += (_, _) => action(); menu.Items.Add(item);
        }
        Add("MenuEndTask", () => _ = EndTaskAsync(row));
        Add("MenuOpenFileLocation", () => OpenFile(row, false));
        Add("MenuProperties", () => OpenFile(row, true));
        menu.ShowAt(ProcessList, new FlyoutShowOptions { Position = e.GetPosition(ProcessList) });
        e.Handled = true;
    }

    private async Task EndTaskAsync(ProcessDisplayRow row)
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot, Title = I18n.T("EndTaskConfirmTitle"),
                Content = I18n.T("EndTaskConfirmMessage", row.Name, row.Pid),
                PrimaryButtonText = MenuText("MenuEndTask"), CloseButtonText = I18n.T("Cancel"), DefaultButton = ContentDialogButton.Close
            };
            // Keep identity from before the confirmation while periodic refreshes continue.
            DateTime? expectedStart = row.StartTimeUtc;
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            using var process = Process.GetProcessById((int)row.Pid);
            if (!expectedStart.HasValue || process.StartTime.ToUniversalTime() != expectedStart.Value)
                throw new InvalidOperationException(I18n.T("ProcessChanged"));
            process.Kill();
            await RefreshAsync();
        }
        catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); }
        finally { _dialogOpen = false; }
    }

    private void OpenFile(ProcessDisplayRow row, bool properties)
    {
        try
        {
            if (row.ExePath == null || !File.Exists(row.ExePath)) throw new FileNotFoundException(I18n.T("FileNotFoundMessage"));
            if (properties) ShellHelper.ShowFileProperties(row.ExePath);
            else Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), $"/select,\"{row.ExePath}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); }
    }

    private async void Diagnostics_Click(object sender, RoutedEventArgs e) => await ShowDialogAsync(I18n.T("NvmlDiagDialogTitle"), $"NVML: {_nvmlStatus}\n{_nvmlMessage}\n\n{I18n.T("SourceWindows")}");
    private async void About_Click(object sender, RoutedEventArgs e) => await ShowDialogAsync(I18n.T("AboutDialogTitle"), I18n.T("AboutWinUI"));
    private async Task ShowDialogAsync(string title, string content)
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        try { await new ContentDialog { XamlRoot = Root.XamlRoot, Title = title, Content = content, CloseButtonText = I18n.T("Close") }.ShowAsync(); }
        finally { _dialogOpen = false; }
    }
    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message; StatusBar.Severity = severity; StatusBar.IsOpen = true;
    }
    private void DisposeServices()
    {
        if (_disposed) return;
        _disposed = true;
        _collector.IconService.Dispose();
        if (_nvmlStatus == NvmlStatus.Ready) Nvml.Shutdown();
    }
}
