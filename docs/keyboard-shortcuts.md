# キー割当と根拠

v0.2.5。ツール名の括弧内の文字に下線を付け、ツールチップとアクセシビリティ情報にもキーを表示する。ツールは文字キー単独とAlt+同じ文字で切り替えられる。文字・数値の入力欄では単独キーを実行しない。

## ツール

| 操作 | キー | 採用理由 |
|---|---|---|
| 選択・移動 | V | Photoshopの移動 |
| 矩形範囲 | M | Photoshopの矩形選択 |
| ペン | B | Photoshopのブラシ（自由描画） |
| 消しゴム | E | Photoshopの消しゴム |
| 矢印 | A | Greenshotの矢印 |
| 四角 | R | Greenshotの矩形 |
| 直線 | L | Greenshotの直線 |
| 文字 | T | PhotoshopとGreenshotの文字 |
| 蛍光ペン | H | Greenshotのハイライト |
| 丸 | O | Eは消しゴムで使うため、丸を連想しやすいOへ独自割当 |
| 番号 | N | Numberから独自割当 |
| 情報隠し | K | BlackoutのKを独自割当 |
| 矩形ぼかし | F | Filterから独自割当 |

V/M/B/E/Tの根拠は [Adobe公式ショートカット表](https://helpx.adobe.com/content/dam/help/en/photoshop/using/default-keyboard-shortcuts/photoshop-keyboard-shortcuts.pdf)、A/R/L/T/Hの根拠は [Greenshot公式ヘルプ](https://getgreenshot.org/help/)。複数製品の割当は衝突するため完全互換ではない。O/N/K/Fは一般標準と称さず、本アプリの固定割当として扱う。

## 共通コマンド

Ctrl+N（新規）、Ctrl+O（開く）、Ctrl+V（貼付）、Ctrl+C（コピー）、Ctrl+S（PNG保存）、Ctrl+Shift+S（名前を付けてPNG保存）、Ctrl+Z / Ctrl+Y（Undo/Redo）、Delete（削除）、Esc（取消・選択へ戻る）を維持する。

矩形範囲がある場合はCまたはEnterで切り抜く。GreenshotもCをトリミングに割り当てている。Alt+Gでその他、Alt+Pで設定、Alt+Iで色、Alt+Jで重なり、Alt+Wで全体表示、Alt+Dで原寸、Alt+Zで画像サイズを開く。これらのAlt文字割当はツールキーとの衝突を避ける本アプリの規則である。

## 修飾キーの根拠と検討範囲

Office公式は、ハンドルのShiftドラッグで比率固定、Ctrlドラッグで中心を固定、Ctrl+Shiftで両方を適用すると説明している。[Microsoft公式](https://support.microsoft.com/en-us/office/graphics-visuals/change-the-size-of-a-picture-shape-text-box-or-wordart)

Excel公式はCtrlドラッグによる複製と、Altでセルの格子へ合わせる操作を説明している。[Microsoft公式](https://support.microsoft.com/en-us/excel/group-copy-move-or-align-controls-on-a-worksheet)

本アプリにセルはないため、Altの位置合わせを採用する場合はキャンバスと既存図形への吸着へ置き換える。この置き換えは本アプリ向けの設計であり、Excelとの完全互換ではない。新しい修飾キーの対応範囲はユーザー回答待ち。実装済みのShiftによる比率・方向固定は維持している。
