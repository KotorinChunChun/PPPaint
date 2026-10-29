# 開発・検証

KotorinToysに登録した実行先は`artifacts/management/PPPaint.exe`です。子リポジトリで次を実行して生成します。通常ビルドだけではこの実行先は更新されません。

```powershell
cd C:\develop\KotorinToys\tools\PPPaint
pwsh -NoProfile -File dev/scripts/Build-Management.ps1
```

`--settings-dir <絶対パス>`で設定・復元データ・単一起動・管理窓口を分離できます。`--launch-origin host_manual`は本体の手動起動、引数なしは単体の手動起動です。`--startup`と`--hidden`は終了コード2で拒否します。ファイルパスまたは`pppaint://open?path=...`で文書を開けます。旧`chunpaint://`リンクも受け付けます。

通常起動の初回だけ旧`%LOCALAPPDATA%\ChunPaint`の設定と復元データをPPPaint用フォルダーへコピーします。移行元と移行先の既存設定を上書きせず、隔離設定では移行しません。APIキーはWindows資格情報ストアで保持し、旧製品名の資格情報を読み込めます。新たに保存するキーはPPPaint名を使用します。

```powershell
cd C:\develop\KotorinToys\tools\PPPaint
pwsh -NoProfile -File dev/release/Invoke-V020Validation.ps1
```

この検証は既存品質ゲート、Core/Imagingテスト、Releaseビルド、資産・版・日本語リソース・x64構成、インストーラーWhatIfを含みます。AIサービスへの送信、OSへの実インストール、壁紙変更、印刷確定は行いません。

デバッグ起動・画面記録は次のコマンドです。

```powershell
cd C:\develop\KotorinToys\tools\PPPaint
pwsh -NoProfile -File dev/capture/Start-PPPaintDevelopment.ps1
```

[画面記録手順](pppaint-capture-workflow.md)、[プライバシー](../PRIVACY.md)、[変更履歴](../CHANGELOG.md)、[旧版リリースノート](v0.1.0-release-notes.md)も参照してください。
