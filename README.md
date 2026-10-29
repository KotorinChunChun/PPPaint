# PPPaint

PPPaintはWindows 11 x64向けのペイントアプリです。描画、レイヤー、選択、テキスト・図形、画像加工を行い、PNG/JPEG/BMP/GIF/WebPと独自の`.pppaint`形式を扱います。旧`.chunpaint`形式も読み込めます。既存製品のソース・DLL・画像資産を取り込まず、独自のコードとアイコンで実装しています。

## v0.2.0

- 自動保存と前回作業の復元、設定の保存・初期値復帰、文書ごとのタブ・Undo・保存先・ズーム。
- レイヤーロック・不透明度、画素ブロック共有とメモリ上限を備えた操作履歴。
- 高品質リサイズ、モザイク・ぼかし・黒塗り、明るさ・コントラスト・彩度のプレビュー編集。
- WebP入出力、書き出し画質・透過・実容量プレビュー、一括変換の進捗・取消・結果表示。
- 筆圧による線幅・濃さ、手ぶれ補正、背景除去の許容差・境界・残す/消す範囲の調整。
- AI画像編集・生成消去。利用者が接続先・モデル・APIキーを設定し、送信画像を確認して同意したときに実行。
- 更新情報URLを設定した場合の更新確認、ハッシュ検証、確認後のWindows App Installer起動。MSIX作成・署名用スクリプトも用意。

実装と実機検証の範囲は[リリースノート](docs/v0.2.0-release-notes.md)に分けて記載しています。正式なAIアカウント・配布URL・署名証明書は同梱していません。

## 起動・配布

標準成果物はself-contained・非MSIXの`artifacts/release/win-x64/PPPaint.exe`です。管理者権限を要求しません。配布フォルダー内で次を実行すると、現在のユーザーの`%LOCALAPPDATA%\Programs\PPPaint`に登録できます。

```powershell
pwsh -NoProfile -File .\Install-PPPaint.ps1
```

`.pppaint`関連付け、`pppaint://open/?path=...`、上書き更新、アンインストール情報を登録します。解除はWindowsの「インストールされているアプリ」、または次のコマンドから行います。

```powershell
pwsh -NoProfile -File "$env:LOCALAPPDATA\Programs\PPPaint\Uninstall-PPPaint.ps1"
```

MSIXは追加の配布経路です。[パッケージ作成手順](dev/packaging/README.md)のスクリプトに実際のPublisher、Windows SDK、署名証明書、公開URLを指定します。非MSIX版からの移行は別インストールになる場合があります。アプリ内更新では既存EXEを直接置換せず、Windowsインストーラーへパッケージを渡します。

[開発・検証手順](docs/development.md)、[KotorinToysからの利用](docs/kotorintoys-integration.md)も参照してください。

## 使用ライブラリ

WinUI / Windows App SDK、Win2D、.NETに加え、WebP処理にSkiaSharp 3.119.4を使用しています。SkiaSharpはMITライセンスです。Release検証は復元済みNuGetパッケージのライセンス全文とネイティブ依存の第三者通知を`licenses/SkiaSharp/`へ同梱します。各ライブラリのライセンスはアプリ本体の[LICENSE](LICENSE)とは別に適用されます。
