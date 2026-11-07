# PPPaint 開発作業手順

v0.2.35のソースを基準とする手順です。構成と試験の選び方は [開発・検証](../docs/development.md)、利用者向けの操作は [キー一覧](../docs/keyboard-shortcuts.md) を参照してください。以下のコマンドはリポジトリルートで `pwsh` から実行します。

## 変更の開始と記録

1. [AGENTS.md](../AGENTS.md)、[dev/reject.md](reject.md)、対象版の要求・実装報告を読み、`git status --short` で既存変更を確認します。
2. 現行版は [PPPaint.csproj](../src/PPPaint/PPPaint.csproj) と照合します。次の機能修正は末尾のパッチ番号を上げます。完成済みの版の文書整理だけならアプリの版を変更しません。
3. ユーザー要求は `dev/vX.Y.Z-req.md` に１件１行で記録します。AIの判断で意味・範囲を変えず、却下事項はユーザーが明言した場合だけ追記します。
4. `dev/vX.Y.Z-imp.md` に変更対象・方針・検証方法を先に記録します。要求の粒度が粗い場合だけ `task` を追加し、実行できる単位へ分割します。
5. `rg -n 'キーワード' src tests dev` で関連するイベント入口、状態、計算、描画、過去の回帰記録をまとめて確認します。

モデル・幾何計算はCore、画像合成はImaging、画面と入力変換はアプリへ置きます。変更した動作を守る検証を選び、既存試験を削って通さないようにします。コードを変更しない文書整理では、ソースとの照合・リンク・記載コマンド・差分の確認を検証とします。

## 復元・ビルド・テスト

PATH上のSDKを使う例です。`--version` が [global.json](../global.json) と適合することを確認します。

```powershell
$pppaintDotnet = 'dotnet'
& $pppaintDotnet --version
& $pppaintDotnet restore PPPaint.slnx
& ./dev/release/Invoke-EditorValidation.ps1 -DotnetPath $pppaintDotnet -NoRestore
```

このチェックアウトでv0.2.29に使用したSDKは `C:/develop/tool/PdfDesk/.tools/dotnet/dotnet.exe`、v0.2.30～v0.2.35では `C:/develop/tool/SSCapture/.tools/dotnet/dotnet.exe`（いずれも10.0.401）です。別PCに必須のパスではありません。PATHにSDKがなければ `$pppaintDotnet` に利用可能なSDKの絶対パスを設定します。

共通スクリプトはReleaseビルド、Core/Imaging全試験、`git diff --check` を順に実行します。事前の `restore` を省く場合は、スクリプトの `-NoRestore` を外してください。ソリューションに `-p:Platform=x64` を渡すと構成が一致しないため指定しません。

不具合の切り分けには、まず対象クラスを絞ることもできます。

```powershell
& $pppaintDotnet test tests/PPPaint.Core.Tests/PPPaint.Core.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~FixedResizePreviewTests'
```

絞った試験の成功と全試験の成功は分けて記録します。ファイルロックが出たら対象のプロセスと出力先を調べ、未保存画像のあるアプリを強制終了しません。利用中の配布先を上書きせず、別の出力先で検証します。再試行を繰り返す前に原因を確認してください。

## 自己完結型の配布物を作る

通常の配布作成は[共通手順](workflow.md)のNew-EditorRelease.ps1を使用します。以下は内部処理を説明する個別コマンドです。共通処理では検証記録との一致、通知同梱、ZIPとハッシュの生成まで行います。

次の例はv0.2.35を再現するものです。新しい版では、プロジェクトの版と出力先をそろえます。既存の配布物を保持するため、同じ出力先がある場合は停止します。再検証では別名の新しいフォルダーを指定してください。

```powershell
$pppaintRelease = Join-Path $PWD 'artifacts/release/v0.2.35/win-x64'
if (Test-Path -LiteralPath $pppaintRelease) { throw '既存の配布物を残し、新しい出力先を指定してください。' }
& $pppaintDotnet publish src/PPPaint/PPPaint.csproj -c Release -p:Platform=x64 --no-restore --self-contained true -o $pppaintRelease --nologo
if ($LASTEXITCODE -ne 0) { throw 'publishに失敗しました。' }
& ./dev/release/New-EditorLauncher.ps1 -ReleaseDirectory $pppaintRelease
Copy-Item -LiteralPath ./LICENSE -Destination (Join-Path $pppaintRelease 'LICENSE.txt')
```

publishは `App.xbf`、`EditorWindow.xbf`、`PPPaint.pri` とカーソル資源の存在を確認します。EXEだけを取り出さず、依存DLL・資源を含むフォルダーを一式で扱います。

SkiaSharpの通知はpublishだけでは同梱されません。復元した `SkiaSharp.NativeAssets.Win32` パッケージの `LICENSE.txt` と `THIRD-PARTY-NOTICES.txt` を、出力先の `licenses/SkiaSharp` へそのまま複製します。パッケージ版は `src/PPPaint.Imaging/PPPaint.Imaging.csproj`、NuGetキャッシュの場所は復元後の `src/PPPaint.Imaging/obj/project.assets.json` の `packageFolders` で確認します。現行の [Invoke-EditorValidation.ps1](release/Invoke-EditorValidation.ps1) は通知の複製や配布を行いません。

