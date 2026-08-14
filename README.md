# VRAM Monitor (C# / WinForms)

Windows上でプロセスごとのGPUメモリ（専用VRAM / 共有メモリ）使用量をリアルタイムに可視化・監視するデスクトップアプリです。

> [!NOTE]
> **※ 個人利用（自分用）ツールです。**
> 動作確認は **AMD Ryzen 7 9800X3D + NVIDIA GeForce RTX 5080 (Windows 11)** の環境でのみ行っています。他の構成での完全な動作は保証していません。

---

## 主な機能

- **正確なGPU総メモリ使用量表示**:
  - NVIDIA GPU選択時は **NVML (NVIDIA Management Library)** を直接参照し、タスクマネージャーのGPUパフォーマンス表示と一致する総VRAM使用量を表示
- **プロセス別 VRAM 内訳表示**:
  - Windows パフォーマンスカウンター（`GPU Process Memory`）から、各プロセスの**専用VRAM (Local)** および **共有メモリ (Non-Local)** を取得・リアルタイム集計
  - アダプターごとの **LUID 照合** により、Ryzen内蔵iGPUとGeForceディスクリートGPUのメモリ使用量を分離
- **システム / カーネル領域の可視化**:
  - NVMLの総使用量とプロセス別使用量の合計との差分を「システム/カーネル（ドライバ等）」として算出・表示
- **マルチGPU対応**:
  - 接続されている GPU（NVIDIA / AMD / Intel）をドロップダウンから切り替え可能
- **メニューバー & 設定**:
  - Windowsアプリ標準のメニューバー（ファイル / 表示 / 設定 / ヘルプ）
  - **更新頻度設定**: 0.5秒 / 1秒 / 1.5秒 / 2秒 / 3秒 / 5秒 から選択可能
  - **多言語対応 (JSON拡張)**:
    - OS言語（日本語 / 英語など）の自動検出 ＆ 手動切替
    - `Languages/` フォルダに JSON ファイルを追加するだけで新しい言語を自動検知して利用可能
    - `Languages/template.json` のひな形ファイルを用意
- **ダークモード対応**:
  - Windows 10/11 のテーマ自動検出・動的追従 ＆ 手動切り替え（💻 自動 / 🌙 ダーク / ☀️ ライト）
- **単一EXEビルド対応**:
  - **ランタイム非同梱版**（超軽量 / 約200 KB）
  - **ランタイム同梱版**（スタンドアロン / 約68 MB / .NET未インストールPCでも動作）

---

## 仕組みと設計

1. **GPU総使用量（ヘッダー）**:
   - NVIDIA GPU: `nvml.dll` の `nvmlDeviceGetMemoryInfo` から正確な物理VRAM使用量を取得
   - iGPU / その他のGPU: DXGI (`IDXGIAdapter3`) およびパフォーマンスカウンターのプロセス集計値を使用
2. **プロセス別内訳（一覧）**:
   - `\GPU Process Memory(*)\Local Usage`（専用VRAM）および `Shared Usage`（共有）カウンターを集計
   - DXGIアダプターのLUIDとパフォーマンスカウンターのインスタンス名をマッチングし、対象GPUのプロセスのみをフィルタリング
3. **ドライバ・システム領域**:
   - NVIDIA環境では、NVMLで得られる総使用量からプロセス別合計を引いた差分を「システム/カーネル」行として表示

---

## 動作環境

- **検証済み環境**: **AMD Ryzen 7 9800X3D + NVIDIA GeForce RTX 5080 (Windows 11 x64)**
- **対応OS**: Windows 10 (1809以降) / Windows 11 (x64)
- **ランタイム**:
  - **ランタイム非同梱版 exe**: [.NET 8 デスクトップランタイム](https://dotnet.microsoft.com/download/dotnet/8.0) が必要
  - **ランタイム同梱版 exe**: ランタイムインストール不要（単体で動作）
  - **ソースコードからビルドする場合**: .NET 8 SDK が必要
- **ドライバ**: NVIDIA ディスプレイドライバ（NVML連携用。なくてもDXGIフォールバックで動作）

---

## ビルド方法

用途に合わせて、3種類の方法でビルド・実行できます。

### 1. ランタイム非同梱版（軽量 単一EXE / 約200 KB）★おすすめ
実行環境に [.NET 8 デスクトップランタイム](https://dotnet.microsoft.com/download/dotnet/8.0) が入っているPC向けの超軽量な単一exeです。

- **バッチファイル（ダブルクリックで実行）**:
  - `build-light.bat`
- **PowerShell**:
  ```powershell
  .\build.ps1 -FrameworkDependent -OutputDir publish_light
  ```
- **CLI コマンド**:
  ```powershell
  dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o publish_light
  ```
- **生成ファイル**: `publish_light\VramMonitor.exe`

---

### 2. ランタイム同梱版（スタンドアロン 単一EXE / 約68 MB）
.NET 8 ランタイムがインストールされていないPCでも、この単一exeファイルのみでそのまま動作します。

- **バッチファイル（ダブルクリックで実行）**:
  - `build.bat`
- **PowerShell**:
  ```powershell
  .\build.ps1
  ```
- **CLI コマンド**:
  ```powershell
  dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o publish
  ```
- **生成ファイル**: `publish\VramMonitor.exe`

---

### 3. 開発用ビルド・デバッグ実行

```powershell
# ビルド
dotnet build -c Release

# 実行
dotnet run -c Release
```
（生成先: `bin\Release\net8.0-windows\VramMonitor.exe`）

---

## 画面の見方

- **ヘッダー**:
  - GPU名とテーマ切り替えボタン（💻 自動 / 🌙 ダーク / ☀️ ライト）
  - GPU選択ドロップダウン
  - 専用 / 共有 VRAM 使用量（GB・%）
  - VRAM 使用率ゲージ（プログレスバー）
- **プロセス一覧**:
  - PID、プロセス名、専用 VRAM、共有 VRAM
  - システム/カーネル行: NVML総使用量とプロセス合計の差分（ドライバ・DWM等）
- **1.5秒ごとに自動更新**

---

## トラブルシューティング

- **「⚠️ nvml.dll が見つかりません」と出る場合**:
  NVIDIAドライバが未インストールまたは標準パスに見つからない場合です。DXGIフォールバックモードで動作します。
- **「⚠️ NVML 初期化エラー」と出る場合**:
  最新の GeForce / Studio ドライバに更新してください。
