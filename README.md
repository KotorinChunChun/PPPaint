# ChunPaint

ChunPaint は、Windows 11 の「ペイント」で慣れた操作感を維持しつつ、独自機能を追加できる Windows 向けペイントソフトです。

Microsoft Paint のソース、DLL、アイコン、画像などは取り込まず、公開された挙動と実機観察を基に独自実装します。

## 開発画面の記録

ChunPaintとメッセージ駆動のキャプチャーサービスを併用起動する。

```powershell
pwsh -NoProfile -File dev/capture/Start-ChunPaintDevelopment.ps1
```

起動直後の最大化画面が自動保存される。追加撮影、メタデータ、サービス終了方法は [開発画面キャプチャー運用](docs/chunpaint-capture-workflow.md) を参照する。
