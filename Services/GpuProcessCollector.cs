using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using VramMonitor.Models;
using VramMonitor.Native;

namespace VramMonitor.Services
{
    public sealed class GpuProcessCollector
    {
        public ProcessIconService IconService { get; } = new();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        public void ClearCache()
        {
            IconService.ClearCache();
        }

        public Image GetProcessIcon(uint pid, bool isSystem)
        {
            return IconService.GetProcessIcon(pid, isSystem);
        }

        public string GetProcessName(uint pid)
        {
            if (pid == 0) return "System Idle";
            if (pid == 4) return "System (NT Kernel)";

            string name;
            try
            {
                using var p = Process.GetProcessById((int)pid);
                name = p.ProcessName;
            }
            catch
            {
                name = GetProcessNameLowLevel(pid);
            }

            return name;
        }

        private static string GetProcessNameLowLevel(uint pid)
        {
            IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (int)pid);
            if (handle != IntPtr.Zero)
            {
                try
                {
                    var sb = new StringBuilder(1024);
                    int size = sb.Capacity;
                    if (QueryFullProcessImageName(handle, 0, sb, ref size))
                    {
                        return Path.GetFileNameWithoutExtension(sb.ToString());
                    }
                }
                finally
                {
                    CloseHandle(handle);
                }
            }
            return $"(PID {pid})";
        }

        /// <summary>
        /// Windows パフォーマンスカウンター "GPU Process Memory" から
        /// プロセスごとの専用 (Local) / 共有 (Shared / Non-Local) VRAM 使用量を取得する。
        /// </summary>
        public static List<ProcessRow> CollectProcesses(AdapterInfo? adapter)
            => CollectSnapshot(adapter).Processes;

        public sealed record MemorySnapshot(List<ProcessRow> Processes, ulong? DedicatedBytes, ulong? SharedBytes);

        public static MemorySnapshot CollectSnapshot(AdapterInfo? adapter)
        {
            var pidLocal    = new Dictionary<uint, ulong>();
            var pidNonLocal = new Dictionary<uint, ulong>();

            // adapter.LuidFilter は perf counter の実データとクロスマッチ済みの文字列
            string? luidFilter = adapter?.LuidFilter;

            try
            {
                var category = new PerformanceCounterCategory("GPU Process Memory");
                // One category read gives a consistent snapshot and avoids opening
                // several native counter handles for every process on every tick.
                var counters = category.ReadCategory();
                var local = GetCounter(counters, "Local Usage") ?? GetCounter(counters, "Dedicated Usage");
                var shared = GetCounter(counters, "Shared Usage") ?? GetCounter(counters, "Non Local Usage");
                var instances = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (local != null) foreach (string name in local.Keys) instances.Add(name);
                if (shared != null) foreach (string name in shared.Keys) instances.Add(name);

                foreach (var inst in instances)
                {
                    // LUID フィルタリング
                    if (luidFilter != null &&
                        !inst.Contains(luidFilter, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!TryParsePid(inst, out uint pid))
                        continue;

                    long lv = local != null && local.Contains(inst) ? local[inst].RawValue : 0;
                    long nlv = shared != null && shared.Contains(inst) ? shared[inst].RawValue : 0;

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
            ulong? dedicatedBytes = null, sharedBytes = null;
            try
            {
                var counters = new PerformanceCounterCategory("GPU Adapter Memory").ReadCategory();
                dedicatedBytes = SumAdapterCounter(GetCounter(counters, "Dedicated Usage"), luidFilter);
                sharedBytes = SumAdapterCounter(GetCounter(counters, "Shared Usage"), luidFilter);
            }
            catch { }
            return new MemorySnapshot(rows, dedicatedBytes, sharedBytes);
        }

        private static InstanceDataCollection? GetCounter(InstanceDataCollectionCollection counters, string name)
            => counters.Contains(name) ? counters[name] : null;

        private static ulong? SumAdapterCounter(InstanceDataCollection? counter, string? luidFilter)
        {
            if (counter == null) return null;
            ulong total = 0;
            bool found = false;
            foreach (InstanceData instance in counter.Values)
            {
                if (luidFilter != null && !instance.InstanceName.Contains(luidFilter, StringComparison.OrdinalIgnoreCase)) continue;
                found = true;
                if (instance.RawValue > 0) total += (ulong)instance.RawValue;
            }
            return found ? total : null;
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
    }
}
