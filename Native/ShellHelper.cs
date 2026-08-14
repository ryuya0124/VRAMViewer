using System;
using System.Runtime.InteropServices;

namespace VramMonitor.Native
{
    public static class ShellHelper
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct SHELLEXECUTEINFO
        {
            public int cbSize;
            public uint fMask;
            public IntPtr hwnd;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpVerb;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpFile;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string? lpParameters;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string? lpDirectory;
            public int nShow;
            public IntPtr hInstApp;
            public IntPtr lpIDList;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string? lpClass;
            public IntPtr hkeyClass;
            public uint dwHotKey;
            public IntPtr hIcon;
            public IntPtr hProcess;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

        public const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;

        /// <summary>
        /// 指定されたファイルの Windows 標準プロパティダイアログを表示する。
        /// </summary>
        public static bool ShowFileProperties(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;

            var info = new SHELLEXECUTEINFO
            {
                cbSize = Marshal.SizeOf(typeof(SHELLEXECUTEINFO)),
                lpVerb = "properties",
                lpFile = filePath,
                nShow  = 5, // SW_SHOW
                fMask  = SEE_MASK_INVOKEIDLIST,
            };

            return ShellExecuteEx(ref info);
        }
    }
}
