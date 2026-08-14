using System;

namespace VramMonitor.Services
{
    public static class FormatHelper
    {
        public static string FormatVram(ulong bytes)
        {
            if (bytes == 0) return "-";
            const double gb = 1024.0 * 1024.0 * 1024.0;
            const double mb = 1024.0 * 1024.0;
            return bytes >= gb
                ? $"{bytes / gb:0.00} GB"
                : $"{bytes / mb:0.0} MB";
        }

        public static int ClampPercent(double value)
        {
            return Math.Max(0, Math.Min(100, (int)Math.Round(value)));
        }
    }
}
