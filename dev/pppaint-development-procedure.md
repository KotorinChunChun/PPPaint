# PPPaint 連続バグ修正手順

## 目的

ユーザー報告を1件ずつ短いフェーズで完了させ、既存の変更・生成物・キャプチャー記録を巻き込まずに、同じ確認作業を繰り返さないための手順。GUIテスト不要の依頼では、Coreテストとコンパイルを検証の中心にする。

## 開始時チェック

- `AGENTS.md`、`v0.1.0-req.md`、`v0.1.0-imp.md`を先に読む。
- `git status --short`で、ユーザーの既存変更を把握する。
- `rg -n "キーワード" src tests dev`で関連実装・テスト・記録を一度に検索する。
- 依頼を「再現条件」「期待動作」「変更範囲」「検証方法」に分解し、1フェーズ1不具合にする。

## 実装フェーズ

1. 関連するイベント入口、状態、計算処理、描画処理をまとめて読む。
2. Coreに置ける計算・変換はCoreへ置き、UIは入力状態の変換と呼び出しに限定する。
3. 既存挙動を保つデフォルト引数または明示的な分岐を選び、最小差分を`apply_patch`で適用する。
4. GUIを起動せず確認できる不変条件をCoreテストへ追加する。

## 検証の順番

対象がCoreだけなら、まず対象クラスのテストを絞って実行し、その後に全Coreテストを実行する。

```powershell
dotnet test tests/PPPaint.Core.Tests/PPPaint.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~対象テスト"
dotnet test tests/PPPaint.Core.Tests/PPPaint.Core.Tests.csproj --no-restore
dotnet build src/PPPaint/PPPaint.csproj --no-restore --arch x64
git diff --check
```

既存の`obj`が書き込み拒否になる場合は、同じコマンドを繰り返さず、承認を得て通常出力先を使う。起動中アプリがDLLをロックしている場合は終了させず、一時出力先へビルドする。

```powershell
$pppaintBuildOutput = Join-Path ([IO.Path]::GetTempPath()) 'PPPaint-build'
$env:DefaultItemExcludes = '**/obj/**;**/bin/**;**/artifacts/**'
dotnet build src/PPPaint/PPPaint.csproj --no-restore -p:OutDir="$pppaintBuildOutput\"
```

`BaseOutputPath=artifacts/...`の反復利用は、生成されたファイルを次回ビルドの入力へ取り込ませ、重複コンパイルやパス長超過を起こしやすいので避ける。

## コミット前チェック

- `v0.1.0-req.md`に新しい受入条件を記録する。
- `v0.1.0-imp.md`に実施内容、テスト件数、ビルド結果、GUIテスト未実施を日本語で記録する。
- `git add -- <対象ファイル>`で対象を明示する。
- `git diff --cached --check`と`git status --short`で、キャプチャー記録など無関係な変更が含まれていないことを確認する。
- フェーズ完了ごとに日本語コミットを作り、次の不具合へ進む。
