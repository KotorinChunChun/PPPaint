# 高度編集機能・改名互換の検証

実施日: 2026-10-29

## 実施内容

- [x] 複製・修復ブラシで、存在しない `PixelSurface.Dispose()` の呼び出しを除去した。
- [x] 各プレビューをストローク開始時の原画から再計算し、移動イベント数で不透明度が累積する問題を解消した。
- [x] 複製元レイヤー識別子を実際の `LayerState.Id` に合わせて `string?` に修正した。
- [x] 操作履歴画面で不足していた `PPPaint.Editing` 名前空間の参照を追加した。
- [x] 調整レイヤーの再編集プレビューへ描画モードを反映し、確定時の合成と計算を合わせた。
- [x] 高度編集ダイアログの操作説明をツールチップへ移した。
- [x] 新形式 `.pppaint` と新プロトコル `pppaint://` に加え、旧形式 `.chunpaint` と旧プロトコル `chunpaint://` の読み込み互換を維持した。新規リンクのスキーム定数は `pppaint` とする。
- [x] 複製・修復入力の不変性、プレビュー回数による結果不変性、不透明度と選択範囲、新旧形式・プロトコルの組合せを検証した。
- [x] 既存ソース契約を、一時画像を使うオブジェクト合成と起動引数解析の委譲先に追従させた。

## 検証結果

実行:

```powershell
dotnet test tests/PPPaint.Core.Tests/PPPaint.Core.Tests.csproj -c Release -p:Platform=x64 --no-restore --nologo --logger 'trx;LogFileName=core-tests.trx' --results-directory dev/history/v0.2.0/logs/20261029-advanced-validation
```

- Core: 274件合格、失敗0件、スキップ0件。
- 対象差分の `git diff --check`: 成功。
- 制限環境内の初回実行はvstestからtesthostへの接続が90秒でタイムアウトした。ローカルIPCを利用できる実行環境で上記テストを完了した。
- NuGet脆弱性データ取得はネットワーク制限により `NU1900`。パッケージ復元・コンパイル・テスト結果とは別の未確認事項とする。
- 生結果: `logs/20261029-advanced-validation/core-tests.trx`。Git登録対象外。

## 確認範囲

この報告はCoreの自動テストとソース修正を対象とする。アプリ全体のビルド、実GUIの描画結果、筆圧入力、DPI別表示、KotorinToysからの起動・終了、配布物の検証結果は統合側の報告に記録する。
