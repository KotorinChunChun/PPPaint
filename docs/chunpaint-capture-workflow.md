# ChunPaint 開発画面キャプチャー運用

## 目的

ChunPaint の実画面を継続的に保存し、Windows 11 Paint とのUI比較、機能実装の進捗確認、回帰調査に利用する。Computer Useには依存せず、開発用キャプチャーサービスへメッセージを送って撮影する。

## 構成

- `dev/capture/Start-CaptureService.ps1`: 名前付きパイプ `ChunPaint.Capture` で撮影要求を待つ常駐サイドカー。
- `dev/capture/Send-CaptureRequest.ps1`: 目的、進捗、指示、対象プロセスをJSONメッセージとして送る。
- `dev/capture/Start-ChunPaintDevelopment.ps1`: サイドカーとChunPaintを併用起動し、起動直後の基準画面を自動保存する。
- `docs/chunpaint-captures/YYYY-MM-DD/`: PNG画像と同名のJSONメタデータを保存する。
- `docs/chunpaint-captures/capture-index.jsonl`: 全キャプチャーを検索できる追記型索引。

## 併用起動

```powershell
pwsh -NoProfile -File dev/capture/Start-ChunPaintDevelopment.ps1
```

既定ではChunPaintを最大化し、表示が安定してから起動直後の基準画面を自動保存する。最大化せずに現在のウィンドウ寸法で撮影したい場合は `-DoNotMaximize` を指定する。

再ビルドしてから起動する場合:

```powershell
pwsh -NoProfile -File dev/capture/Start-ChunPaintDevelopment.ps1 -Build
```

## 画面を撮影するメッセージ

起動スクリプトが返した `applicationProcessId` を指定する。

```powershell
pwsh -NoProfile -File dev/capture/Send-CaptureRequest.ps1 `
  -Purpose 'リボンの位置と高さをPaintと比較' `
  -Progress 'ui-ribbon-adjustment' `
  -Instruction '1920x1080、レイヤーパネル表示状態' `
  -ProcessId 12345
```

ファイル名の例:

```text
20260829_181530_245__ui-ribbon-adjustment__リボンの位置と高さをPaintと比較__1920x1080、レイヤーパネル表示状態.png
```

目的・進捗・指示はファイル名に安全な長さで含まれる。全文、撮影日時、ウィンドウ座標、対象プロセス、SHA-256は同名JSONに残る。

## 撮影方式

- 既定の `Screen` は、画面に実際に表示されている対象ウィンドウ範囲を撮影する。比較用途では対象ウィンドウを前面にし、ほかのウィンドウを重ねない。
- `PrintWindow` は対象ウィンドウへWindowsの描画要求を送り、ほかのウィンドウに隠れていても撮影できる場合がある。WinUIの合成状態によって黒画面になる場合があるため補助方式とする。

```powershell
pwsh -NoProfile -File dev/capture/Send-CaptureRequest.ps1 `
  -Purpose '非前面状態の描画確認' `
  -Progress 'capture-validation' `
  -CaptureMode PrintWindow
```

## サービス確認と終了

```powershell
pwsh -NoProfile -File dev/capture/Send-CaptureRequest.ps1 -Action Status
pwsh -NoProfile -File dev/capture/Send-CaptureRequest.ps1 -Action Stop
```

サービスは開発セッション中そのまま利用できる。終了要求は現在のユーザーが起動した同名パイプのサービスだけを停止する。

## C6品質ゲート

テスト、アプリのDebug|x64ビルド、通常ソリューションDebugビルド、`git diff --check`、既存キャプチャーのJSONL索引・同名メタデータ・SHA-256を一貫して検証する入口:

```powershell
pwsh -NoProfile -File dev/quality/Invoke-QualityGate.ps1
```

起動済みアプリのキャプチャーを品質ゲートへ含める場合:

```powershell
pwsh -NoProfile -File dev/quality/Invoke-QualityGate.ps1 `
  -Capture -CaptureProcessId 12345 -CaptureMode Screen
```

`-Capture`は既存のChunPaintプロセスとキャプチャーサービスを使用する。品質ゲートはネットワーク接続、壁紙設定、印刷確定、巨大なメモリ確保を行わず、プリンター／クリップボードなし等の実機依存は手動回帰として記録する。
