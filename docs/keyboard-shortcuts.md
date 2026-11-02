# キー割当と根拠

対象はPPPaint v0.2.33です。操作の流れは[使い方](user-guide.md)を参照してください。

ツール名は括弧内に下線付きのキーを表示します。ツールバー1行目はキャプションにキーを付けず、ツールチップに表示します。文字・数値入力中は単独文字によるツール切替を行いません。ダイアログを開いている間は、背後の編集操作へキーを渡しません。

## ツール

| ツール | キー | 採用理由 |
|---|---|---|
| 選択・移動 | V | Photoshopの移動 |
| 矩形範囲 | M | Photoshopの矩形選択 |
| ペン | B | Photoshopのブラシ・鉛筆 |
| 消しゴム | E | Photoshopの消しゴム |
| スポイト | I | Photoshopのスポイト |
| 矢印 | A | Greenshotの矢印 |
| 四角 | R | Greenshotの矩形 |
| 丸 | O | Eとの衝突を避けた独自割当 |
| 直線 | L | Greenshotの直線 |
| 文字 | T | Photoshop・Greenshotの文字 |
| 番号 | N | Numberから独自割当 |
| 情報隠し | K | BlackoutのKから独自割当 |
| 蛍光ペン | H | Greenshotのハイライト |
| ぼかし | F | Filterから独自割当 |
| 吹き出し | Q | Quoteから独自割当 |
| モザイク | X | PixelのXから独自割当 |

スポイトとぼかし以外は、Alt+同じ文字でも切り替えられます。**Alt+Iは色編集、Alt+Fはファイルメニュー**です。スポイトはIで開始・解除し、キャンバスをクリックすると合成色を取得して解除します。Escでも解除できます。

