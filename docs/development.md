# 開発・検証

現行版v0.2.34の構成と検証の入口です。変更の進め方、ビルド、配布物の作成・起動は [開発作業手順](../dev/pppaint-development-procedure.md) にまとめています。最初に [開発ルール](../AGENTS.md) と [却下事項・方針変更](../dev/reject.md) を確認してください。

## 開発環境

Windows x64、PowerShell 7（`pwsh`）、.NET SDKを使用します。[global.json](../global.json) は10.0.400を起点に同じフィーチャーバンド内の最新パッチを選択します。v0.2.29・v0.2.30の記録では10.0.401を使用しました。

WinUI / Windows App SDK、Win2D、SkiaSharp等の依存関係はNuGetで復元します。バージョンは各プロジェクトファイルを正とします。アプリは非MSIX・自己完結型のwin-x64出力です。通常のビルド・publishに、インストールやOS登録変更は含まれません。

## 構成

| 場所 | 役割 |
|---|---|
| [src/PPPaint](../src/PPPaint) | `EditorWindow`、WinUI画面、入力、ファイル・クリップボード、設定、管理窓口 |
| [src/PPPaint.Core](../src/PPPaint.Core) | `ScreenshotDocument`、注釈・画像モデル、編集履歴、幾何計算、コピー・連結・切り抜き |
| [src/PPPaint.Imaging](../src/PPPaint.Imaging) | Win2D描画、画像形式、合成キャッシュ、サイズ変更プレビュー |
| [tests/PPPaint.Core.Tests](../tests/PPPaint.Core.Tests) | モデル・幾何・操作・保存契約の試験 |
| [tests/PPPaint.Imaging.Tests](../tests/PPPaint.Imaging.Tests) | Windows/Win2D上の描画・全画素・入出力試験 |
| [dev/release](../dev/release) | ビルド・テストの入口と配布ランチャー生成 |
| [dev/validation](../dev/validation) | 専用画像・設定でのUIA／実入力検証 |

現在の画面は `EditorWindow` です。v0.2.30で、旧版由来の処理（旧レイヤー編集、`.pppaint`形式、選択範囲エンジン、写真補正・背景除去・グラデーション・特殊ブラシ・一括変換、自動更新、WebP書き出し）とその試験を削除しました。画像コーデックには現行UIから使用しないBMP/GIF出力分岐も残っています。アプリの保存形式はPNG/JPEGです。回転・反転は `ScreenshotOrientation`、矩形範囲の画素変換は `ScreenshotCrop.RegionArea` が担います。

## 変更時に維持する仕組み

- **画素の一致**：編集表示も保存も96dpiで合成した `ScreenshotRenderCache.GetScene` を使い、表示は `ScreenshotRenderer.DrawCanvasImage` の最近傍補間で行います。キャッシュ所有の画像を呼び出し側で破棄しません。画面DPIへ直接ベクター描画すると、保存前後で画素が変わります。
- **入力と描画の座標**：画像は `PaintCanvas`、外側の余白を含む選択枠・ポインター・入力は `InteractionCanvas` です。入力は `PaintCanvas` 相対で計算し、描画原点を `TransformToVisual` でそろえます。再描画には `InvalidateCanvas` を使用します。
- **効果の範囲**：マスクのないぼかし・モザイクは `CanvasComposite.BoundedCopy` で合成します。マスク付きは対象領域をクリアして `SourceOver` とし、範囲外のRGBAを維持します。
- **サイズ変更の仮状態**：ダイアログの値と境界は適用まで文書を変更しません。８プレビューは元画像の表示範囲を固定し、変更後の境界と切り取り部分の暗さを更新します。
- **起動と終了**：画像読み込みは `CreateResources` でデバイス準備ができるまで待ちます。未保存確認は `EditorWindow` の `Closed` で行い、後処理とアプリの窓一覧更新は `Handled` による取消を尊重します。
- **画素の境界**：画像は線形補間で描くため、位置が小数だと表示・保存がぼやけます。移動量は `ScreenshotManipulation.AlignMoveToPixels` で画像を含む選択・矩形範囲だけ整数へ丸め、図形のみなら小数座標への吸着を維持します。中央配置は `ScreenshotPasting.CenteredOrigin`、挿入は `ScreenshotDocument.InsertImage` で整数画素にします。矩形範囲は `ScreenshotBox.SnapToPixels` で境界に合わせ、削除・切り取り・切り抜きは `ScreenshotCrop.RegionArea` だけで画素範囲へ変換します。
- **文字入力の履歴**：文字欄の連続入力は `ScreenshotDocument.Replace(annotation, mergeKey)` で1件のUndoにまとめます。欄を離れたら `EndMerge` で区切り、保存時も `CaptureForSave` で不変状態を取得する前に区切ります。Ctrl+S後の追加入力をUndoすると保存時へ戻れます。保存失敗時にも区切りは残りますが、編集内容と履歴は保持します。
- **画面配置**：`ToolbarWrapPanel` が共通操作の群、描画ボタン、設定の群を折り返します。画像のViewportスクロールは維持します。
- **貼り付け可否**：`EditorWindow.Clipboard.cs` がクリップボード変更と窓のアクティブ化を監視します。画像の有無をUIスレッドへ反映し、貼り付けと▼を同時に無効化します。実行前にも再確認し、実際の窓終了時だけ購読を解除します。
- **ペンの描画予定位置**：`ScreenshotPenPreview` が実描線と同じ96dpiのスタンプを作り、InteractionCanvasへ最近傍補間で描きます。端は倍率変換前の画素格子で切ります。ホバー中は新規作図設定を使い、ドラッグ中は描線の仮状態だけを表示して二重合成を避けます。保存側には呼び出しを追加しません。

