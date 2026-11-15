using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace VramMonitor.Services
{
    /// <summary>
    /// プロセスの実行ファイルからアイコンを抽出し、メモリ上にキャッシュするサービス。
    /// </summary>
    public sealed class ProcessIconService : IDisposable
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(
            string pszPath,
            uint dwFileAttributes,
            ref SHFILEINFO psfi,
            uint cbFileInfo,
            uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint SHGFI_ICON              = 0x000000100;
        private const uint SHGFI_SMALLICON         = 0x000000001;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
        private const uint FILE_ATTRIBUTE_NORMAL   = 0x00000080;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        private readonly Dictionary<string, Image> _pathIconCache = new(StringComparer.OrdinalIgnoreCase);

        private Image? _defaultExeIcon;
        private Image? _systemIcon;

        public void ClearCache()
        {
            foreach (var img in _pathIconCache.Values.Distinct())
                if (!ReferenceEquals(img, _defaultExeIcon)) img.Dispose();
            _pathIconCache.Clear();
        }

        public Image GetDefaultExeIcon()
        {
            if (_defaultExeIcon != null) return _defaultExeIcon;

            try
            {
                var shinfo = new SHFILEINFO();
                IntPtr res = SHGetFileInfo(
                    ".exe",
                    FILE_ATTRIBUTE_NORMAL,
                    ref shinfo,
                    (uint)Marshal.SizeOf(shinfo),
                    SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);

                if (res != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
                {
                    using var ico = Icon.FromHandle(shinfo.hIcon);
                    _defaultExeIcon = ico.ToBitmap();
                    DestroyIcon(shinfo.hIcon);
                }
            }
            catch { }

            _defaultExeIcon ??= SystemIcons.Application.ToBitmap();
            return _defaultExeIcon;
        }

        public Image GetSystemIcon()
        {
            if (_systemIcon != null) return _systemIcon;

            try
            {
                string shell32 = Path.Combine(Environment.SystemDirectory, "shell32.dll");
                if (File.Exists(shell32))
                {
                    var shinfo = new SHFILEINFO();
                    IntPtr res = SHGetFileInfo(
                        shell32,
                        0,
                        ref shinfo,
                        (uint)Marshal.SizeOf(shinfo),
                        SHGFI_ICON | SHGFI_SMALLICON);

                    if (res != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
                    {
                        using var ico = Icon.FromHandle(shinfo.hIcon);
                        _systemIcon = ico.ToBitmap();
                        DestroyIcon(shinfo.hIcon);
                    }
                }
            }
            catch { }

            _systemIcon ??= SystemIcons.WinLogo.ToBitmap();
            return _systemIcon;
        }

        public Image GetProcessIcon(uint pid, bool isSystem)
        {
            if (isSystem || pid == 0 || pid == 4)
            {
                return GetSystemIcon();
            }

            string? exePath = GetProcessExecutablePath(pid);
            Image icon;

            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                icon = GetIconForPath(exePath);
            }
            else
            {
                icon = GetDefaultExeIcon();
            }

            return icon;
        }

        public string? GetProcessExecutablePath(uint pid)
        {
            if (pid == 0 || pid == 4) return null;

            string? path = null;
            IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (int)pid);
            if (handle != IntPtr.Zero)
            {
                try
                {
                    var sb = new StringBuilder(1024);
                    int size = sb.Capacity;
                    if (QueryFullProcessImageName(handle, 0, sb, ref size))
                    {
                        path = sb.ToString();
                    }
                }
                finally
                {
                    CloseHandle(handle);
                }
            }

            if (string.IsNullOrEmpty(path))
            {
                try
                {
                    using var p = Process.GetProcessById((int)pid);
                    path = p.MainModule?.FileName;
                }
                catch { }
            }

            return path;
        }

        private Image GetIconForPath(string path)
        {
            if (_pathIconCache.TryGetValue(path, out var img))
            {
                return img;
            }

            // Paths change as applications update. Bound the GDI image cache.
            if (_pathIconCache.Count >= 256) ClearCache();

            Image? result = null;
            try
            {
                var shinfo = new SHFILEINFO();
                IntPtr res = SHGetFileInfo(
                    path,
                    0,
                    ref shinfo,
                    (uint)Marshal.SizeOf(shinfo),
                    SHGFI_ICON | SHGFI_SMALLICON);

                if (res != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
                {
                    using var ico = Icon.FromHandle(shinfo.hIcon);
                    result = ico.ToBitmap();
                    DestroyIcon(shinfo.hIcon);
                }
            }
            catch { }

            if (result == null)
            {
                try
                {
                    using var ico = Icon.ExtractAssociatedIcon(path);
                    if (ico != null)
                    {
                        using var small = new Icon(ico, 16, 16);
                        result = small.ToBitmap();
                    }
                }
                catch { }
            }

            result ??= GetDefaultExeIcon();
            _pathIconCache[path] = result;
            return result;
        }

        public void Dispose()
        {
            ClearCache();

            _defaultExeIcon?.Dispose();
            _defaultExeIcon = null;

            _systemIcon?.Dispose();
            _systemIcon = null;
        }
    }
}
