using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace VramMonitor.Native
{
    /// <summary>GPU アダプター情報を保持するクラス。</summary>
    public sealed class AdapterInfo
    {
        public string Name     { get; init; } = "";
        public uint   LuidLow  { get; init; }
        public int    LuidHigh { get; init; }
        public uint   VendorId { get; init; }
        public bool   IsNvidia { get; init; }
        public bool   IsAmd    { get; init; }
        public bool   IsIntel  { get; init; }
        public bool   IsIntegrated { get; init; }
        public bool   IsAlpha  => !(IsNvidia || (IsAmd && IsIntegrated));
        public ulong  DedicatedVideoMemory  { get; init; }
        public ulong  DedicatedSystemMemory { get; init; }
        public ulong  SharedSystemMemory    { get; init; }

        /// <summary>
        /// パフォーマンスカウンターのインスタンス名に実際に含まれる LUID 部分文字列。
        /// GetAllAdapters() 内でクロスマッチングにより検証済みの文字列。
        /// 例: "luid_0x00000000_0x0001A123"
        /// </summary>
        internal string? VerifiedLuidPart { get; init; }

        /// <summary>CollectProcesses でのフィルタリングに使用する文字列。</summary>
        public string LuidFilter =>
            VerifiedLuidPart ?? $"luid_0x{LuidHigh:X8}_0x{LuidLow:X8}";

        public override string ToString()
        {
            if (IsAlpha)
            {
                string tag = IsIntegrated ? "[Alpha: iGPU]" : "[Alpha]";
                return $"{Name} {tag}";
            }
            return Name;
        }
    }

    /// <summary>DXGI QueryVideoMemoryInfo の結果。</summary>
    public readonly struct VideoMemSegment
    {
        /// <summary>ドライバが許可する最大使用量 (bytes)。</summary>
        public ulong Budget       { get; init; }
        /// <summary>現在の使用量 (bytes)。</summary>
        public ulong CurrentUsage { get; init; }
    }

    /// <summary>
    /// DXGI COM P/Invoke ラッパー。
    ///
    /// アダプター一覧取得時に Windows パフォーマンスカウンター "GPU Process Memory" の
    /// インスタンス名から実際の LUID 文字列を抽出し、DXGI の LUID 値と照合して
    /// 確実なフィルター文字列を AdapterInfo に格納する。
    /// </summary>
    internal static class DxgiHelper
    {
        // --- GUIDs ---
        private static Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
        private static Guid IID_IDXGIAdapter3  = new("645967A4-1392-4310-A798-8053CE3E93FD");

        [DllImport("dxgi.dll", PreserveSig = true)]
        private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr ppFactory);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int  QueryInterfaceDel(IntPtr self, ref Guid riid, out IntPtr ppvObject);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int  EnumAdapters1Del(IntPtr self, uint index, out IntPtr ppAdapter);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int  GetDescDel(IntPtr self, IntPtr pDescBuffer);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int  QueryVideoMemInfoDel(IntPtr self, uint nodeIndex, uint segmentGroup, IntPtr pInfo);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint ReleaseDel(IntPtr self);

        private const uint NvidiaVendorId    = 0x10DE;
        private const uint AmdVendorId       = 0x1002;
        private const uint AmdVendorIdAlt    = 0x1022;
        private const uint IntelVendorId     = 0x8086;
        private const uint IntelVendorIdAlt  = 0x8087;

        private const int DescSize       = 304;
        private const int VendorIdOffset = 256;
        private const int LuidLowOffset  = 296;
        private const int LuidHighOffset = 300;
        private const int QvmiSize       = 32;

        // ----------------------------------------------------------------
        // Public API
        // ----------------------------------------------------------------

        /// <summary>
        /// システム上の全 GPU アダプター一覧を返す。
        /// パフォーマンスカウンターの実際のインスタンス名と照合して
        /// 正確な LUID フィルター文字列を設定する。
        /// </summary>
        public static List<AdapterInfo> GetAllAdapters()
        {
            var rawList = GetRawDxgiAdapters();
            var perfLuids = GetUniquePerfCounterLuidParts();

            var result = new List<AdapterInfo>();
            foreach (var raw in rawList)
            {
                string hl = $"luid_0x{raw.LuidHigh:X8}_0x{raw.LuidLow:X8}";
                string lh = $"luid_0x{raw.LuidLow:X8}_0x{(uint)raw.LuidHigh:X8}";

                string? verified = null;
                foreach (var pl in perfLuids)
                {
                    if (pl.Equals(hl, StringComparison.OrdinalIgnoreCase)) { verified = hl; break; }
                    if (pl.Equals(lh, StringComparison.OrdinalIgnoreCase)) { verified = lh; break; }
                }

                result.Add(new AdapterInfo
                {
                    Name                  = raw.Name,
                    LuidLow               = raw.LuidLow,
                    LuidHigh              = raw.LuidHigh,
                    VendorId              = raw.VendorId,
                    IsNvidia              = raw.IsNvidia,
                    IsAmd                 = raw.IsAmd,
                    IsIntel               = raw.IsIntel,
                    IsIntegrated          = raw.IsIntegrated,
                    DedicatedVideoMemory  = raw.DedicatedVideoMemory,
                    DedicatedSystemMemory = raw.DedicatedSystemMemory,
                    SharedSystemMemory    = raw.SharedSystemMemory,
                    VerifiedLuidPart      = verified,
                });
            }

            return result;
        }

        /// <summary>
        /// 指定 LUID のアダプターの GPU メモリ情報を返す。
        /// segmentGroup: 0 = Local (専用 VRAM), 1 = Non-Local (共有)
        /// </summary>
        public static VideoMemSegment? QueryVideoMemory(uint luidLow, int luidHigh, uint segmentGroup)
        {
            VideoMemSegment? result = null;
            try
            {
                WithFactory(factory =>
                {
                    IntPtr descBuf = Marshal.AllocHGlobal(DescSize);
                    IntPtr qvmiBuf = Marshal.AllocHGlobal(QvmiSize);
                    try
                    {
                        ForEachAdapter(factory, adapter =>
                        {
                            if (result.HasValue) return;

                            ZeroBuffer(descBuf, DescSize);
                            var getFn = GetVtblFn<GetDescDel>(Marshal.ReadIntPtr(adapter), 8);
                            if (getFn(adapter, descBuf) != 0) return;

                            uint rLow  = (uint)Marshal.ReadInt32(descBuf, LuidLowOffset);
                            int  rHigh = Marshal.ReadInt32(descBuf, LuidHighOffset);
                            if (rLow != luidLow || rHigh != luidHigh) return;

                            var qiIid = IID_IDXGIAdapter3;
                            var qiFn  = GetVtblFn<QueryInterfaceDel>(Marshal.ReadIntPtr(adapter), 0);
                            if (qiFn(adapter, ref qiIid, out IntPtr adapter3) != 0 || adapter3 == IntPtr.Zero)
                                return;

                            try
                            {
                                ZeroBuffer(qvmiBuf, QvmiSize);
                                var qvmiFn = GetVtblFn<QueryVideoMemInfoDel>(Marshal.ReadIntPtr(adapter3), 14);
                                if (qvmiFn(adapter3, 0, segmentGroup, qvmiBuf) == 0)
                                {
                                    result = new VideoMemSegment
                                    {
                                        Budget       = (ulong)Marshal.ReadInt64(qvmiBuf, 0),
                                        CurrentUsage = (ulong)Marshal.ReadInt64(qvmiBuf, 8),
                                    };
                                }
                            }
                            finally { VtblRelease(adapter3); }
                        });
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(descBuf);
                        Marshal.FreeHGlobal(qvmiBuf);
                    }
                });
            }
            catch { }
            return result;
        }

        // ----------------------------------------------------------------
        // Internal helpers
        // ----------------------------------------------------------------

        private const int DedicatedVideoMemoryOffset  = 272;
        private const int DedicatedSystemMemoryOffset = 280;
        private const int SharedSystemMemoryOffset    = 288;

        private sealed class RawAdapter
        {
            public string Name                  { get; init; } = "";
            public uint   LuidLow               { get; init; }
            public int    LuidHigh              { get; init; }
            public uint   VendorId              { get; init; }
            public bool   IsNvidia              { get; init; }
            public bool   IsAmd                 { get; init; }
            public bool   IsIntel               { get; init; }
            public bool   IsIntegrated          { get; init; }
            public ulong  DedicatedVideoMemory  { get; init; }
            public ulong  DedicatedSystemMemory { get; init; }
            public ulong  SharedSystemMemory    { get; init; }
        }

        private static List<RawAdapter> GetRawDxgiAdapters()
        {
            var list = new List<RawAdapter>();
            try
            {
                WithFactory(factory =>
                {
                    IntPtr descBuf = Marshal.AllocHGlobal(DescSize);
                    try
                    {
                        ForEachAdapter(factory, adapter =>
                        {
                            ZeroBuffer(descBuf, DescSize);
                            var getFn = GetVtblFn<GetDescDel>(Marshal.ReadIntPtr(adapter), 8);
                            if (getFn(adapter, descBuf) != 0) return;

                            uint vendorId = (uint)Marshal.ReadInt32(descBuf, VendorIdOffset);

                            if (vendorId == 0x1414) return; // Microsoft Basic Render Driver / WARP

                            uint luidLow  = (uint)Marshal.ReadInt32(descBuf, LuidLowOffset);
                            int  luidHigh = Marshal.ReadInt32(descBuf, LuidHighOffset);
                            string name   = Marshal.PtrToStringUni(descBuf) ?? "(Unknown)";

                            ulong dedicatedVideo = (ulong)Marshal.ReadInt64(descBuf, DedicatedVideoMemoryOffset);
                            ulong dedicatedSys   = (ulong)Marshal.ReadInt64(descBuf, DedicatedSystemMemoryOffset);
                            ulong sharedSys      = (ulong)Marshal.ReadInt64(descBuf, SharedSystemMemoryOffset);

                            bool isNvidia = vendorId == NvidiaVendorId;
                            bool isAmd    = vendorId == AmdVendorId || vendorId == AmdVendorIdAlt;
                            bool isIntel  = vendorId == IntelVendorId || vendorId == IntelVendorIdAlt;

                            // iGPU判定:
                            // 1. 専用VRAMが512MB以下かつ共有メモリが専用VRAMより大きい
                            // 2. AMD/Intelで、専用VRAMが2GB以下かつ共有メモリが専用VRAMの2倍以上あり、名前にdGPU明確名称(RX 6/7/8xxx等)が含まれない場合
                            bool isIntegrated = dedicatedVideo <= 512UL * 1024 * 1024 && sharedSys > dedicatedVideo;
                            if (!isIntegrated && (isAmd || isIntel) && dedicatedVideo <= 2048UL * 1024 * 1024 && sharedSys >= dedicatedVideo * 2)
                            {
                                string lowerName = name.ToLowerInvariant();
                                if (lowerName.Contains("graphics") || lowerName.Contains("vega") || lowerName.Contains("uhd") || lowerName.Contains("iris"))
                                {
                                    isIntegrated = true;
                                }
                            }

                            list.Add(new RawAdapter
                            {
                                Name                  = name,
                                LuidLow               = luidLow,
                                LuidHigh              = luidHigh,
                                VendorId              = vendorId,
                                IsNvidia              = isNvidia,
                                IsAmd                 = isAmd,
                                IsIntel               = isIntel,
                                IsIntegrated          = isIntegrated,
                                DedicatedVideoMemory  = dedicatedVideo,
                                DedicatedSystemMemory = dedicatedSys,
                                SharedSystemMemory    = sharedSys,
                            });
                        });
                    }
                    finally { Marshal.FreeHGlobal(descBuf); }
                });
            }
            catch { }
            return list;
        }

        private static HashSet<string> GetUniquePerfCounterLuidParts()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var cat = new System.Diagnostics.PerformanceCounterCategory("GPU Process Memory");
                foreach (var inst in cat.GetInstanceNames())
                {
                    string? part = ExtractLuidPart(inst);
                    if (part != null && part.Length > 5)
                        set.Add(part);
                }
            }
            catch { }
            return set;
        }

        private static string? ExtractLuidPart(string instance)
        {
            int luidStart = instance.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
            if (luidStart < 0) return null;

            int physStart = instance.IndexOf("_phys_", luidStart, StringComparison.OrdinalIgnoreCase);
            return physStart >= 0
                ? instance.Substring(luidStart, physStart - luidStart)
                : instance.Substring(luidStart);
        }

        // ----------------------------------------------------------------
        // COM infrastructure
        // ----------------------------------------------------------------

        private static void WithFactory(Action<IntPtr> action)
        {
            var iid = IID_IDXGIFactory1;
            if (CreateDXGIFactory1(ref iid, out IntPtr factory) != 0 || factory == IntPtr.Zero)
                return;
            try { action(factory); }
            finally { VtblRelease(factory); }
        }

        private static void ForEachAdapter(IntPtr factory, Action<IntPtr> action)
        {
            var enumFn = GetVtblFn<EnumAdapters1Del>(Marshal.ReadIntPtr(factory), 12);
            for (uint idx = 0; ; idx++)
            {
                if (enumFn(factory, idx, out IntPtr adapter) != 0 || adapter == IntPtr.Zero)
                    break;
                try { action(adapter); }
                finally { VtblRelease(adapter); }
            }
        }

        private static T GetVtblFn<T>(IntPtr vtable, int slot) where T : Delegate
            => Marshal.GetDelegateForFunctionPointer<T>(
                Marshal.ReadIntPtr(vtable, slot * IntPtr.Size));

        private static void VtblRelease(IntPtr obj)
        {
            if (obj == IntPtr.Zero) return;
            try
            {
                var fn = GetVtblFn<ReleaseDel>(Marshal.ReadIntPtr(obj), 2);
                fn(obj);
            }
            catch { }
        }

        private static void ZeroBuffer(IntPtr buf, int size)
        {
            int words = size / IntPtr.Size;
            for (int i = 0; i < words; i++)
                Marshal.WriteIntPtr(buf + i * IntPtr.Size, IntPtr.Zero);
        }
    }
}
