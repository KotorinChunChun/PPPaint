# PPPaint 非常駐管理連携の実装記録

対象版: 0.2.0。対象日: 2026-10-29。

## 実装

- [x] 起動引数と設定先の正本を `LaunchOptions` に集約。
- [x] 引数なしで編集画面を表示し、同じ設定先への二重起動は既存PIDに表示・ファイルを引き継ぐ。
- [x] `--settings-dir <絶対パス>` と `--settings-dir=<絶対パス>` に対応。指定時は `PPPAINT_DATA_DIRECTORY` より優先する。設定値がない場合は `%LOCALAPPDATA%\PPPaint` を使う。
- [x] `--launch-origin kotorintoys` を `host_manual` として扱い、本体の `host_manual`、`via_kotorintoys`、`direct_task` と単体起動 `standalone` を管理応答に反映。
- [x] `--startup`、`--hidden` は終了コード2で拒否。トレイ・常駐・自動起動予約は追加しない。
- [x] `status`、`show`、`stop` を管理契約2で応答。stopは受理応答後に通常の画面終了へ渡し、直前にも状態を再確認する。
- [x] 最後の編集画面を閉じたら管理窓口とmutexを解放してアプリを終了。
- [x] タイトル生成を `WindowTitle.Create` に集約。主画面は `PPPaint - ver 0.2.0`、確認画面は `画面名 - PPPaint - ver 0.2.0`。文書名と未保存印は文書タブで表示する。
- [x] `ContentDialog` とWindowsファイル・フォルダーピッカーの表示期間を共通ヘルパーで追跡。

管理pipeは `KotorinToys.<現在ユーザーSID>.pppaint`。設定分離時は `pppaint-<識別子>` に変える。識別子は絶対設定パスの区切りを `\` に統一し小文字化したUTF-8値のSHA-256先頭16桁（小文字16進）。mutexは同じ名前に `Local\` を付ける。

pipeは `CurrentUserOnly`、要求は4096文字・受付から2秒以内。書込みにも同じ期限を適用する。操作要求は現在PIDと一致する `expectedPid` を必須とし、接続元プロセスのWindowsセッションも確認する。任意の `expectedSessionId`、`deadlineUtc` も検証する。二重起動の転送側はOSのpipe所有PID、応答PID、同一セッション、同一実行ファイルを照合する。

親の実プロセス試験で `expectedPid: null` の要求が応答せず切断されることを検出したため、任意PID・セッション・期限フィールドのJSON nullは未指定として扱うよう修正。PIDを省略・null指定した操作は切断ではなく `refused` として応答する。`requestId` と `path` のnullは空文字扱いとする。

管理停止はアクティブ・非アクティブの未保存文書、読込・保存・復元データ処理、ファイル引継ぎ、確認画面、ピッカー、描画、図形・選択範囲・オブジェクトの操作中に拒否する。強制終了は行わない。

## 旧設定の引継ぎ

既定のPPPaint設定先が未作成の場合だけ、旧 `%LOCALAPPDATA%\ChunPaint` から `preferences.json` と `Recovery` を一時フォルダーへコピーして採用する。旧フォルダーを移動・変更せず、通常のファイル・フォルダーのみ扱う。移行途中で読込に失敗した場合は未完了markerを残し、次回起動で再試行する。再試行前にPPPaint側へ作成された設定・復元ファイルは上書きしない。

`--settings-dir` または `PPPAINT_DATA_DIRECTORY` による分離起動では旧設定を移行しない。旧 `CHUNPAINT_DATA_DIRECTORY` は認識しない。AI接続設定・利用数・更新設定は自動コピー対象外。APIキーはWindows資格情報ストアを使い、保存先は `PPPaint.ImageEditing`、読込時だけ旧 `ChunPaint.ImageEditing` にフォールバックする。旧資格情報を削除せず、キーをJSONや検証ログへ出力しない。

## 検証

- [x] `LaunchOptionsTests` 5件: 既定起動、設定引数とファイル引数の分離、環境変数との優先度、識別子の正規化、常駐・不正引数の拒否。
- [x] `SettingsMigrationTests` 2件: 元データ保持・一度だけのコピー、ロックによる中断後の再試行と新設定保持。
- 実行: `.\.tools\dotnet\dotnet.exe test tests/PPPaint.Core.Tests/PPPaint.Core.Tests.csproj --artifacts-path artifacts/lifecycle-check --no-restore --filter 'FullyQualifiedName~LaunchOptionsTests|FullyQualifiedName~SettingsMigrationTests' -p:Platform=x64 -v:minimal`
- 結果: 7件成功、失敗0、スキップ0。制限付き環境ではtesthost IPCが進行しないため、同じ試験を昇格実行して完了を確認。パッケージの脆弱性データ取得はネットワーク制限によりNU1900警告。
- [ ] 実GUIでの起動・表示復元・二重起動・ファイル引継ぎ・正常終了は親の統合試験で確認する。
- [ ] 未保存・操作中の停止拒否は実GUI試験で確認する。

起動例外の診断は分離設定先の `startup-error.log`、WinUI未処理例外は `crash-diagnostic.txt` へ記録する。後者は秘密情報を含み得る例外本文を記録せず、型とソースパスを含まない呼出箇所に限定する。
