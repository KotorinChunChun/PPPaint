# MSIX配布・更新基盤

既存の非パッケージ配布とは別に、publish済みフォルダーからMSIXを作成します。Windows SDKのMakeAppxが必要です。署名する場合はSignToolと、現在のユーザーの証明書ストアに登録済みのコード署名証明書が必要です。証明書の作成・インポート、信頼設定変更、公開サーバーへの送信、インストールは行いません。

## 実行

```powershell
cd C:\develop\KotorinToys\tools\PPPaint
pwsh -File dev/packaging/New-PPPaintPackage.ps1 -PublishDirectory artifacts/release/win-x64 -OutputDirectory artifacts/packages/0.2.0.0 -Version 0.2.0.0 -Publisher 'CN=実際の発行者' -WhatIf
```

実行時は `-WhatIf` を外します。`-Publisher` は署名証明書のSubjectと一致させます。`-CertificateThumbprint` を指定した場合だけSHA-256で署名し、SignTool `/pa /v` で検証します。`-TimestampUri` は利用するHTTPSタイムスタンプサーバーです。証明書未指定の成果物は未署名であり、正式配布完了を意味しません。

`-SdkBinDirectory` にはMakeAppx/SignToolを含むSDKのx64ディレクトリを指定できます。省略時はPATH、Windows Kitsの順で探索します。

`-UpdateBaseUri https://実際のホスト/配布ディレクトリ` を指定すると、同じ出力先に `PPPaint.appinstaller` と `update.json` を生成します。これらとMSIXを同一HTTPS配布先に公開する運用です。App Installerの起動時チェック間隔は12時間、強制ダウングレードは指定しません。URLに認証情報、クエリ、フラグメントを含められません。

入力と出力の包含・同一関係、再解析ポイントを拒否し、既存MSIXを上書きしません。ステージングは毎回GUID名で生成し、障害調査用に保持します。自動削除はありません。

## 更新情報

`UpdateManifest.Parse` は3桁/4桁の版番号、資格情報のないHTTPS URL、64桁SHA-256を検証します。`EnsureUpgradeFrom` は同じ版と古い版を拒否し、`VerifyPayloadAsync` は読み込んだストリームの現在位置から末尾までを検証します。呼出側はストリーム先頭を渡してください。

SHA-256は改変検知であり発行元の認証ではありません。呼出側で信頼する更新情報URLの管理、リダイレクト後HTTPS/ホスト検証、サイズ上限、署名検証を行ってからインストールします。自動ダウンロード・適用UIはこの基盤に含みません。

## 検証状況

PowerShell構文解析、既存publishフォルダーを入力にしたWhatIfが成功。UpdateManifestの降格拒否、不正URL/ハッシュ/版の拒否、実バイトのSHA-256一致と改変検知の3テスト成功。Windows SDK 10.0.26100.0で未署名MSIXの実packと構造確認に成功しました。実証明書・配布URL未提供のため、署名、クリーン環境インストール、パッケージ関連付け起動、更新実行は未検証です。パッケージ化による起動引数/アクティベーションの差異も実機で確認します。

## 公式仕様

- [MakeAppxによるパッケージ作成](https://learn.microsoft.com/en-us/windows/msix/package/create-app-package-with-makeappx-tool)
- [SignToolによる署名](https://learn.microsoft.com/en-us/windows/msix/package/sign-app-package-using-signtool)
- [App Installerファイル作成](https://learn.microsoft.com/en-us/windows/msix/app-installer/how-to-create-appinstaller-file)

## アプリ内の更新確認

「更新の確認」は初期状態では公開URL未設定・自動確認OFFです。利用者がHTTPSのupdate.jsonを設定し、自動確認をONにすると、起動時と1日以上経過後に情報だけ取得します（起動中は1時間単位で期限確認）。新しい版の通知以外は表示しません。パッケージ取得・Windowsインストーラー起動はそれぞれ明示的な確認後に進みます。

更新情報は64KiB、パッケージは512MiBを上限とし、HTTPリダイレクトを拒否します。同じ版・古い版、MSIX以外の取得を拒否し、SHA-256不一致では取得ファイルを削除します。JSONのPublisherは配布元の申告値として表示し、暗号的な保証とは扱いません。署名の信頼とパッケージ互換性はWindowsインストーラーが検証します。インストーラーを開いたことを更新完了とは表示しません。

取得物はLocalApplicationData/PPPaint/UpdatesへGUID名で保存します。既存非MSIX版のEXE・設定・関連付けをアプリ内から置換しません。非MSIX版からMSIX版への移行は別インストールになる場合があり、既存版の解除・文書の移行は利用者が確認して行います。既存非MSIX版の上書き更新には従来のInstall-PPPaint.ps1を引き続き利用できます。

公開URLと正式証明書が未提供のため、現行成果物では実配信からの取得・インストール・次版への更新は未検証です。テストは通信を差し替えたHTTPハンドラーで行い、OS登録を変更しません。

## 未署名パッケージ実検証（2026-09-12）

最終Releaseゲート成功後の`artifacts/release/win-x64`を使用し、Windows SDK 10.0.26100.0 x64のMakeAppxで実パッケージを生成しました。スクリプトの修正や追加権限は不要でした。

- 成果物：`artifacts/msix-v020-validation/PPPaint-0.2.0.0-x64.msix`
- MakeAppx結果：`Package creation succeeded.`
- 版・アーキテクチャ：0.2.0.0 / x64。試験用Publisher：`CN=PPPaint Development`。
- 構造：1026エントリー。manifest、block map、content types、EXE/DLL、ロゴ、SkiaSharpライセンス・第三者通知を確認。
- サイズ：120334654バイト。
- SHA-256：`BECED97510ED96507260F35C9EE36D341931ED2AABDC17FE4DD76C54AC2C24B0`
- ログ：`artifacts/v0.2.0-tests/msix-validation.log`

`AppxSignature.p7x`は存在せず、未署名であることも確認しています。試験用Publisherは正式な発行者を表しません。証明書作成、署名、インストール、公開は実行していません。
