# ChunPaint

ChunPaint は、Windows 11 の「ペイント」で慣れた操作感を維持しつつ、独自機能を追加できる Windows 向けペイントソフトです。

Microsoft Paint のソース、DLL、アイコン、画像などは取り込まず、公開された挙動と実機観察を基に独自実装します。

## v0.1.0 リリース候補

このリポジトリのリリース候補は、Windows 11 x64向けのself-containedアプリです。ビルド時のアプリ表示名とバージョンは `ChunPaint 0.1.0`、実行権限はユーザー権限（`asInvoker`）です。実サービスのAI、外部通信、管理者権限は使用しません。

配布用の検証は、プロジェクト同梱のdotnetと `pwsh` で次のコマンドから再実行できます。

```powershell
pwsh -NoProfile -File dev/release/Invoke-ReleaseValidation.ps1
```

検証スクリプトは `artifacts/release/win-x64` にself-containedのRelease成果物を生成し、Coreテスト、Releaseビルド、独自ロゴ、禁止参照、成果物のバージョンとx64 PEを確認します。壁紙変更、実印刷、外部ネットワーク、署名証明書の購入・使用は行いません。

## 配布上の制限

現時点は `WindowsPackageType=None` の非MSIX構成です。そのためMSIX署名、正式なpublisher identity、`.chunpaint`ファイル関連付け、カスタムプロトコル、インストール／更新／アンインストールは未構成です。将来MSIXまたはインストーラーを採用する際は、`ApplicationDisplayName`、`ApplicationPublisher`、`ApplicationIconSource`、`src/ChunPaint/Assets/ChunPaintIcon.svg`を配布メタデータへ接続してください。

利用規約、プライバシー、外部通信、既知の実機依存は [PRIVACY.md](PRIVACY.md)、変更履歴は [CHANGELOG.md](CHANGELOG.md)、v0.1.0の受入範囲は [リリースノート](docs/v0.1.0-release-notes.md)を参照してください。

## 開発画面の記録

ChunPaintとメッセージ駆動のキャプチャーサービスを併用起動する。

```powershell
pwsh -NoProfile -File dev/capture/Start-ChunPaintDevelopment.ps1
```

起動直後の最大化画面が自動保存される。追加撮影、メタデータ、サービス終了方法は [開発画面キャプチャー運用](docs/chunpaint-capture-workflow.md) を参照する。
