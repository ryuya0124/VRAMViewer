using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace VramMonitor.Services
{
    public class LanguagePack
    {
        public string CultureCode { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public Dictionary<string, string> Strings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string FilePath { get; set; } = "";
    }

    public static class I18n
    {
        public const string AutoLanguageCode = "auto";
        public static event Action? LanguageChanged;

        private static string _selectedLanguageCode = AutoLanguageCode;
        private static readonly List<LanguagePack> _availableLanguages = new();
        private static LanguagePack? _activePack;

        public static string LanguagesDirectory =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Languages");

        public static string SelectedLanguageCode
        {
            get => _selectedLanguageCode;
            set
            {
                if (_selectedLanguageCode != value)
                {
                    _selectedLanguageCode = value;
                    ResolveActiveLanguage();
                    LanguageChanged?.Invoke();
                }
            }
        }

        public static IReadOnlyList<LanguagePack> AvailableLanguages => _availableLanguages;

        public static LanguagePack? ActivePack => _activePack;

        static I18n()
        {
            ReloadLanguages();
        }

        public static void ReloadLanguages()
        {
            _availableLanguages.Clear();

            try
            {
                if (!Directory.Exists(LanguagesDirectory))
                {
                    Directory.CreateDirectory(LanguagesDirectory);
                }

                var files = Directory.GetFiles(LanguagesDirectory, "*.json");
                foreach (var file in files)
                {
                    string fileName = Path.GetFileName(file);
                    // template.json や _ から始まるファイルは言語リストから除外
                    if (fileName.StartsWith("_", StringComparison.OrdinalIgnoreCase) ||
                        fileName.Contains("template", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    try
                    {
                        string json = File.ReadAllText(file);
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var pack = JsonSerializer.Deserialize<LanguagePack>(json, options);

                        if (pack != null && !string.IsNullOrWhiteSpace(pack.CultureCode))
                        {
                            pack.FilePath = file;
                            if (string.IsNullOrWhiteSpace(pack.DisplayName))
                            {
                                pack.DisplayName = pack.CultureCode;
                            }
                            _availableLanguages.Add(pack);
                        }
                    }
                    catch { /* 不正なJSONはスキップ */ }
                }
            }
            catch { }

            // 言語ファイルが1つも読み込めなかった場合の組み込みフォールバック
            if (_availableLanguages.Count == 0)
            {
                _availableLanguages.Add(CreateDefaultJaPack());
                _availableLanguages.Add(CreateDefaultEnPack());
            }

            ResolveActiveLanguage();
        }

        public static void OpenLanguagesFolder()
        {
            try
            {
                if (!Directory.Exists(LanguagesDirectory))
                {
                    Directory.CreateDirectory(LanguagesDirectory);
                }
                Process.Start(new ProcessStartInfo
                {
                    FileName = LanguagesDirectory,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch { }
        }

        private static void ResolveActiveLanguage()
        {
            if (_selectedLanguageCode == AutoLanguageCode)
            {
                var culture = CultureInfo.CurrentUICulture;
                string twoLetter = culture.TwoLetterISOLanguageName;
                string fullName = culture.Name;

                _activePack = _availableLanguages.FirstOrDefault(p =>
                    p.CultureCode.Equals(fullName, StringComparison.OrdinalIgnoreCase) ||
                    p.CultureCode.Equals(twoLetter, StringComparison.OrdinalIgnoreCase));

                if (_activePack == null)
                {
                    _activePack = _availableLanguages.FirstOrDefault(p =>
                        p.CultureCode.Equals("en", StringComparison.OrdinalIgnoreCase))
                        ?? _availableLanguages.FirstOrDefault();
                }
            }
            else
            {
                _activePack = _availableLanguages.FirstOrDefault(p =>
                    p.CultureCode.Equals(_selectedLanguageCode, StringComparison.OrdinalIgnoreCase))
                    ?? _availableLanguages.FirstOrDefault();
            }
        }

        public static string T(string key, params object[] args)
        {
            string? text = null;

            // 1. アクティブ言語パックから取得
            if (_activePack != null && _activePack.Strings.TryGetValue(key, out var val) && !string.IsNullOrEmpty(val))
            {
                text = val;
            }

            // 2. 英語 (en) パックからフォールバック
            if (string.IsNullOrEmpty(text))
            {
                var enPack = _availableLanguages.FirstOrDefault(p => p.CultureCode.Equals("en", StringComparison.OrdinalIgnoreCase));
                if (enPack != null && enPack.Strings.TryGetValue(key, out var enVal) && !string.IsNullOrEmpty(enVal))
                {
                    text = enVal;
                }
            }

            // 3. 組み込みデフォルト辞書からフォールバック
            if (string.IsNullOrEmpty(text))
            {
                if (BuiltInFallback.TryGetValue(key, out var fbVal))
                {
                    text = fbVal;
                }
                else
                {
                    text = key;
                }
            }

            if (args != null && args.Length > 0)
            {
                try
                {
                    return string.Format(text, args);
                }
                catch
                {
                    return text;
                }
            }

            return text;
        }

        private static LanguagePack CreateDefaultJaPack() => new()
        {
            CultureCode = "ja",
            DisplayName = "🇯🇵 日本語 (Japanese)",
            Strings = BuiltInJaStrings
        };

        private static LanguagePack CreateDefaultEnPack() => new()
        {
            CultureCode = "en",
            DisplayName = "🇺🇸 English",
            Strings = BuiltInFallback
        };

        private static readonly Dictionary<string, string> BuiltInFallback = new(StringComparer.OrdinalIgnoreCase)
        {
            ["AppTitle"] = "VRAM Monitor",
            ["Initializing"] = "Initializing...",
            ["GpuNotFound"] = "No GPU found",
            ["NoGpuMemoryInfo"] = "Unable to retrieve GPU memory information",
            ["LastUpdated"] = "Last updated: {0}",
            ["MenuFile"] = "&File",
            ["MenuRefreshNow"] = "&Refresh Now",
            ["MenuExit"] = "E&xit",
            ["MenuView"] = "&View",
            ["MenuTheme"] = "&Theme",
            ["ThemeAuto"] = "💻 Auto",
            ["ThemeDark"] = "🌙 Dark",
            ["ThemeLight"] = "☀️ Light",
            ["MenuAlwaysOnTop"] = "&Always on Top",
            ["MenuSettings"] = "&Settings",
            ["MenuRefreshInterval"] = "Update &Interval",
            ["Interval500ms"] = "0.5 sec (500 ms)",
            ["Interval1000ms"] = "1.0 sec (1000 ms)",
            ["Interval1500ms"] = "1.5 sec (1500 ms - Default)",
            ["Interval2000ms"] = "2.0 sec (2000 ms)",
            ["Interval3000ms"] = "3.0 sec (3000 ms)",
            ["Interval5000ms"] = "5.0 sec (5000 ms)",
            ["MenuLanguage"] = "&Language",
            ["LangAuto"] = "💻 Auto Detect",
            ["MenuOpenLanguagesFolder"] = "📁 Open Languages Folder...",
            ["MenuHelp"] = "&Help",
            ["MenuNvmlDiag"] = "NVML &Diagnostics...",
            ["MenuAbout"] = "&About VRAM Monitor...",
            ["ColPid"] = "PID",
            ["ColProcessName"] = "Process Name",
            ["ColDedicatedVram"] = "Dedicated VRAM",
            ["ColSharedVram"] = "Shared VRAM",
            ["SystemKernelProcess"] = "System / Kernel (Driver & Others)",
            ["HeaderDedicated"] = "Dedicated",
            ["HeaderShared"] = "Shared",
            ["BannerNvmlDllNotFound"] = "⚠️ nvml.dll not found (DXGI fallback active - Click for instructions)",
            ["BannerNvmlDriverNotLoaded"] = "⚠️ NVIDIA driver not loaded (DXGI fallback active - Click for details)",
            ["BannerNvmlError"] = "⚠️ NVML initialization error (DXGI fallback active - Click for details)",
            ["BannerAlphaSupport"] = "ℹ️ Experimental Support (Alpha): {0} - Monitoring via DXGI (Click for details)",
            ["NvmlDiagDialogTitle"] = "NVML (NVIDIA Management Library) Diagnostics",
            ["AlphaDiagDialogTitle"] = "GPU Experimental Support (Alpha)",
            ["AlphaDiagDialogMessage"] = "Detected GPU: {0}\n\nThis environment is operating under Alpha (Experimental) support.\nGPU memory usage is monitored and displayed using Windows DXGI and Performance Counters.",
            ["AboutDialogTitle"] = "About VRAM Monitor",
            ["AboutDialogMessage"] = "VRAM Monitor v1.0.0\n\nA real-time GPU VRAM monitoring utility using DirectX (DXGI) and NVIDIA Management Library (NVML).\n\n• Real-time VRAM usage tracking\n• Per-process VRAM breakdown\n• Dark & Light theme support\n• JSON-based multi-language localization"
        };

        private static readonly Dictionary<string, string> BuiltInJaStrings = new(StringComparer.OrdinalIgnoreCase)
        {
            ["AppTitle"] = "VRAM Monitor",
            ["Initializing"] = "初期化中...",
            ["GpuNotFound"] = "GPU が見つかりません",
            ["NoGpuMemoryInfo"] = "GPU メモリ情報を取得できません",
            ["LastUpdated"] = "最終更新: {0}",
            ["MenuFile"] = "ファイル(&F)",
            ["MenuRefreshNow"] = "今すぐ更新(&R)",
            ["MenuExit"] = "終了(&X)",
            ["MenuView"] = "表示(&V)",
            ["MenuTheme"] = "テーマ(&T)",
            ["ThemeAuto"] = "💻 自動",
            ["ThemeDark"] = "🌙 ダーク",
            ["ThemeLight"] = "☀️ ライト",
            ["MenuAlwaysOnTop"] = "常に最前面に表示(&A)",
            ["MenuSettings"] = "設定(&S)",
            ["MenuRefreshInterval"] = "更新頻度(&I)",
            ["Interval500ms"] = "0.5 秒 (500 ms)",
            ["Interval1000ms"] = "1.0 秒 (1000 ms)",
            ["Interval1500ms"] = "1.5 秒 (1500 ms - デフォルト)",
            ["Interval2000ms"] = "2.0 秒 (2000 ms)",
            ["Interval3000ms"] = "3.0 秒 (3000 ms)",
            ["Interval5000ms"] = "5.0 秒 (5000 ms)",
            ["MenuLanguage"] = "言語 (Language)(&L)",
            ["LangAuto"] = "💻 自動検出 (Auto)",
            ["MenuOpenLanguagesFolder"] = "📁 言語フォルダを開く...",
            ["MenuHelp"] = "ヘルプ(&H)",
            ["MenuNvmlDiag"] = "NVML 診断情報(&D)...",
            ["MenuAbout"] = "バージョン情報(&A)...",
            ["ColPid"] = "PID",
            ["ColProcessName"] = "プロセス名",
            ["ColDedicatedVram"] = "専用 VRAM",
            ["ColSharedVram"] = "共有 VRAM",
            ["SystemKernelProcess"] = "システム/カーネル (ドライバ・その他)",
            ["HeaderDedicated"] = "専用",
            ["HeaderShared"] = "共有",
            ["BannerNvmlDllNotFound"] = "⚠️ nvml.dll が見つかりません (DXGIフォールバック動作中 - クリックで解決手順を表示)",
            ["BannerNvmlDriverNotLoaded"] = "⚠️ NVIDIA ドライバが読み込まれていません (DXGIフォールバック動作中 - クリックで詳細)",
            ["BannerNvmlError"] = "⚠️ NVML 初期化エラー (DXGIフォールバック動作中 - クリックで詳細)",
            ["BannerAlphaSupport"] = "ℹ️ 実験的サポート (Alpha): {0} - DXGI 経由で監視中 (クリックで詳細)",
            ["NvmlDiagDialogTitle"] = "NVML (NVIDIA Management Library) 診断情報",
            ["AlphaDiagDialogTitle"] = "GPU 実験的サポート (Alpha)",
            ["AlphaDiagDialogMessage"] = "検出された GPU: {0}\n\n本環境は Alpha 版（実験的対応）として動作しています。\nWindows DXGI およびパフォーマンスカウンターを使用して GPU メモリ使用状況を取得・表示しています。",
            ["AboutDialogTitle"] = "VRAM Monitor について",
            ["AboutDialogMessage"] = "VRAM Monitor v1.0.0\n\nDirectX (DXGI) および NVIDIA Management Library (NVML) を使用した GPU VRAM 監視ツールです。\n\n・リアルタイム VRAM 使用量監視\n・プロセス別 VRAM 内訳表示\n・ダーク / ライトテーマ対応\n・JSONファイルによる多言語対応"
        };
    }
}