## 検証の入口

ビルドとCore/Imaging全試験の共通入口は [Invoke-EditorValidation.ps1](../dev/release/Invoke-EditorValidation.ps1) です。`-DotnetPath` でSDKを選び、復元済みなら `-NoRestore` を指定できます。ソリューションには `-p:Platform=x64` を付けません。個別アプリのpublish時に指定します。

| 対象 | 確認先 | 入力・副作用 |
|---|---|---|
| ペンの●・画素・倍率・DPI・保存除外／全ツール太さ3 | `PenPreviewTests` / [Test-EditorPenDefaults.ps1](../dev/validation/Test-EditorPenDefaults.ps1) | GPU比較と専用設定のUIA。実マウスは使わない |
| 画像がない場合の貼り付け無効化と変更への追従 | [Test-EditorPasteAvailability.ps1](../dev/validation/Test-EditorPasteAvailability.ps1) | UIAとクリップボード全形式の事前退避・変更・復元。他のコピーを検知したら上書きしない |
| サイズ変更、８プレビュー、リセット | [Test-EditorResizeComparison.ps1](../dev/validation/Test-EditorResizeComparison.ps1) | `-UiOnly` はUIA。省略すると実マウスとAlt+Eを使用 |
| プレビューの画素と幾何 | `FixedResizePreviewTests` / `FixedResizePreviewRenderingTests` | ８方位、1px変更、96/144/192dpiの画像固定を試験 |
| 重ね貼り・連結 | [Test-EditorPasteV0222.ps1](../dev/validation/Test-EditorPasteV0222.ps1) | UIAとクリップボードの退避・変更・復元 |
| 貼り付けキー・キャンバス端・統合 | [Test-EditorV0222Native.ps1](../dev/validation/Test-EditorV0222Native.ps1) | 実キー・マウスとクリップボードを使用 |
| 選択対象の切り抜き | [Test-EditorSelectedCrop.ps1](../dev/validation/Test-EditorSelectedCrop.ps1) | 実キー・マウスを使用 |
| 選択対象のコピー | [Test-EditorSelectedCopy.ps1](../dev/validation/Test-EditorSelectedCopy.ps1) | 実キー・マウスとクリップボードを使用 |
| 表示倍率67%での画像移動と保存画素 | [Test-EditorV0230Move.ps1](../dev/validation/Test-EditorV0230Move.ps1) | 実マウスを使用。専用の市松模様PNGを上書き保存して補間画素を数える |

検証スクリプトは操作対象、必要画像、初期設定、実行引数を確認してから選びます。版番号付きの全スクリプトを一括で現行版へ実行するものではありません。特にv0.2.21の９ビュー検証は、中央プレビューを削除した現行仕様と一致しません。v0.2.0以前の旧リボン・レイヤー・AI・`MainWindow`を前提とする検証も履歴として扱います。

## 確認済みの範囲

[v0.2.33実装報告](../dev/v0.2.33-imp.md) には、Releaseの警告・エラー0、341試験成功、配布版UIAによる貼り付け可否の9ケース、クリップボード復元、通常デスクトップ上の新版起動を記録しています。白・透明・半透明のPNG背景の画素往復も既存試験で確認しました。実キー・マウスは使っていません。

[v0.2.32実装報告](../dev/v0.2.32-imp.md) には、Releaseの警告・エラー0、Core243＋Imaging98＝341試験成功、文字の不透明度とPNGの画素往復、旧設定移行、配布版UIAによる透かしボタン削除・不透明度の再起動保持、通常デスクトップ上の新版起動を記録しています。この版では実キー・マウスを使っていません。

[v0.2.31実装報告](../dev/v0.2.31-imp.md) には、Releaseの警告・エラー0、Core239＋Imaging94＝333試験成功、配布版での設定保持UIA、通常デスクトップ上の新版起動を記録しています。図形吸着と保存後UndoはCore回帰試験で確認しました。今回の実キー・マウス試験は前面化ガードにより入力前に中止し、未実施として記録しています。

[v0.2.30実装報告](../dev/v0.2.30-imp.md) には、Releaseの警告・エラー０、Core233＋Imaging94＝327試験成功（未使用コードの削除で旧機能の試験171件を削除）、配布版での実マウス移動と保存画素の確認、更新版の起動確認を記録しています。[v0.2.29実装報告](../dev/v0.2.29-imp.md) には、Core388＋Imaging98＝486試験成功、ソース版／配布版UIA、専用窓での境界ドラッグとAlt+E、対象窓の表示確認を記録しています。いずれも実装時の記録であり、文書更新のたびに再実行した結果ではありません。

GPU試験で生成した画像と実画面のスクリーンショット、UIAによる操作と実キー・マウス操作を区別して報告します。自動試験や特定の窓サイズでの成功は、未実施のDPI・IME・読み上げ・全画面構成、別PCへのインストール、MSIXの信頼や公開配布の確認を代替しません。

詳細ログ・生成画像は `artifacts` 配下のローカル証跡です。Gitの新規チェックアウトには含まれないため、過去の結果は版ごとの `dev` 報告を参照し、必要な検証だけ新しい専用出力先で再実行します。
