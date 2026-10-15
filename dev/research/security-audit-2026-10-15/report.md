# セキュリティ診断・改善実装レポート（2026-10-15）

## 対象と方法
main の作業ツリー。WinUIアプリ、Coreの独自ZIP形式、AI画像送信、更新取得、NuGetを確認。Core/Imaging/GUIと2テストプロジェクトは同一Git。多数の既存未コミット変更（ChunPaintProjectArchive.cs等）を保持し、上書きしていない。追跡テキスト138ファイルの既知トークン/秘密鍵署名候補0、機密名追跡0。資格情報実体・ユーザー画像は読んでいない。

## 指摘・改善
| ID | 深刻度・条件 | 証拠 | 改善方針・実装状態 |
|---|---|---|---|
| CP-01 | 中。利用者の更新情報URL設定または配信元が改変された場合 | src/ChunPaint.Core/UpdateManifest.cs:6-10、src/ChunPaint/Services/UpdateClient.cs:46-59 | SHA-256は改ざん検出であり発行者認証ではない。OS App Installerへ渡す前に期待Publisher/製品IDを検証する改善案。正規Publisherと更新経路の方針が必要なため保留。署名未検証で勝手に起動する実証ではない。 |
| CP-02 | 低。悪意のある巨大なZIP/画像を開く場合 | src/ChunPaint.Core/ChunPaintProjectArchive.cs:64-99 | manifest/画素上限はあるがZIP entry総数や圧縮ファイル全体上限を明示する追加防御を検討。既存編集と形式互換に関わるため保留。メモリ破壊や任意ファイル書込の成立は未確認。 |

## 確認できた防御
ImageEditingClient.cs:16-21 は資格情報/クエリなしHTTPSのみ、:27 は送信同意、:38 はredirect禁止、:67-83 は実読込上限。PNG寸法・画素数も制限する。
UpdateClient.cs:14-21 はHTTPS/redirect拒否、:40 は取得同意、:51 はCreateNew、:78-90 は実転送量上限。ChunPaintProjectArchiveはZIPを任意ディレクトリーへ展開せず、manifest内entryを読んで画素長/合計上限を検証。

## 検証・限界
dotnet list ChunPaint.slnx package --vulnerable --include-transitive --format json は終了0、Core/Imaging/GUI/2テストの5プロジェクトで既知脆弱性表示0（NuGet、2026-10-15）。署名実体/実インストール、画像AI送信、GUI、既存編集の製品回帰は未実施。コード変更なし、文書だけなので形式的な製品ビルドは行わない。Git履歴/全パスワード/外部配布バイナリの網羅検査ではない。
