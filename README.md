# VRAM Monitor (C# / WinForms)

タスクマネージャーの「専用GPUメモリ」と**同じ情報源(NVML)**からプロセス別
VRAM使用量を取得するWindowsデスクトップアプリです。

## なぜ一致するのか

Windowsの `\GPU Process Memory(*)\Dedicated Usage` パフォーマンスカウンターは
グラフィックスタック側の集計で、DWM合成の都合上、同じVRAM領域が複数プロセス分
重複計上されることがあります。

本アプリはNVIDIAドライバが直接管理する **NVML (NVIDIA Management Library)**
を P/Invoke 経由で直接叩きます。タスクマネージャーの表示も同じNVMLの情報を
元にしているため、原理的に数値が一致します。NVMLはNVIDIA GPUしか扱わないため、
AMD iGPUとの混同（LUID分離の手間）も発生しません。

## 動作環境

- Windows 10/11 (x64)
- .NET 8 SDK（ビルド時のみ必要。実行だけなら発行済みexeで不要）
- NVIDIAドライバがインストール済みで `nvml.dll` が解決できること
  （通常 `C:\Program Files\NVIDIA Corporation\NVSMI` が自動でPATHに入っています）

## ビルド方法

1. [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) をインストール
2. このフォルダ一式（`VramMonitor.csproj` と `.cs` ファイル）を任意の場所に置く
3. コマンドプロンプト/PowerShellでフォルダに移動し、以下を実行

```powershell
dotnet build -c Release
```

初回はNuGetの復元が走ります（このプロジェクトは外部NuGetパッケージ不要ですが、
SDK自体のワークロード解決のためネットワークが必要です）。

## 実行

```powershell
dotnet run -c Release
```

または、ビルド後に生成される実行ファイルを直接起動:

```
bin\Release\net8.0-windows\VramMonitor.exe
```

## 単一exeのビルド (配布用)

付属のビルドスクリプトを実行すると、単一のスタンドアロン `.exe` ファイルが生成されます（.NET ランタイム同梱のため、他のPCでもそのまま実行可能です）。

- **バッチファイル（ダブルクリックで実行）**:
  - `build.bat` を実行
- **PowerShell**:
  - `.\build.ps1` を実行

出力先:
```
publish\VramMonitor.exe
```

## 画面の見方

- 上部: GPU名、専用GPUメモリ使用量（GB・%）、プログレスバー
  → ここがタスクマネージャーの「専用GPUメモリ」と一致するはずです
- 一覧: プロセスごとのVRAM使用量（MB単位、降順）
  - 種別列: `Compute`(CUDA等) / `Graphics`(描画) / 両方使っていれば`Compute+Graphics`
- 1.5秒ごとに自動更新

## Visual Studioで開く場合

`VramMonitor.csproj` をダブルクリックするか、Visual Studio 2022以降で
「フォルダーを開く」→ このフォルダを選択すればそのまま認識されます。
デバッグ実行はF5でOKです。

## トラブルシューティング

- 起動時に「nvml.dll が見つかりません」と出る場合:
  NVIDIAドライバの再インストール、または
  `C:\Program Files\NVIDIA Corporation\NVSMI` が存在するか確認してください。
- 「NVMLの初期化に失敗しました」と出る場合: ドライバのバージョンが古い可能性があります。
  最新のGeForceドライバに更新してください。
- ビルド時に `net8.0-windows` が見つからないエラーが出る場合: .NET 8 SDKの
  Windows Desktop向けコンポーネントが入っていない可能性があります。SDKインストーラを
  再実行し、"ASP.NET と Web開発" ではなく通常のSDKインストールを選んでください
  （WinFormsは.NET SDK標準に含まれます）。
