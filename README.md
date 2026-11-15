# VRAMViewer — WinUI 3

WindowsのGPUメモリ使用量とプロセス別の内訳を表示する、WinUI 3 / .NET 11のデスクトップアプリです。NVIDIA GeForce RTX 5080とAMD Radeon内蔵GPUを搭載したWindows 11 x64で確認しています。

## 機能

- GPUを選んで専用・共有メモリの使用量を確認
- PID、プロセス名、アプリアイコン、専用・共有メモリを一覧表示
- 列見出しをクリックして並べ替え
- 0.5 / 1 / 1.5 / 2 / 3 / 5秒の更新間隔
- ライト・ダーク・システム連動テーマ、日本語・英語、JSON言語パック
- 最前面表示、NVML診断、プロセスの終了・ファイルの場所・プロパティ

画面右上の設定ボタンから、テーマ、言語、更新間隔などを変更できます。プロセス操作は一覧の右クリックメニューから行います。プロセスの終了には確認画面が表示され、確認中にPIDが再利用された場合は終了を中止します。

## 使用量の意味

NVIDIAの総専用メモリはNVML、その他のGPUの総使用量と共有メモリはWindowsの`GPU Adapter Memory`カウンターから取得します。プロセス別の内訳は`GPU Process Memory`を使い、DXGIのLUIDでGPUを分離します。カウンターを取得できずプロセス合計を使う場合は、概算であることを表示します。

プロセス間で共有されたメモリや集計時刻、ドライバとWindowsの計測方法の違いにより、行の合計と総使用量が一致しない場合があります。正の差分は「未割り当てのGPUメモリ」と表示します。この差分のすべてがカーネルや特定プロセスによる使用とは断定できません。NVMLとタスクマネージャーの値も、同じになるとは限りません。

## ビルド

.NET 11 SDKとWindows SDKが必要です。管理者権限は不要です。Windows App SDK 2.6.5の実行ファイルを同梱する、x64向けポータブルフォルダーを生成します。

```powershell
# .NETランタイム同梱版 → publish/
.\build.ps1

# .NETランタイム非同梱版 → publish_light/
.\build.ps1 -FrameworkDependent -OutputDir publish_light

# 開発用ビルド
 dotnet build -c Release -p:Platform=x64
```

`VramMonitor.exe`と同じフォルダーにあるDLL、XAML関連ファイル、`Languages/`をまとめて配布してください。WinUI 3移行後の配布はフォルダー単位です。非同梱版には.NET 11ランタイムが必要です。

## 検証

```powershell
# GPUを持たないWindowsでも実行できる回帰テスト
 dotnet run --project Tests/CoreRegression -c Release

# NVIDIA + AMD GPU搭載の開発機で行う実機検証
 dotnet run --project Tests/CoreRegression -c Release -- --live

 dotnet list VramMonitor.csproj package --vulnerable --include-transitive
```

依存パッケージのバージョンを`packages.lock.json`で固定し、既知のNuGet脆弱性を復元時のエラーにします。GitHub ActionsでテストとRelease発行を行い、Dependabotで更新を確認します。NVMLはSystem32またはNVIDIAの標準インストール先から読み込みます。

メンテナンスの詳細は[記録](docs/MAINTENANCE_2026-11-15.md)を参照してください。
