# 開発・配布の共通手順

アプリのバージョンはsrc/PPPaint/PPPaint.csprojとtool.jsonの指定に従う。作業の進め方は[開発手順](pppaint-development-procedure.md)、操作の決まりは[編集操作の共通契約](interaction-contract.md)を参照する。版別artifacts内の過去の処理をコピーして次版を作らない。

## コミット前

```powershell
& ./dev/Install-EditorHooks.ps1
```

既存のhooksPathやpre-commitがある場合は置き換えず停止する。導入はこのリポジトリのローカル設定だけ。ステージ済み内容から版・キー重複・JSON/PowerShell構文・変更文書のローカルリンク・作者/実行者・禁止識別子・空白を確認する。実マウス操作、クリップボード変更、ビルドは行わない。

## 検証と配布

次はリポジトリルートからpwshで実行する例。SDKパスは環境に合わせる。既存の出力先・検証記録は上書きしない。

```powershell
$editorSdk = 'C:/develop/tool/SSCapture/.tools/dotnet/dotnet.exe'
$editorRun = Join-Path $PWD ('artifacts/workflow-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$editorReceipt = Join-Path $editorRun 'validation.json'
& ./dev/release/Invoke-EditorValidation.ps1 -DotnetPath $editorSdk -NoRestore -ReceiptPath $editorReceipt
# 検証結果を記録してコミットする。下の配布処理は変更のない作業ツリーを要求する。
$editorRelease = Join-Path $editorRun 'win-x64'
& ./dev/release/New-EditorRelease.ps1 -DotnetPath $editorSdk -ValidationReceipt $editorReceipt -OutputDirectory $editorRelease
```

検証記録は対象ソース・試験・設定・配布/検証スクリプトの内容ハッシュを持つ。変更後の古い検証記録では配布できない。文書だけを追記したコミットは試験対象ハッシュを変えない。失敗した出力は証跡として残し、新しい出力先で修正後の処理を行う。

配布処理は自己完結型win-x64、必須資源、アプリ版とコミット、SkiaSharp通知を確認し、通常/並行ランチャー・LICENSE・build-receipt.jsonを同梱する。ZIPとSHA256、公開準備用JSONを生成する。ZIPはEXEだけでなくフォルダー一式を含む。

## 通常デスクトップで起動する

```powershell
$editorProfile = Join-Path $editorRun 'profile'
& ./dev/release/Start-EditorRelease.ps1 -ReleaseDirectory $editorRelease -SettingsDirectory $editorProfile -ReportPath (Join-Path $editorRun 'startup.json')
```

通常の設定先はLOCALAPPDATA下のPPPaint/profiles/editor。別版が同じ設定先を使用しているときは停止するので、明示的に別の設定先を指定する。CopyPreferencesFromに元の設定フォルダーを指定すると、screenshot-preferences.jsonが複製先にない場合だけファイルを複製し、ハッシュを照合する。既存値の初期化や設定の同期はしない。

起動はwinsta0/Defaultを明示し、既存の作業窓は閉じない。PID・パス・版・可視窓・管理応答readyを記録する。同じEXEと設定先が稼働中ならそのプロセスを再利用する。通常デスクトップへのアクセス権がない場合はエラーを記録し、起動成功扱いにしない。

## 公開準備と公開

New-EditorReleaseが生成したZIP.jsonとリリース本文を公開用スクリプトへ指定する。Publishを付けない実行はローカルの整合性確認と予定の表示だけ。認証情報は引数に渡さず、KotorinChunChunで設定済みのGit Credential Managerから実行時だけ取得する。

公開はユーザーから依頼された場合だけ行う。通常pushと新規タグ/リリース作成を使用し、force pushや既存添付の置換はしない。途中で失敗したらGitHub側の実状態を確認する。失敗した公開を完了扱いにせず、再実行で既存リリースを上書きしない。

## 保管と容量整理

容量調査は対象リポジトリ内に限定し、再解析ポイントを追跡しない。bin/objや一時publishは再生成可能だが、試験画像・ログ・設定・旧配布物と区別する。実行中EXEのフォルダー、最新配布ZIP、ソース、Git履歴、利用者データとバックアップは保持する。

削除計画には絶対パス・容量・削除理由・再生成方法・保持対象・実行中参照の有無を記す。計画の作成は削除の承認と見なさない。ビルドキャッシュを削除すると次回ビルドが遅くなるため、その影響を記載する。