V/M/B/E/Tは[Adobe公式ショートカット表](https://helpx.adobe.com/content/dam/help/en/photoshop/using/default-keyboard-shortcuts/photoshop-keyboard-shortcuts.pdf)、Iは[Adobe公式Web版ショートカット表](https://helpx.adobe.com/ph_fil/photoshop/web/get-set-up/preferences-and-settings/keyboard-shortcuts.html)、A/R/L/T/Hは[Greenshot公式ヘルプ](https://getgreenshot.org/help/)を採用根拠としています。製品間で衝突するキーは独自に割り当てており、完全互換ではありません。

## ファイルと編集

以下はキャンバス操作時のキーです。入力欄のCtrl+A/C/X/Vなどは文字編集を優先します。

| 操作 | キー | 対象・補足 |
|---|---|---|
| 新規 | Ctrl+N | 別ウィンドウ |
| 開く | Ctrl+O | 編集中の文書があれば別ウィンドウ |
| 保存 | Ctrl+S | PNG/JPEG |
| 名前を付けて保存 | Ctrl+Shift+S | 保存先とファイル名を指定 |
| 重ねて貼り付け | Ctrl+V | キャンバスを縮小しない |
| 貼り付け方法 | Ctrl+Shift+V | 重ねる／上／下／左／右から選択 |
| コピー | Ctrl+C | 図形選択のみなら選択対象の外周矩形。矩形範囲あり・無選択ならキャンバス全体 |
| 切り取り | Ctrl+X | 矩形範囲または選択対象をコピーして削除。コピー失敗時は削除しない |
| 全体の範囲選択 | Ctrl+A | キャンバスと同じ大きさの矩形範囲 |
| 切り抜き | Ctrl+Shift+X／C／Enter | 矩形範囲を優先。なければ選択図形すべての外周でキャンバスを切り抜く |
| 削除 | Delete | 矩形範囲があれば範囲削除、なければ選択対象を削除 |
| 選択を画像に統合 | Ctrl+E | 透過背景の1枚にする。Undoで元の図形へ戻せる |
| 元に戻す／やり直す | Ctrl+Z／Ctrl+Y | 文書の編集履歴 |
| 取消・選択解除 | Esc | ドラッグ中なら操作を取消。通常時は選択解除して選択・移動へ戻る |
| 1px移動／10px移動 | 矢印／Shift+矢印 | 選択した図形・画像 |
| 1つ前へ／1つ後ろへ | Ctrl+PageUp／Ctrl+PageDown | 選択対象の重なり順 |
| 最前面へ／最背面へ | Ctrl+Shift+PageUp／Ctrl+Shift+PageDown | 選択対象の重なり順 |

矩形範囲があるとCtrl+Cは全体コピーですが、Ctrl+Xはその範囲の切り取りになります。図形だけをCtrl+Xで切り取る場合は、現行版ではキャンバス寸法でコピーします。Ctrl+Cの外周矩形コピーとは区別してください。

画像のないクリップボードではCtrl+V／Ctrl+Shift+Vは何も実行せず、貼り付けボタンと▼も無効になります。

Ctrl+Eは[Adobe公式の選択レイヤー統合](https://helpx.adobe.com/photoshop/desktop/create-manage-layers/get-started-layers/merge-and-stamp-layers.html)に合わせました。Cによる切り抜きは[Greenshot公式ヘルプ](https://getgreenshot.org/help/)も採用しています。

## メニュー・表示・作図設定

| 操作 | キー |
|---|---|
| ファイル▼ | Alt+F |
| その他▼ | Alt+G |
| 重なり▼ | Alt+J |
| 設定 | Alt+P |
| 色を編集 | Alt+I |
| 選択した図形の設定を新規作図設定へ取得 | Alt+Y |
| キャンバスサイズ | Alt+Z |
| キャンバス背景色 | Alt+U |
| 全体表示の切替 | Alt+W |
| 100%表示 | Alt+D |
| ポインタ位置を基準に表示倍率を変更 | Ctrl+ホイール |
| 表示位置を移動 | Space+ドラッグ／中ボタンドラッグ |

Alt文字は本アプリの固定割当です。全体表示は表示領域の変化へ追従し、再度押すか手動で倍率を指定すると解除します。表示倍率は10～400%で、画像の画素寸法を変えません。

注釈文字欄のEscは入力内容と選択を保持し、キャンバスへフォーカスを戻します。IME変換中は変換操作を優先します。

## 図形と矩形範囲の修飾キー

| 操作対象 | 修飾キー | 動作 |
|---|---|---|
| 選択 | Ctrl／Shift+クリック | 選択を追加・解除。クリックだけでは複製しない |
| 図形・画像の移動 | Ctrl+ドラッグ | 複製。複数選択は相対位置と順番を保持 |
| 移動 | Shift+ドラッグ | 横または縦の大きい移動方向へ固定 |
| 矩形・範囲の新規描画 | Shift | 正方形に固定 |
| 矩形・範囲の新規描画 | Ctrl | 開始位置を中心に描く |
| 線・矢印の描画／端点変更 | Shift | 45度刻みへ方向を固定 |
| 選択枠の8方向ハンドル | Shift | 元の縦横比を固定 |
| 選択枠の8方向ハンドル | Ctrl | 元の中心を固定 |
| 描画・サイズ変更 | Ctrl+Shift | 中心基準と比率・方向固定を併用 |
| 移動・描画・サイズ変更 | Alt | キャンバス／非選択オブジェクトの辺・中心へ吸着し、ガイド表示 |

Ctrl複製はマウスを離す時点でもCtrlを保持します。修飾キーの押下・解放はドラッグ中のプレビューへ反映します。複合時はShiftの制約を優先し、成立した吸着だけを表示します。図形だけなら小数座標の辺・中心にも吸着します。画像を含む選択や矩形範囲の移動では整数画素を優先するため、端数の位置には吸着しない場合があります。移動・複製・サイズ変更は離すと1操作として確定し、Escまたはキャプチャ喪失で元へ戻ります。

Shiftの比率固定・Ctrlの中心固定・Ctrl+Shift併用は[Microsoft公式のサイズ変更手順](https://support.microsoft.com/en-us/office/graphics-visuals/change-the-size-of-a-picture-shape-text-box-or-wordart)に合わせています。Ctrlドラッグ複製とAlt位置合わせは[Microsoft公式のExcel操作](https://support.microsoft.com/en-us/excel/group-copy-move-or-align-controls-on-a-worksheet)を参考にしています。本アプリではセル格子の代わりにキャンバス・既存図形へ吸着します。

## キャンバス境界とサイズ画面

本体キャンバスの四辺・四隅はドラッグで余白追加・切り詰めを行います。Ctrlで対角側も同量変更します。本体外枠のドラッグではShiftによる比率固定は行いません。

Alt+Zで開くサイズ画面では、以下のキーを使えます。

| 操作 | キー | 初期状態・補足 |
|---|---|---|
| 幅／高さへ移動 | Alt+W／Alt+H | 1px単位の整数 |
| 縦横比を固定 | Alt+R | OFF |
| 両端を連動 | Alt+B | ON。中心を固定して増減 |
| プレビュー倍率へ移動 | Alt+V | 1倍。100×100の固定枠に映す画像範囲を変更 |
| リセット | Alt+E | 未適用の寸法・位置・倍率・オプションを開始時へ戻す。画面は閉じない |
| 適用 | Alt+S | 文書へ確定 |
| キャンセル | Esc／Alt+C | 文書を変更せず閉じる |

8方位の青い境界もドラッグできます。プレビューのドラッグ中は、Ctrlまたは「両端を連動」で中心固定、Shiftまたは「縦横比を固定」で比率固定になります。倍率を変えても、本体キャンバスの画素寸法は変わりません。

## ダイアログとメニュー

Tab／Shift+Tabで項目を移動します。Enterは画面の既定操作、Escは取消です。入力欄や一覧に固有のEnter操作がある場合はそちらを優先します。

| 画面 | 操作 | キー |
|---|---|---|
| 共通 | 保存・適用などの主操作 | Alt+S |
| 共通 | 取消・閉じる | Esc／Alt+C |
| 未保存確認 | 保存せずに閉じる | N／Alt+N |
| 未保存・上書き確認 | 既定の取消 | Enter |
| その他の副操作 | 表示された副ボタン | Alt+D（サイズ画面のリセットはAlt+E） |
| ファイル | フォルダー／ファイル名 | Alt+L／Alt+N |
| ファイル | 上位フォルダー | Alt+U／Alt+↑ |
| ファイル一覧 | 選択／開く・確定 | 矢印／Enter |
| フォルダー入力欄 | 入力した場所へ移動 | Enter |
| 色編集 | RGBの16進数欄 | Alt+H |
| 背景色 | 透明切替／白に戻す | Alt+T／Alt+W |
| 設定 | 描画設定の初期化 | Alt+R |
| 貼り付け方法 | 選択／実行／取消 | 上下／Enter／Esc |

未保存・上書き・メッセージの確認画面だけは、表示されたS/N/D/Cの単独文字でも操作できます。ファイル名・色・寸法などの入力画面では単独文字を奪いません。未保存・上書き確認の既定は取消で、Enterだけで破棄や上書きを実行しません。

Enterの既定操作と入力欄の扱いは[Microsoftのダイアログ仕様](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/dialogs)、Altの範囲分けは[アクセスキー仕様](https://learn.microsoft.com/en-us/windows/apps/develop/input/access-keys)を参照しています。各ボタンの文字割当は本アプリの規則です。
