# 開発・検証

.NET SDK 10.0.4xx、Windows x64を使用する。global.jsonの指定を確認する。

```powershell
dotnet build PPPaint.slnx -c Release
dotnet test PPPaint.slnx -c Release --no-build --no-restore
```

ソリューションのプラットフォームは既定値を使う。アプリプロジェクトを直接指定する場合は `-p:Platform=x64` を使用できる。

```powershell
pwsh -NoProfile -File dev/release/Invoke-V0214Validation.ps1 -DotnetPath <SDKのdotnet.exeの絶対パス>
dotnet publish src/PPPaint/PPPaint.csproj -c Release -p:Platform=x64 -o artifacts/release/v0.2.14/win-x64
```

依存関係の復元済み環境では検証スクリプトに `-NoRestore` を指定できる。

`--settings-dir <絶対パス>` で設定と管理窓口を隔離できる。実画面検証は架空の画像を使い、既存画像・通常設定に変更を加えない。検証用UIヘルパーは `dev/validation/Screenshot-UiHelpers.ps1` にある。windows-ui-automationスキルの配置が必要で、実機入力前の告知と対象ウィンドウ限定の撮影を守る。

画像の読み込みはCanvasControlのLoadedだけで開始せず、CreateResourcesでCanvasDeviceの準備完了を待つ。ファイル読み込みから得た同じバイト列をデコードと競合検知に使用する。UIAのv0.2.6回帰試験は `dev/validation/Test-EditorV026.ps1`。一時設定と架空の900×550画像で起動した検証専用プロセスに限り使用する。

WinUIの `Window.Closed` は取消可能で、`AppWindow.Closing` より前に呼ばれる。未保存確認は前者で行い、後処理とアプリのウィンドウ一覧更新は `WindowEventArgs.Handled` を確認する。終了取消と複数ウィンドウ終了の実操作を回帰確認する。

v0.2.0以前のリリース検証は旧リボン・レイヤー・AI・MainWindowを要求するため、現行画面の検証には使わない。旧資料と契約試験は履歴として保持する。

管理用実行先は従来どおり `artifacts/management/PPPaint.exe`。`dev/scripts/Build-Management.ps1` で生成できる。更新・インストール・外部配布は別操作であり、通常ビルドでは実施しない。

クリップボードと背景のv0.2.7回帰試験は dev/validation/Test-EditorV027.ps1。隔離プロセスと架空900×550画像を使用し、クリップボードを退避・復元する。実キー・マウス操作は行わない。

パレットとキャンバス変更のv0.2.8 UIAは dev/validation/Test-EditorV028Palette.ps1 と Test-EditorV028Canvas.ps1。架空900×550画像と隔離設定だけに使用する。キャンバスのプレビューは編集履歴を変えず、共通描画と確定結果の全画素一致を試験する。

画像描画はPaintCanvas、余白を含む選択/ポインタ描画と入力はInteractionCanvasへ配置する。入力座標はPaintCanvas相対で取得し、描画の原点はTransformToVisualで合わせる。キャプチャはInteractionCanvasへ統一する。設定変更などで再描画する場合はInvalidateCanvasを使う。

ぼかし/モザイクはマスクのない場合にCanvasComposite.BoundedCopyで合成する。CanvasBlend.CopyによるDrawImageは範囲外を透明にするため使用しない。マスクがある場合は対象幾何のみをクリアしてSourceOverとし、RGBA全画素で範囲外不変を検証する。

上部ツールバーはToolbarWrapPanelで幅に応じて改行する。共通操作は操作群単位、描画ツールはボタン単位、設定は色/太さ/文字などの群単位で折り返す。画像Viewportのスクロールは維持する。v0.2.13の配置確認はdev/validation/Test-EditorV0213Toolbar.ps1を隔離設定の専用プロセスへ実行する。UIAで1280/920幅と全16ツールを切り替え、操作の可視性・重なり・設定保持を調べる。実入力や画面撮影は含めない。

編集画像はScreenshotRenderCache.GetSceneで保存と同じ96dpiの合成結果を取得し、ScreenshotRenderer.DrawCanvasImageの最近傍補間で表示する。キャッシュ所有の戻り値を呼出側でDisposeしない。内容不変の選択・倍率変更では再利用し、ドラフト・画像・注釈・背景の変更では再生成する。デバイス再作成/終了時はキャッシュごと破棄する。画面DPIへ直接ベクター描画すると保存前後の画素が変わるため戻さない。