必要な検証を配布先の実行ファイルでも実施し、コミット、アプリの版、実行パス、検証結果を対応付けます。ビルド・publishの成功を、インストール・MSIX信頼・公開先への配布完了として記録しないでください。

## 起動と設定の分離

`New-EditorLauncher.ps1` が生成する `Start-PPPaint.cmd` は、`%LOCALAPPDATA%\PPPaint\profiles\editor` を通常設定先に使います。同じ設定先のアプリが既に動いていると、要求はそちらへ転送されます。新しいEXEを呼んだだけで、新版の別プロセスが動いたとは判断できません。

配布物にある `Start-PPPaint-Parallel.cmd` は、版別の設定先（v0.2.35では `%LOCALAPPDATA%\PPPaint\profiles\editor-v0.2.35`）を使う並行起動用です。このランチャーは `New-EditorRelease.ps1` が生成します。

旧版の未保存画像を残して新版を確認する場合は、異なる絶対パスを `--settings-dir` へ渡します。以下は隔離した開発用設定で起動する例です。起動前に利用者へ知らせ、終了時に利用者の作業窓を巻き込まないようにします。

```powershell
$pppaintProfile = Join-Path $PWD ('artifacts/manual-v0.2.35-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '/profile')
$pppaintExe = Join-Path $pppaintRelease 'PPPaint.exe'
$pppaintProcess = Start-Process -FilePath $pppaintExe -ArgumentList @('--settings-dir', ('"' + $pppaintProfile + '"')) -PassThru -WindowStyle Hidden
```

アプリは起動処理で編集窓を表示します。窓の準備が完了してから、実行パス、プロセスID、版、設定先、通常デスクトップ上の可視窓を照合します。管理応答の確認には [Invoke-CheckedEditorManagement.ps1](validation/Invoke-CheckedEditorManagement.ps1) を使えます。

```powershell
& ./dev/validation/Invoke-CheckedEditorManagement.ps1 -TargetProcessId $pppaintProcess.Id -ExecutablePath $pppaintExe -SettingsDirectory $pppaintProfile -Version '0.2.35' -Command status
```

`show` は対象を表示し、`stop` はアプリ側で未保存・処理中を確認してから終了します。起動確認で未保存画像のある旧版が見つかった場合は残します。並行用設定は通常設定と別なので、検証設定を通常設定へ上書きしません。

## UI・実入力の検証

UI検証は架空画像と `artifacts` 内の専用設定だけを使います。[Screenshot-UiHelpers.ps1](validation/Screenshot-UiHelpers.ps1) は、対象プロセスに編集窓が１つだけあることを要求します。各スクリプトが参照する `windows-ui-automation` の `ui-toolkit.ps1` を実行環境に用意してください。

サイズ変更画面をUIAで確認する例です。毎回新しい設定先を作るため、時刻を含む出力名にします。

```powershell
$pppaintQa = Join-Path $PWD ('artifacts/qa-v0.2.35-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
pwsh -NoProfile -Sta -File ./dev/validation/Test-EditorResizeComparison.ps1 -AppPath $pppaintExe -SettingsDirectory (Join-Path $pppaintQa 'profile') -OutputDirectory (Join-Path $pppaintQa 'results') -UiOnly
```

`-UiOnly` を外すと実マウス・キーを使います。事前に操作することを伝え、対象窓の前面化と入力座標を確認し、ガードが失敗したら中止します。`-CaptureWindows` は対象窓だけを撮影します。操作後は利用者にPC操作を戻したことを伝えます。

コピー・貼り付けの試験は [Clipboard-Snapshot.ps1](validation/Clipboard-Snapshot.ps1) で全形式を退避・復元します。退避できない形式があるときは変更前に止めます。UIA検証であってもクリップボードを使うスクリプトがあるため、実行前に入出力を確認してください。

報告には試験用EXE・設定先、成功／失敗／未実施、実入力の有無、画像がGPU生成か窓撮影かを明記します。利用者の画像・クリップボードを使って試験しません。過去の検証に失敗記録が残っていても、原因と最終結果を区別して保存します。

## コミットと完了

- 対象版の要求チェックと実装報告へ結果を反映し、未完了・未検証を完了扱いにしません。過去版の要求や証跡は保持します。
- `git diff --check` と文書リンクを確認し、`git add -- <対象ファイル>` で変更対象を明示します。
- `git diff --cached --check` と差分を読み、無関係な生成物・キャプチャー・利用者の変更が含まれていないことを確認します。
- 作者・実行者は [リポジトリの指定](../AGENTS.md) を守り、グローバル設定を変更しません。検証に問題がなければフェーズをコミットします。
- 機能修正の完了時は、通常デスクトップで最新版が起動したことを確認し、未保存の旧窓は残します。文書だけの変更では起動中のアプリを再起動しません。

push、認証、外部配布、インストールは、ローカルの文書整理・ビルド・検証とは別の操作です。
