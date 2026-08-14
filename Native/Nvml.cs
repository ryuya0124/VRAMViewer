using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace VramMonitor.Native
{
    /// <summary>
    /// NVML の初期化状態および診断情報
    /// </summary>
    public enum NvmlStatus
    {
        NotAttempted,
        Ready,
        DllNotFound,
        DriverNotLoaded,
        InitializationFailed,
        DeviceNotFound,
    }

    /// <summary>
    /// NVIDIA Management Library (NVML) の薄いP/Invokeラッパー。
    ///
    /// タスクマネージャーの「専用GPUメモリ」表示は、Windowsの
    /// GPU Process Memory パフォーマンスカウンター経由の集計値ではなく、
    /// このNVMLをベースにしたドライバの内部情報を参照している。
    /// そのため、ここで取得する数値はTask Managerと一致する（同一情報源）。
    ///
    /// 参考: nvml.dll は NVIDIA ドライバのインストール時に
    ///   C:\Program Files\NVIDIA Corporation\NVSMI\nvml.dll
    ///   または C:\Windows\System32\nvml.dll
    /// に配置されます。本クラスでは自動探索とフォールバック解決を行います。
    /// </summary>
    internal static class Nvml
    {
        private const string DllName = "nvml.dll";
        private const CallingConvention Convention = CallingConvention.Cdecl;

        static Nvml()
        {
            try
            {
                NativeLibrary.SetDllImportResolver(typeof(Nvml).Assembly, DllImportResolver);
            }
            catch
            {
                // リゾルバ設定に失敗した場合はデフォルトの P/Invoke 検索に任せる
            }
        }

        private static IntPtr DllImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (libraryName.Equals(DllName, StringComparison.OrdinalIgnoreCase) ||
                libraryName.Equals("nvml", StringComparison.OrdinalIgnoreCase))
            {
                // 1. 標準検索 (System32 / PATH)
                if (NativeLibrary.TryLoad("nvml.dll", out IntPtr handle))
                    return handle;

                // 2. NVSMI フォルダ (標準インストール先)
                string nvsmiPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "NVIDIA Corporation", "NVSMI", "nvml.dll");
                if (File.Exists(nvsmiPath) && NativeLibrary.TryLoad(nvsmiPath, out handle))
                    return handle;

                // 3. System32 明示パス
                string sys32Path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "nvml.dll");
                if (File.Exists(sys32Path) && NativeLibrary.TryLoad(sys32Path, out handle))
                    return handle;
            }
            return IntPtr.Zero;
        }

        public enum NvmlReturn : int
        {
            Success = 0,
            ErrorUninitialized = 1,
            ErrorInvalidArgument = 2,
            ErrorNotSupported = 3,
            ErrorNoPermission = 4,
            ErrorAlreadyInitialized = 5,
            ErrorNotFound = 6,
            ErrorInsufficientSize = 7,
            ErrorInsufficientPower = 8,
            ErrorDriverNotLoaded = 9,
            ErrorTimeout = 10,
            ErrorUnknown = 999,
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NvmlMemory
        {
            public ulong Total;
            public ulong Free;
            public ulong Used;
        }

        // nvmlProcessInfo_v1_t: { uint pid; unsigned long long usedGpuMemory; }
        // x64では pid(4B)+padding(4B)+usedGpuMemory(8B) = 16B。
        // Pack=8 を明示してネイティブ側と確実に一致させる。
        // v3 API では usedGpuMemory が取得不可の場合 ULLONG_MAX を返すため
        // 呼び出し側でそのセンチネル値を 0 に置き換える。
        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        public struct NvmlProcessInfo
        {
            public uint Pid;
            public ulong UsedGpuMemory;
            public uint GpuInstanceId;
            public uint ComputeInstanceId;

            /// <summary>ULLONG_MAX はメモリ使用量が取得不可を示す NVML のセンチネル値。</summary>
            public ulong UsedGpuMemorySafe =>
                UsedGpuMemory == ulong.MaxValue ? 0UL : UsedGpuMemory;
        }

        [DllImport(DllName, CallingConvention = Convention, EntryPoint = "nvmlInit_v2")]
        public static extern NvmlReturn Init();

        [DllImport(DllName, CallingConvention = Convention, EntryPoint = "nvmlShutdown")]
        public static extern NvmlReturn Shutdown();

        [DllImport(DllName, CallingConvention = Convention, EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
        public static extern NvmlReturn DeviceGetHandleByIndex(uint index, out IntPtr device);

        [DllImport(DllName, CallingConvention = Convention, EntryPoint = "nvmlDeviceGetCount_v2")]
        public static extern NvmlReturn DeviceGetCount(out uint count);

        [DllImport(DllName, CallingConvention = Convention, EntryPoint = "nvmlDeviceGetName", CharSet = CharSet.Ansi)]
        public static extern NvmlReturn DeviceGetName(IntPtr device, StringBuilder name, uint length);

        [DllImport(DllName, CallingConvention = Convention, EntryPoint = "nvmlDeviceGetMemoryInfo")]
        public static extern NvmlReturn DeviceGetMemoryInfo(IntPtr device, out NvmlMemory memory);

        [DllImport(DllName, CallingConvention = Convention, EntryPoint = "nvmlDeviceGetComputeRunningProcesses_v3")]
        public static extern NvmlReturn DeviceGetComputeRunningProcesses(
            IntPtr device, ref uint infoCount, [In, Out] NvmlProcessInfo[]? infos);

        [DllImport(DllName, CallingConvention = Convention, EntryPoint = "nvmlDeviceGetGraphicsRunningProcesses_v3")]
        public static extern NvmlReturn DeviceGetGraphicsRunningProcesses(
            IntPtr device, ref uint infoCount, [In, Out] NvmlProcessInfo[]? infos);

        public delegate NvmlReturn RunningProcessesFunc(IntPtr device, ref uint infoCount, NvmlProcessInfo[]? infos);

        public static NvmlProcessInfo[] GetRunningProcesses(RunningProcessesFunc func, IntPtr device)
        {
            uint count = 0;
            NvmlReturn result = func(device, ref count, null);

            if (result == NvmlReturn.Success)
            {
                return Array.Empty<NvmlProcessInfo>();
            }

            if (result != NvmlReturn.ErrorInsufficientSize)
            {
                throw new NvmlException("プロセス一覧の件数取得に失敗しました", result);
            }

            for (int attempt = 0; attempt < 3; attempt++)
            {
                uint capacity = count + 8;
                var infos = new NvmlProcessInfo[capacity];
                uint actualCount = capacity;

                result = func(device, ref actualCount, infos);

                if (result == NvmlReturn.Success)
                {
                    Array.Resize(ref infos, (int)actualCount);
                    return infos;
                }

                if (result == NvmlReturn.ErrorInsufficientSize)
                {
                    count = actualCount;
                    continue;
                }

                throw new NvmlException("プロセス一覧の取得に失敗しました", result);
            }

            return Array.Empty<NvmlProcessInfo>();
        }

        /// <summary>
        /// NVML の初期化を安全に試み、結果ステータスと詳細メッセージを返す。
        /// </summary>
        public static (NvmlStatus Status, string Message, IntPtr Device) TryInitialize()
        {
            try
            {
                var ret = Init();
                if (ret == NvmlReturn.ErrorDriverNotLoaded)
                {
                    return (NvmlStatus.DriverNotLoaded,
                        "NVIDIA ドライバが読み込まれていません。ドライバが正しくインストールされているか確認してください。",
                        IntPtr.Zero);
                }
                if (ret != NvmlReturn.Success && ret != NvmlReturn.ErrorAlreadyInitialized)
                {
                    return (NvmlStatus.InitializationFailed,
                        $"NVML の初期化に失敗しました (エラー: {ret})。NVIDIA ドライバを最新版に更新してください。",
                        IntPtr.Zero);
                }

                var devRet = DeviceGetHandleByIndex(0, out IntPtr device);
                if (devRet != NvmlReturn.Success || device == IntPtr.Zero)
                {
                    return (NvmlStatus.DeviceNotFound,
                        $"NVIDIA GPU デバイスハンドルの取得に失敗しました (エラー: {devRet})。",
                        IntPtr.Zero);
                }

                return (NvmlStatus.Ready, "NVML 初期化完了", device);
            }
            catch (DllNotFoundException)
            {
                return (NvmlStatus.DllNotFound,
                    "nvml.dll が見つかりませんでした。\n" +
                    "NVIDIA GeForce/Quadro ドライバが正しくインストールされているか確認してください。\n" +
                    "(通常 C:\\Program Files\\NVIDIA Corporation\\NVSMI または C:\\Windows\\System32 に配置されます)",
                    IntPtr.Zero);
            }
            catch (Exception ex)
            {
                return (NvmlStatus.InitializationFailed,
                    $"NVML 初期化中に予期せぬ例外が発生しました: {ex.Message}",
                    IntPtr.Zero);
            }
        }
    }

    internal sealed class NvmlException : Exception
    {
        public Nvml.NvmlReturn ErrorCode { get; }

        public NvmlException(string message, Nvml.NvmlReturn errorCode)
            : base($"{message} (NVML error: {errorCode})")
        {
            ErrorCode = errorCode;
        }
    }
}
