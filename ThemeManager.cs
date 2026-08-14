using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VramMonitor
{
    public enum AppTheme
    {
        Auto,
        Dark,
        Light
    }

    public static class ThemeManager
    {
        // --- Win32 / DWM / UxTheme Interop ---
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("uxtheme.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        public static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string? pszSubIdList);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        /// <summary>
        /// Windows 10/11 のタイトルバーにダークモードを設定する
        /// </summary>
        public static void SetWindowDarkMode(IntPtr handle, bool isDark)
        {
            if (handle == IntPtr.Zero) return;
            try
            {
                int val = isDark ? 1 : 0;
                if (DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref val, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref val, sizeof(int));
                }
            }
            catch { }
        }

        /// <summary>
        /// OS のアプリテーマがダークモードかどうかをレジストリから取得する
        /// </summary>
        public static bool IsSystemDarkMode()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var value = key?.GetValue("AppsUseLightTheme");
                if (value is int lightTheme)
                {
                    return lightTheme == 0;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// 指定されたテーマモードとOS設定から実際に適用すべきダークモード状態を解決する
        /// </summary>
        public static bool ResolveIsDark(AppTheme theme)
        {
            return theme switch
            {
                AppTheme.Dark => true,
                AppTheme.Light => false,
                _ => IsSystemDarkMode()
            };
        }

        // --- カラーパレット定義 ---
        public static class Dark
        {
            public static readonly Color WindowBg       = Color.FromArgb(30, 30, 30);
            public static readonly Color HeaderBg       = Color.FromArgb(37, 37, 38);
            public static readonly Color ControlBg      = Color.FromArgb(45, 45, 48);
            public static readonly Color ControlBorder  = Color.FromArgb(62, 62, 66);
            public static readonly Color TextPrimary    = Color.FromArgb(240, 240, 240);
            public static readonly Color TextSecondary  = Color.FromArgb(170, 170, 170);
            public static readonly Color TextDisabled   = Color.FromArgb(110, 110, 110);
            public static readonly Color ListBg         = Color.FromArgb(28, 28, 28);
            public static readonly Color ListText       = Color.FromArgb(230, 230, 230);
            public static readonly Color SystemRowText  = Color.FromArgb(130, 130, 130);
            public static readonly Color ProgressTrack  = Color.FromArgb(50, 50, 50);
            public static readonly Color ProgressFill   = Color.FromArgb(118, 185, 0); // NVIDIA Green
            public static readonly Color BannerBg       = Color.FromArgb(60, 48, 0);
            public static readonly Color BannerText     = Color.FromArgb(255, 224, 130);
        }

        public static class Light
        {
            public static readonly Color WindowBg       = SystemColors.Control;
            public static readonly Color HeaderBg       = SystemColors.Control;
            public static readonly Color ControlBg      = SystemColors.Window;
            public static readonly Color ControlBorder  = SystemColors.ControlDark;
            public static readonly Color TextPrimary    = SystemColors.ControlText;
            public static readonly Color TextSecondary  = Color.Gray;
            public static readonly Color TextDisabled   = SystemColors.GrayText;
            public static readonly Color ListBg         = SystemColors.Window;
            public static readonly Color ListText       = SystemColors.WindowText;
            public static readonly Color SystemRowText  = Color.Gray;
            public static readonly Color ProgressTrack  = Color.FromArgb(230, 230, 230);
            public static readonly Color ProgressFill   = Color.FromArgb(76, 175, 80);
            public static readonly Color BannerBg       = Color.FromArgb(255, 243, 205);
            public static readonly Color BannerText     = Color.FromArgb(133, 100, 4);
        }
    }
}
