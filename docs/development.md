# 開発・検証

.NET SDK 10.0.4xx、Windows x64を使用する。global.jsonの指定を確認する。

```powershell
dotnet build PPPaint.slnx -c Release
dotnet test PPPaint.slnx -c Release --no-build --no-restore
```

ソリューションのプラットフォームは既定値を使う。アプリプロジェクトを直接指定する場合は `-p:Platform=x64` を使用できる。

```powershell
pwsh -NoProfile -File dev/release/Invoke-V024Validation.ps1 -DotnetPath <SDKのdotnet.exeの絶対パス>
dotnet publish src/PPPaint/PPPaint.csproj -c Release -p:Platform=x64 -o artifacts/release/v0.2.4/win-x64
```

`--settings-dir <絶対パス>` で設定と管理窓口を隔離できる。実画面検証は架空の画像を使い、既存画像・通常設定に変更を加えない。検証用UIヘルパーは `dev/validation/Screenshot-UiHelpers.ps1` にある。windows-ui-automationスキルの配置が必要で、実機入力前の告知と対象ウィンドウ限定の撮影を守る。

WinUIの `Window.Closed` は取消可能で、`AppWindow.Closing` より前に呼ばれる。未保存確認は前者で行い、後処理とアプリのウィンドウ一覧更新は `WindowEventArgs.Handled` を確認する。終了取消と複数ウィンドウ終了の実操作を回帰確認する。

v0.2.0以前のリリース検証は旧リボン・レイヤー・AI・MainWindowを要求するため、現行画面の検証には使わない。旧資料と契約試験は履歴として保持する。

管理用実行先は従来どおり `artifacts/management/PPPaint.exe`。`dev/scripts/Build-Management.ps1` で生成できる。更新・インストール・外部配布は別操作であり、通常ビルドでは実施しない。
