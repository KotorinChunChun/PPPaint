---
name: pppaint-development
description: PPPaintのWindowsスクリーンショット編集アプリを実装・修正・検証・配布するときに、操作契約、関連試験、配布と通常デスクトップ起動の共通手順を使う。他の画像編集アプリや画像そのものの加工には使わない。
---

# PPPaint開発

対象リポジトリは通常 `C:/develop/tool/PPPaint`。作業場所が違う場合は、ユーザーが指定したチェックアウトでPPPaint.slnxとtool.jsonを確認する。

- 最初に対象のAGENTS.mdとdev/reject.md、今回の要求・実装計画を読む。既存の変更を確認する。版やGitアカウントはAGENTS.mdを正とし、過去の作業報告の値を流用しない。
- 実装と試験の入口はdocs/development.md。ドラッグ・選択・枠線を変える場合はdev/interaction-contract.mdを読み、対応する既存のCore／Imaging試験を選ぶ。UIAと実キー・マウスの確認を分ける。
- コミット前の軽い検査はdev/validation/Test-CommitContent.ps1。フック導入はdev/Install-EditorHooks.ps1で、このリポジトリだけへ設定する。
- ビルドから配布・起動までの共通操作はdev/workflow.mdを読む。検証記録、配布ZIP、実行コミット、Defaultデスクトップの可視窓を対応づける。ソリューションbuildにPlatform=x64を付けない。個別アプリのpublishには付ける。
- 設定は既存先へ上書きせず、未保存の旧窓を閉じない。起動時の太さ初期化を繰り返さない。
- スキルはpushや公開の許可を自動で与えない。ユーザーが依頼した範囲で、指定アカウントの認証とリモートを確認して実行する。過去の認証失敗を現在の状態と決めつけない。
- 容量調査はdev/workflow.mdの保管・整理方針に従う。削除計画と実削除を区別する。

詳しい記録方法はdev/pppaint-development-procedure.md、利用者向けの現行操作はdocs/user-guide.mdを参照する。手順やスクリプトをこのスキルへ複製しない。
