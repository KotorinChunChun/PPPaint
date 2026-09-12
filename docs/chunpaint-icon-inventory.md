# ChunPaint UIアイコン生成・照合台帳

調査日: 2026-09-08。対象: 現行のsrc/ChunPaintのXAMLと動的UI生成コード。実画面の全経路巡回ではなく、ソースに基づく棚卸し。初回棚卸し時点では画像生成・UI差し替え未実施。v0.2.0の現状は末尾の追記を参照。

## 制作数の見取り図

| 区分 | 機能・状態の行数 | 扱い |
|---|---:|---|
| A 一般アイコン | 46 | 生成対象 |
| B ブラシの種類 | 9 | 生成対象。現行メニューは文字のみ |
| D 図形 | 23 | 生成対象。大きい32 DIP表示版は不要 |
| E 輪郭・塗り | 14 | 生成対象。状態ごとに照合 |
| **生成対象合計** | **92** | 共有前の機能・状態数。PNGファイル数ではない |
| C 文字だけの追加候補 | 42 | アイコン化を採用した場合に追加 |
| F/G/H/U | 55 | 動的描画、ダイアログ、ブランド・カーソル、標準部品の維持確認 |
| X 静的配置 | 132 | 上記と重複する使用箇所の照合表。原画数に加算しない |

原画共有や全サイズの書き出しによって実ファイル数は変わる。生成済みファイル数だけで完了判定せず、92行の各チェックと共有先で判定する。

## サイズと生成仕様

- 通常アイコン: 表示用16／24／32を基本とする。32×32を通常の大きい表示用サイズとして用意する。
- XAMLのWidth/Heightは論理サイズ（DIP）。32 DIPの200%表示用には64×64のPNGが必要。通常は16/20/24/30/32/40/48/60/64の書き出しを用意すると、16/24/32 DIPの100/125/150/200%に対応できる。30は独立した表示仕様でなく、24 DIPの125%用。
- AIには高解像度の正方形原画を生成させ、最終サイズに縮小して調整する。32pxファイルだけを拡大して全サイズへ流用しない。可能なら後工程でベクター原稿を整える。
- 図形23種: 16 DIPを基本（現状ボタン28×22、文字サイズ16）。32 DIPの大きい表示版は不要。ただし200%表示用の32pxファイルは必要。16/20/24/32pxを用意する。
- 輪郭／塗りの見本は現状12／19、線幅見本22。図形そのものとは別で、通常16/24/32の制作枠に含める。細い見本は実表示サイズでも個別確認する。
- 例外: ブラシ総称は現状15、レイヤー表示は幅16・文字10、ロゴ28、レイヤー総称の描画領域42。32素材を置くだけで現在の配置へ収まるとは限らない。レイヤー総称を現状サイズで維持するなら48 DIP用まで追加する。
- 色は前回プロンプトの青灰／ピンク／水色／黄／ミント。輪郭とシルエットを統一し、図形は幾何学的な正確さを優先。文字ツールA、B/I/U/Sは機能記号なので「文字禁止」の例外。一般アイコンの星・ハート禁止も図形の星／ハートには適用しない。

## 管理方法

固定IDはカテゴリ接頭辞＋英語名。ファイル名は例: A-save-16.png、A-save-24.png、A-save-32.png、A-save-64.png。原画はA-save-master.png。保存先案はassets/icons/pop/。現時点では未作成。

各行の「生成／組込／実寸」は個別チェック。生成完了だけで組込や実寸確認を完了にしない。同じアイコンを共有する場合は別原画を作らず、共有先IDと使用箇所の確認記録を残す。B/Cの機能追加アイコンは現行表示と区別する。

## A 現在表示される一般アイコン（生成対象）

生成対象。ただしBは機能別素材を用意する計画であり、現行ブラシメニューにアイコン表示があるという意味ではない。

| ID | 機能 | 使用箇所・識別子 | モチーフ／現状 | 生成 | 組込 | 実寸 |
|---|---|---|---|---|---|---|
| A-save | 保存 | 上部／ファイルメニュー | フロッピー型。現状22 | [x] | [ ] | [ ] |
| A-share | 共有 | 上部 | 上へ出る共有矢印。現状22 | [x] | [ ] | [ ] |
| A-undo | 元に戻す | 上部／編集／共通 | 左へ戻る曲がった矢印。現状文字24 | [x] | [ ] | [ ] |
| A-redo | やり直す | 上部／編集／共通 | 右へ戻る曲がった矢印。現状文字24 | [x] | [ ] | [ ] |
| A-display-mode | アイコン／名前表示切替 | 上部 | 小さな図記号と文字行。現状A▣・14 | [x] | [ ] | [ ] |
| A-zoom-out | ズームアウト | 上部 | 虫眼鏡にマイナス | [x] | [ ] | [ ] |
| A-zoom-in | ズームイン | 上部 | 虫眼鏡にプラス | [x] | [ ] | [ ] |
| A-feedback | フィードバック | 上部 | 短い線を入れた吹き出し。現状⌁・21 | [x] | [ ] | [ ] |
| A-account | アカウント | 上部 | 丸い人物。現状♙・18 | [x] | [ ] | [ ] |
| A-settings | 設定 | 上部 | 歯車。現状⚙・22 | [x] | [ ] | [ ] |
| A-select-rectangle | 矩形選択 | 選択リボン／編集 | 破線の矩形。現状□ | [x] | [ ] | [ ] |
| A-select-free | 自由選択 | 選択リボン／編集 | 閉じた自由曲線・投げ縄。現状⌁ | [x] | [ ] | [ ] |
| A-select-all | すべて選択 | 選択リボン／編集 | キャンバス全体を囲む選択枠。現状▣ | [x] | [ ] | [ ] |
| A-cancel | 取消・閉じる | 選択取消／レイヤーパネル閉じる | 共通の×。選択解除とパネル閉じるは使用箇所別に確認 | [x] | [ ] | [ ] |
| A-select-transparent | 透明な選択 | 選択リボン／編集 | 選択枠と透ける領域。機能表現としての市松は可 | [x] | [ ] | [ ] |
| A-delete | 削除 | 選択／編集／キャンバス右クリック／レイヤー右クリック | ごみ箱。現状選択ボタンは⌫ | [x] | [ ] | [ ] |
| A-crop | トリミング | 画像リボン／キャンバス右クリック | 交差するトリミング枠 | [x] | [ ] | [ ] |
| A-resize | サイズ変更 | 画像リボン | 矩形と対角矢印。現状PNG24 | [x] | [ ] | [ ] |
| A-import | キャンバスにインポート | 画像リボン／ファイルメニュー | 画像へ入る矢印。現状▧ | [x] | [ ] | [ ] |
| A-background-remove | 背景の削除 | 画像リボン | 前景が残り背景が抜ける図。処理中は再クリックで取消 | [x] | [ ] | [ ] |
| A-rotate | 回転メニュー本体 | 画像リボン | 図形と回転矢印。現状◢。下向き開閉矢印はU参照 | [x] | [ ] | [ ] |
| A-pencil | 鉛筆 | ツールリボンの複合ボタン | 芯先のある鉛筆。現状✎ | [x] | [ ] | [ ] |
| A-brush | ブラシ総称 | 鉛筆・ブラシの複合ボタン | 毛先のある筆。現状PNG15。15での確認必須 | [x] | [ ] | [ ] |
| A-bucket-fill | 塗りつぶしツール | ツールリボン | 傾いたバケツと一滴。現状PNG24 | [x] | [ ] | [ ] |
| A-text | テキストツール | ツールリボン | 明快なA。機能記号として文字を許可 | [x] | [ ] | [ ] |
| A-eraser | 消しゴム | ツールリボン | 丸みのある消しゴム。現状PNG24 | [x] | [ ] | [ ] |
| A-eyedropper | スポイト | ツールリボン | ゴムと採取管。現状⚕ | [x] | [ ] | [ ] |
| A-magnifier | 拡大鏡ツール | ツールリボン | 記号なしの虫眼鏡。左右クリックで拡大／縮小 | [x] | [ ] | [ ] |
| A-edit-colors | 色の編集 | 色パレットフライアウト | 色相環／パレット。現状🌈・18 | [x] | [ ] | [ ] |
| A-layers | レイヤーパネル開閉 | リボン右端 | 重なった面。現状Canvas42・内部Path幅36。32へ収める案、現状維持なら48表示版を追加 | [x] | [ ] | [ ] |
| A-layer-add | レイヤー追加 | レイヤーパネル | 重なる面と＋。現状＋ 追加 | [x] | [ ] | [ ] |
| A-layer-up | レイヤーを上へ | レイヤーパネル／右クリック | 面と上矢印。現状↑・20 | [x] | [ ] | [ ] |
| A-layer-down | レイヤーを下へ | レイヤーパネル／右クリック | 面と下矢印。現状↓・20 | [x] | [ ] | [ ] |
| A-layer-merge | 選択レイヤーを結合 | レイヤーパネル／右クリック | 複数の面が一枚へ集まる図。現状⇲・20 | [x] | [ ] | [ ] |
| A-layer-rename | レイヤー名変更 | レイヤーパネル／右クリック | 名札と鉛筆。現状✎・20 | [x] | [ ] | [ ] |
| A-layer-visible | レイヤー表示中 | レイヤー行／右クリックの非表示操作 | 開いた目。現状●・10、幅16の列 | [x] | [ ] | [ ] |
| A-layer-hidden | レイヤー非表示中 | レイヤー行／右クリックの表示操作 | 斜線入りの目。現状○・10、幅16の列 | [x] | [ ] | [ ] |
| A-stroke-width | 線幅 | 左側スライダー見出し／太さボタンの候補 | 太さの異なる線。現状左≋・20。リボンは動的見本も併用 | [x] | [ ] | [ ] |
| A-opacity | 不透明度 | 左側スライダー見出し | 半分が薄い円。現状◕・20 | [x] | [ ] | [ ] |
| A-pointer-position | ポインター座標 | ステータスバー | 座標十字。現状⌁・19 | [x] | [ ] | [ ] |
| A-canvas-size | キャンバス寸法 | ステータスバー | 画像枠と寸法線。現状▧・18 | [x] | [ ] | [ ] |
| A-zoom-reset | ズーム100%へ戻す | ステータスバー／表示メニュー | 虫眼鏡と等倍を示す簡潔な記号。現状⌾ | [x] | [ ] | [ ] |
| A-text-bold | 太字 | 文字ダイアログ | 太字B。文字を許可、正確なフォント／ベクター仕上げ可 | [x] | [ ] | [ ] |
| A-text-italic | 斜体 | 文字ダイアログ | 斜体I。文字を許可 | [x] | [ ] | [ ] |
| A-text-underline | 下線 | 文字ダイアログ | Uと下線。文字を許可 | [x] | [ ] | [ ] |
| A-text-strike | 取り消し線 | 文字ダイアログ | Sと中央線。文字を許可 | [x] | [ ] | [ ] |

## B ブラシ9種類（機能別生成対象・現状メニューは文字のみ）

生成対象。ただしBは機能別素材を用意する計画であり、現行ブラシメニューにアイコン表示があるという意味ではない。

| ID | 機能 | 使用箇所・識別子 | モチーフ／現状 | 生成 | 組込 | 実寸 |
|---|---|---|---|---|---|---|
| B-brush-oil | 油彩ブラシ | Oil | 丸い厚めの毛先 | [x] | [ ] | [ ] |
| B-brush-watercolor | 水彩ブラシ | Watercolor | 尖った柔らかい毛先と一滴 | [x] | [ ] | [ ] |
| B-brush-marker | マーカー | Marker | 太い角芯 | [x] | [ ] | [ ] |
| B-brush-natural-pencil | 鉛筆風ブラシ | NaturalPencil | 芯のある鉛筆。A-pencilと共有可、機能行は残す | [x] | [ ] | [ ] |
| B-brush-airbrush | エアブラシ | Airbrush | ノズルと少数の大きな噴霧点 | [x] | [ ] | [ ] |
| B-brush-calligraphy | カリグラフィー | Calligraphy | 斜めの幅広ペン先 | [x] | [ ] | [ ] |
| B-brush-crayon | クレヨン | Crayon | 短く太い蝋筆 | [x] | [ ] | [ ] |
| B-brush-flat | 平筆 | Flat | 横に広い四角い毛先 | [x] | [ ] | [ ] |
| B-brush-pixel | ピクセルペン | Pixel | 四角い先端と大きな画素1～2個 | [x] | [ ] | [ ] |

## C 文字のみの機能（将来アイコン化する場合の追加候補）

現在は文字のみ。全ボタン棚卸しとして記載し、生成漏れの必須判定からは除く。アイコン化を採用した行を生成対象へ変更する。

| ID | 機能 | 使用箇所・識別子 | モチーフ／現状 | 生成 | 組込 | 実寸 |
|---|---|---|---|---|---|---|
| C-new | 新規 | ファイル | 白紙と＋ | [ ] | [ ] | [ ] |
| C-open | 開く | ファイル | 開いたフォルダー | [ ] | [ ] | [ ] |
| C-recent | 最近使ったファイル | ファイル／可変履歴 | ファイルと時計。各履歴は共通アイコン | [ ] | [ ] | [ ] |
| C-save-as | 名前を付けて保存 | ファイル | 保存と鉛筆 | [ ] | [ ] | [ ] |
| C-save-project | プロジェクト保存 | Ctrl+Shift+S／保存形式ChunPaint | 重なる面と保存記号。2箇所で共有 | [ ] | [ ] | [ ] |
| C-format-png | PNG画像として保存 | 保存形式 | 画像ファイル。拡張子はUI文字を維持 | [ ] | [ ] | [ ] |
| C-format-jpeg | JPEG画像として保存 | 保存形式 | 画像ファイル。共通画像ファイル素材を共有可 | [ ] | [ ] | [ ] |
| C-format-bmp | BMP画像として保存 | 保存形式 | 画像ファイル。共通素材共有可 | [ ] | [ ] | [ ] |
| C-format-gif | GIF画像として保存 | 保存形式 | 画像ファイル。共通素材共有可 | [ ] | [ ] | [ ] |
| C-print | 印刷 | ファイル／OS印刷画面起動 | プリンター | [ ] | [ ] | [ ] |
| C-wallpaper | 壁紙設定 | ファイル・親メニュー | モニターと画像 | [ ] | [ ] | [ ] |
| C-wallpaper-fill | 壁紙・画面に合わせる | 壁紙サブメニュー | 画面を埋める画像 | [ ] | [ ] | [ ] |
| C-wallpaper-tile | 壁紙・並べて表示 | 壁紙サブメニュー | 複数のタイル | [ ] | [ ] | [ ] |
| C-wallpaper-center | 壁紙・中央に表示 | 壁紙サブメニュー | 画面中央の画像 | [ ] | [ ] | [ ] |
| C-properties | 画像のプロパティ | ファイル | 画像と情報記号 | [ ] | [ ] | [ ] |
| C-exit | 終了 | ファイル | 出口矢印 | [ ] | [ ] | [ ] |
| C-select-invert | 選択範囲反転 | 編集→選択 | 内外の選択を入れ替える図 | [ ] | [ ] | [ ] |
| C-apply | 選択／オブジェクト適用・設定適用・OK | 編集／ダイアログ | 共通チェック。使用箇所ごとに確認 | [ ] | [ ] | [ ] |
| C-object-cancel | オブジェクト取消 | 編集 | A-cancel共有 | [ ] | [ ] | [ ] |
| C-cut | 切り取り | 編集／キャンバス右クリック | はさみ | [ ] | [ ] | [ ] |
| C-copy | コピー | 編集／キャンバス右クリック | 重なる紙 | [ ] | [ ] | [ ] |
| C-copy-visible | 表示レイヤーをコピー | 編集／キャンバス右クリック | 重なる面と紙 | [ ] | [ ] | [ ] |
| C-paste | 貼り付け | 編集／キャンバス右クリック | クリップボード | [ ] | [ ] | [ ] |
| C-invert-colors | 色の反転 | 編集／キャンバス右クリック | 白黒の入れ替わる円 | [ ] | [ ] | [ ] |
| C-ruler | ルーラー表示 | 表示／設定 | 定規 | [ ] | [ ] | [ ] |
| C-grid | グリッド線表示 | 表示／設定 | 格子 | [ ] | [ ] | [ ] |
| C-statusbar | ステータスバー表示 | 表示／設定 | ウィンドウ下端を強調 | [ ] | [ ] | [ ] |
| C-fullscreen | 全画面表示 | 表示 | 四隅へ向く矢印。解除は状態別に確認 | [ ] | [ ] | [ ] |
| C-layer-duplicate | レイヤー複製 | レイヤー右クリック | 重なる面と複製記号 | [ ] | [ ] | [ ] |
| C-rotate-right | 右へ90度回転 | 回転メニュー Clockwise | 時計回り矢印 | [ ] | [ ] | [ ] |
| C-rotate-left | 左へ90度回転 | 回転メニュー CounterClockwise | 反時計回り矢印 | [ ] | [ ] | [ ] |
| C-rotate-180 | 180度回転 | 回転メニュー Rotate180 | 半周の矢印 | [ ] | [ ] | [ ] |
| C-flip-horizontal | 左右反転 | 回転メニュー FlipHorizontal | 縦軸と左右対称の図 | [ ] | [ ] | [ ] |
| C-flip-vertical | 上下反転 | 回転メニュー FlipVertical | 横軸と上下対称の図 | [ ] | [ ] | [ ] |
| C-rotate-custom | 任意角度回転 | 回転メニュー CustomRotate | 回転矢印と角度弧 | [ ] | [ ] | [ ] |
| C-skew-horizontal | 水平傾斜 | 回転メニュー SkewHorizontal | 横方向に傾く矩形 | [ ] | [ ] | [ ] |
| C-skew-vertical | 垂直傾斜 | 回転メニュー SkewVertical | 縦方向に傾く矩形 | [ ] | [ ] | [ ] |
| C-text-background | 文字の背景塗り | 文字ダイアログ 背景 | 背景付きA。現在文字ボタン | [ ] | [ ] | [ ] |
| C-align-left | 文字左揃え | 文字ダイアログ 配置 | 左端を揃えた線 | [ ] | [ ] | [ ] |
| C-align-center | 文字中央揃え | 文字ダイアログ 配置 | 中央を揃えた線 | [ ] | [ ] | [ ] |
| C-align-right | 文字右揃え | 文字ダイアログ 配置 | 右端を揃えた線 | [ ] | [ ] | [ ] |
| C-aspect-lock | 縦横比維持 | サイズ変更ダイアログ | 連結した鎖。現在チェックボックス | [ ] | [ ] | [ ] |

## D 図形23種類（生成対象・16 DIP基準）

MainWindow.xamlのShapeButton_Clickに接続された全23ボタンを対象とする。Tagと一対一対応し、似た図形も勝手に統合しない。EllipseとOvalは別ボタンのため別行。原画を共有する場合も2箇所を検証する。

| ID | UI名 | Tag | 生成 | 組込 | 実寸 |
|---|---|---|---|---|---|
| D-Line | 直線 | Line | [x] | [ ] | [ ] |
| D-Curve | 曲線 | Curve | [x] | [ ] | [ ] |
| D-Ellipse | 楕円 | Ellipse | [x] | [ ] | [ ] |
| D-Rectangle | 四角形 | Rectangle | [x] | [ ] | [ ] |
| D-RoundedRectangle | 角丸四角形 | RoundedRectangle | [x] | [ ] | [ ] |
| D-Parallelogram | 平行四辺形 | Parallelogram | [x] | [ ] | [ ] |
| D-Triangle | 三角形 | Triangle | [x] | [ ] | [ ] |
| D-RightTriangle | 直角三角形 | RightTriangle | [x] | [ ] | [ ] |
| D-Diamond | ひし形 | Diamond | [x] | [ ] | [ ] |
| D-Pentagon | 五角形 | Pentagon | [x] | [ ] | [ ] |
| D-Hexagon | 六角形 | Hexagon | [x] | [ ] | [ ] |
| D-RightArrow | 右矢印 | RightArrow | [x] | [ ] | [ ] |
| D-LeftArrow | 左矢印 | LeftArrow | [x] | [ ] | [ ] |
| D-UpArrow | 上矢印 | UpArrow | [x] | [ ] | [ ] |
| D-DownArrow | 下矢印 | DownArrow | [x] | [ ] | [ ] |
| D-FourPointStar | 四角星 | FourPointStar | [x] | [ ] | [ ] |
| D-Star | 星 | Star | [x] | [ ] | [ ] |
| D-FilledStar | 塗りつぶし星形 | FilledStar | [x] | [ ] | [ ] |
| D-Callout | 吹き出し | Callout | [x] | [ ] | [ ] |
| D-Oval | 楕円形 | Oval | [x] | [ ] | [ ] |
| D-Cloud | 雲 | Cloud | [x] | [ ] | [ ] |
| D-Heart | ハート | Heart | [x] | [ ] | [ ] |
| D-Lightning | 稲妻 | Lightning | [x] | [ ] | [ ] |

## E 輪郭・塗りの状態見本（生成対象・14種類）

MainWindow.Shapes.csのOutlineStyleButton_Click／FillStyleButton_Click／UpdateShapeStyleVisuals。選択中の種類が大ボタン・小記号へ動的に反映される。道具の絵だけでなく、輪郭と面の違いを見分けられるプレビューとする。微細な質感をそのまま縮小せず大きい線・塊へ簡略化する。

| ID | 機能・状態 | モチーフ | 生成 | 組込 | 実寸 |
|---|---|---|---|---|---|
| E-outline-none | 輪郭・なし | 枠線／斜線／無効な枠 | [x] | [ ] | [ ] |
| E-outline-solid | 輪郭・実線（ペン） | 枠線／一様で明快 | [x] | [ ] | [ ] |
| E-outline-crayon | 輪郭・クレヨン | 枠線／粗めの少数の凹凸 | [x] | [ ] | [ ] |
| E-outline-marker | 輪郭・マーカー | 枠線／幅広で均一 | [x] | [ ] | [ ] |
| E-outline-oil | 輪郭・油彩ブラシ | 枠線／厚い短い筋 | [x] | [ ] | [ ] |
| E-outline-pencil | 輪郭・鉛筆（通常） | 枠線／細めだが消えない線 | [x] | [ ] | [ ] |
| E-outline-watercolor | 輪郭・水彩ブラシ | 枠線／半透明の広い面 | [x] | [ ] | [ ] |
| E-fill-none | 塗り・なし | 塗られた面／斜線／無効な枠 | [x] | [ ] | [ ] |
| E-fill-solid | 塗り・単色（ペンキ） | 塗られた面／一様で明快 | [x] | [ ] | [ ] |
| E-fill-crayon | 塗り・クレヨン | 塗られた面／粗めの少数の凹凸 | [x] | [ ] | [ ] |
| E-fill-marker | 塗り・マーカー | 塗られた面／幅広で均一 | [x] | [ ] | [ ] |
| E-fill-oil | 塗り・油彩ブラシ | 塗られた面／厚い短い筋 | [x] | [ ] | [ ] |
| E-fill-pencil | 塗り・鉛筆（通常） | 塗られた面／細めだが消えない線 | [x] | [ ] | [ ] |
| E-fill-watercolor | 塗り・水彩ブラシ | 塗られた面／半透明の広い面 | [x] | [ ] | [ ] |

## F 動的プレビュー・UI共通部品（生成画像へ置換しない管理対象）

色や選択状態の意味を持つため、固定のかわいい画像に置換すると機能が失われる。チェックは生成ではなく「維持・動作確認」。

| ID | 対象 | 全状態・使用箇所 | 方針 | 確認 |
|---|---|---|---|---|
| F-primary-color | ペン色 | PrimaryColorPreview・18、選択／非選択 | 実際の選択色で描画。静的アイコンが必要なら補助枠のみ | [ ] |
| F-secondary-color | 背景色 | SecondaryColorPreview・18、選択／非選択 | 実際の選択色で描画 | [ ] |
| F-palette | 標準色20個 | 黒、灰色、茶色、赤、オレンジ、黄色、緑、水色、青、紫、白、薄い灰色、薄茶色、ピンク、金色、クリーム色、黄緑、薄い水色、灰青、薄紫 | 20個の色ボタン。色コードは末尾XAML台帳に記録 | [ ] |
| F-custom-colors | ユーザー作成色0～10個 | MainWindow.Colors.cs RebuildCustomColors | 可変色、各24ボタン内に18の色面。AI生成不要 | [ ] |
| F-width-presets | 線幅メニュー7段階 | 1／3／5／8／12／20／30px、選択マークと線見本 | 実装のStrokeWidthPresetsを基準。線そのものを描画 | [ ] |
| F-width-preview | 選択中線幅 | StrokeWidthPreview幅34、可変高さ、StrokeWidthGlyphは輪郭種と同期 | 正確な太さ／色／記号を動的表示 | [ ] |
| F-thumbnails | レイヤー画像 | 現行動的104×54、初期XAML112×54 | 実画像サムネイル。任意枚数を一つの管理項目で追跡 | [ ] |
| F-transparency | 透明領域の市松 | キャンバス／サムネイル／透明選択 | 描画パターンを維持。画像の透明背景に市松を焼き込まない | [ ] |
| F-selection-frame | 選択枠 | 矩形／自由／反転マスク／浮動選択 | 実形状と同期する描画。固定画像ではない | [ ] |
| F-resize-handles | 8方向サイズ変更ハンドル | 左上／上／右上／右／右下／下／左下／左、選択と図形・文字に共通 | 小矩形、通常／操作中。正確な位置とヒット領域を維持 | [ ] |
| F-rotation-handle | 回転ハンドル | 選択と図形・文字、円形と接続線 | 通常／操作中。ツールバーの回転素材とは別 | [ ] |
| F-outline-fill-state | 輪郭／塗りの選択状態 | E全14、ボタン内大アイコンと小記号、線幅欄 | 生成したE素材が選択に応じて更新されるか確認 | [ ] |
| F-toggle-states | ボタン状態 | 通常／ホバー／押下／選択／無効／フォーカス | 状態はUI枠・背景で描画。別AI原画を増やさない | [ ] |
| F-text-mode | アイコン／名前表示 | _buttonIconContentsと表示切替 | 全対象ボタンの復元、ラベル・Tooltip・Automation名の保持 | [ ] |
| F-rulers-grid | ルーラー・グリッド | 水平／垂直ルーラー、目盛り数字、グリッド | データ描画。定規ツールアイコンC-rulerと区別 | [ ] |
| F-status-text | 座標・寸法・倍率・処理状況 | ステータスの可変文字列 | テキストを維持。Aの見出しアイコンだけ差し替え | [ ] |

## U 開閉・入力部品・OS管理UI

| ID | 対象 | 対象状態・使用箇所 | 制作区分 | 確認 |
|---|---|---|---|---|
| U-chevron-down | 下向き開閉矢印 | 鉛筆複合ボタン9、回転メニュー14、ComboBox | 独自に描かれた⌄は共通ベクター化候補。OS部品は標準を維持 | [ ] |
| U-chevron-right | サブメニュー矢印 | ファイル／編集／保存／壁紙等 | WinUI標準 | [ ] |
| U-check | チェック・選択マーク | ToggleMenuFlyoutItem／CheckBox／複数選択 | WinUI標準。必要なら共通チェックを流用 | [ ] |
| U-spin | 増減矢印 | NumberBox文字サイズ・画像寸法・角度 | WinUI標準。＋／－／上下矢印、配置を確認 | [ ] |
| U-scroll | スクロールバーの端・つまみ | リボン／キャンバス／レイヤー／フライアウト | WinUI標準 | [ ] |
| U-input | 入力欄の消去・開閉・選択 | TextBox／NumberBox／ComboBox／ColorPickerの既定テンプレート | 有無はOS・テーマ依存。実画面で確認。AI制作数に含めない | [ ] |
| U-window | 最小化／最大化／復元／閉じる | OSタイトルバー、全画面切替 | OS管理。A-cancelと別の管理対象 | [ ] |
| U-system-dialogs | ファイル選択／共有／印刷／Windows設定／Feedback Hub | OSや外部アプリのUI | アプリ側では生成・差し替えしない。内部ボタン一覧はOS版依存 | [ ] |

## G 文字だけのダイアログ操作・フィールド（アイコン追加は任意）

同じ「OK」「キャンセル」「閉じる」は共通資産を使う場合でも、下表の全画面を確認する。

| ID | 画面 | ボタン／入力／選択肢 | アイコン方針 | 確認 |
|---|---|---|---|---|
| G-unsaved | 未保存確認 | 保存／保存しない／キャンセル | 現状文字。保存A-save、取消A-cancel共有可。保存しないは文字で明示 | [ ] |
| G-text | テキスト挿入・編集 | 挿入／更新／キャンセル、テキスト内容、フォント7種、文字サイズ、配置3種、装飾5種 | 装飾4個はA、背景・配置はC。挿入・更新の文字を維持可 | [ ] |
| G-fonts | フォント候補 | Segoe UI／Arial／Calibri／Times New Roman／Yu Gothic UI／Meiryo／Consolas | 書体名そのもの。フォントごとの画像不要 | [ ] |
| G-resize | サイズ変更 | OK／キャンセル、ピクセル／パーセント、水平／垂直、縦横比維持 | 現状文字・数値・チェック。比率はC-aspect-lock候補 | [ ] |
| G-angle | 任意角度回転・水平傾斜・垂直傾斜 | 角度NumberBox、OK／キャンセル | メニュー候補はC。入力スピンはU | [ ] |
| G-rename | レイヤー名変更 | 名前入力／OK／キャンセル | A-layer-rename共有候補 | [ ] |
| G-colors | 色の編集 | ColorPicker／OK／キャンセル | A-edit-colors共有候補。ColorPickerは標準UI | [ ] |
| G-properties | 画像プロパティ | ピクセル／インチ／センチメートル、閉じる | C-properties共有候補。寸法・日時・容量は文字 | [ ] |
| G-settings | 設定 | ルーラー／グリッド／ステータスバーのチェック、適用／キャンセル、バージョン表示 | A-settings・C表示項目・C-apply共有候補 | [ ] |
| G-wallpaper | 壁紙設定確認 | 設定／キャンセル、Fill／Tile／Centerに応じた説明 | C-wallpaper系共有候補 | [ ] |
| G-feedback | フィードバック | フィードバックHubを開く／キャンセル | A-feedback共有候補 | [ ] |
| G-account | アカウント | Windowsアカウント設定／閉じる | A-account共有候補 | [ ] |
| G-info | 情報・エラー | ShowInfoAsyncの閉じる | 現状文字。新しい警告アイコンを必須として増やさない | [ ] |
| G-recent-empty | 最近使ったファイル・空 | 履歴はありません（無効） | アイコン不要。履歴ありはC-recent共通 | [ ] |
| G-zoom | ズーム倍率 | 25／50／100／200／400／800%、自由入力、スライダー | 倍率は文字。A-zoom-reset/in/outと共通部品Uを確認 | [ ] |
| G-menu-headings | メイン／サブメニュー見出し | ファイル、編集、表示、インポート、最近使ったファイル、名前を付けて保存、壁紙、選択、オブジェクト | 見出し文字を維持。開閉記号はU | [ ] |

キャンバス右クリックの切り取り／コピー／表示レイヤーコピー／貼り付け／トリミング／削除／色反転はA/Cの同名IDを共有。レイヤー右クリックの表示／非表示／複製／削除／上下移動／結合／名前変更もA/Cを共有する。

## H ブランドとカーソル（別制作枠・漏れ確認対象）

通常のボタンPNGと区別する。カーソルはホットスポット、輪郭の視認性、リソースDLLの検証が必要。今回は生成対象数に含めず、既存維持として追跡する。

| ID | 対象 | 現行資産・状態 | 確認 |
|---|---|---|---|
| H-logo | ウィンドウ内ロゴ | Assets/ChunPaintIcon.svg、28 DIP | [ ] |
| H-app-icon | EXE／タスクバー／関連付け | Assets/ChunPaintIcon.ico、16/32/48/256px | [ ] |
| H-cursor-pencil | 鉛筆・ピクセルペン | ChunPaintCursorPencil（共有） | [ ] |
| H-cursor-oil | 油彩 | ChunPaintCursorOil | [ ] |
| H-cursor-watercolor | 水彩 | ChunPaintCursorWatercolor | [ ] |
| H-cursor-marker | マーカー | ChunPaintCursorMarker | [ ] |
| H-cursor-natural | 鉛筆風・クレヨン | ChunPaintCursorNaturalPencil（共有） | [ ] |
| H-cursor-airbrush | エアブラシ | ChunPaintCursorAirbrush | [ ] |
| H-cursor-calligraphy | カリグラフィー・平筆 | ChunPaintCursorCalligraphy（共有） | [ ] |
| H-cursor-fill | 塗りつぶし | ChunPaintCursorFill | [ ] |
| H-cursor-eraser | 消しゴム | ChunPaintCursorEraser | [ ] |
| H-cursor-picker | スポイト | ChunPaintCursorPicker | [ ] |
| H-cursor-shape | 全図形 | ChunPaintCursorShape | [ ] |
| H-cursor-zoom | 拡大鏡 | ChunPaintCursorZoom | [ ] |
| H-system-cursors | システムカーソル | Arrow／IBeam／SizeAll／Hand／SizeNorthwestSoutheast／SizeNortheastSouthwest／SizeNorthSouth／SizeWestEast。選択・文字・移動・回転・リサイズ・障害時フォールバック | [ ] |

## 生成後の照合手順

1. A/B/D/Eの全IDについて原画、各必要サイズ、透過PNGを照合する。Cは採用したものだけ追加。共有素材は共有先を明記し、空欄を完成扱いにしない。
2. 白・濃灰の両背景で16/24/32 DIP相当を確認。図形は16 DIPを確認。輪郭欠け、透明背景への市松焼き込み、余白のばらつき、別機能との混同を検査する。
3. 100/125/150/200% DPIで確認する。15のブラシ、10相当の目印、12の状態見本は専用確認を行う。32表示版とDPI用32px画像を混同しない。
4. ソース内のPNG参照だけでなくTextBlockの文字、Content、Path、Canvas、動的Textの更新も照合する。残った文字記号を意図した維持／未差し替えに分類する。
5. 鉛筆複合ボタン、ブラシ9種、図形23種、輪郭7・塗り7、線幅7段階、表示／非表示、アイコン／名前切替を実際に巡回する。
6. 各メニュー／右クリック／ダイアログは末尾XAML台帳とG・動的項目表を突き合わせる。クリック、キーボード、Tooltip、Automation名、無効化条件を保持する。
7. 生成／組込／実寸の3欄を更新し、実寸確認の画像・確認DPI・日付を記録する。画像が揃っただけでは「全ボタン確認済み」にしない。
8. UI変更後はソースのボタン数・Tag・動的メニューを再抽出する。この台帳は調査日時点の固定スナップショットであり、自動追従しない。

## AIへ渡す共通追記

```text
同封の台帳のうち、今回指定するIDだけを生成してください。
1 IDにつきアイコン単体の原画を1点作成し、IDと対応が分かる形で納品してください。
通常は16/24/32 DIPでの表示、図形は16 DIPでの表示を前提にしてください。
32 DIPの200%表示には64px版を用意できる原画が必要です。
原画の解像度と最終表示サイズを混同せず、縮小しても分かる形にしてください。
輪郭色、線幅、配色、余白を参照画像と統一してください。
図形の形状・矢印の方向・輪郭と塗りの違いは正確に維持してください。
テキストツールや文字装飾のA/B/I/U/Sは機能記号として使用できます。
各アイコンの名前ラベル、台座、透かしは描かず、背景は実際の透明にしてください。
色見本・サムネイル・OS標準部品・カーソルは今回のボタン画像として生成しません。
```

## 調査根拠

- MainWindow.xaml: 静的ボタン・メニュー・図形・色見本・ステータス。
- MainWindow.TextAndBrushes.cs: ブラシ9種、文字書式。AddBrushMenuItemのglyph引数は現状でアイコン表示に使用されず、SelectBrushもボタン画像を更新しない。生成後に別途接続が必要。
- MainWindow.Shapes.cs: 輪郭／塗りメニューと動的記号。
- MainWindow.xaml.cs: 線幅7段階、色編集、未保存確認、汎用ダイアログ、アイコン／名前切替。
- MainWindow.Layers.cs: レイヤー可視状態、サムネイル、レイヤー・キャンバスの右クリックメニュー。
- MainWindow.ImageOperations.cs／ImageProperties.cs: 回転8コマンド、サイズ・角度・寸法単位。
- MainWindow.Colors.cs／RecentFiles.cs: 可変色・可変履歴。
- MainWindow.TopCommands.cs／Wallpaper.cs／Printing.cs／FullScreen.cs: 設定・外部UI・表示状態。
- MainWindow.Cursors.cs／Objects.cs／Selection.cs: カーソルと編集ハンドル。
- App.xaml: WinUI標準リソースのみ。App.xaml.csの起動処理も別の自作ボタン画面を追加しない。
- 非表示のAI拡張用メソッドは現行XAMLに公開ボタンがないため生成対象にしない。OS・WinUI内部テンプレートの全アイコンは実装コードから列挙できないためUとして別管理する。

## 静的UI照合用の全132配置（自動抽出）

MainWindow.xamlからButton 75、ToggleButton 10、MenuBarItem 3、MenuFlyoutItem 33、MenuFlyoutSubItem 6、ToggleMenuFlyoutItem 5を抽出。アイコン原画数ではなく使用箇所数。動的項目・状態はB/E/F/G/Hに別記。行番号は調査時点。

| 配置ID | 種別 | 表示名・識別名 | Click | Tag | XAML行 | 確認 |
|---|---|---|---|---|---|---|
| X001 | MenuBarItem | ファイルメニュー |  |  | 50 | [ ] |
| X002 | MenuFlyoutItem | 新規 | New_Click |  | 51 | [ ] |
| X003 | MenuFlyoutItem | 開く | Open_Click |  | 52 | [ ] |
| X004 | MenuFlyoutSubItem | キャンバスにインポート |  |  | 53 | [ ] |
| X005 | MenuFlyoutItem | ファイルから | Import_Click |  | 54 | [ ] |
| X006 | MenuFlyoutSubItem | 最近使ったファイル [RecentFilesMenu] |  |  | 56 | [ ] |
| X007 | MenuFlyoutItem | 保存 | Save_Click |  | 58 | [ ] |
| X008 | MenuFlyoutItem | プロジェクトとして保存 | SaveAs_Click | chunpaint | 59 | [ ] |
| X009 | MenuFlyoutSubItem | 名前を付けて保存 |  |  | 60 | [ ] |
| X010 | MenuFlyoutItem | ChunPaint プロジェクト | SaveAs_Click | chunpaint | 61 | [ ] |
| X011 | MenuFlyoutItem | PNG 画像 | SaveAs_Click | png | 62 | [ ] |
| X012 | MenuFlyoutItem | JPEG 画像 | SaveAs_Click | jpg | 63 | [ ] |
| X013 | MenuFlyoutItem | BMP 画像 | SaveAs_Click | bmp | 64 | [ ] |
| X014 | MenuFlyoutItem | GIF 画像 | SaveAs_Click | gif | 65 | [ ] |
| X015 | MenuFlyoutItem | 印刷 | Print_Click |  | 68 | [ ] |
| X016 | MenuFlyoutSubItem | デスクトップの背景として設定 |  |  | 70 | [ ] |
| X017 | MenuFlyoutItem | 画面に合わせる | Wallpaper_Click | Fill | 71 | [ ] |
| X018 | MenuFlyoutItem | 並べて表示 | Wallpaper_Click | Tile | 72 | [ ] |
| X019 | MenuFlyoutItem | 中央に表示 | Wallpaper_Click | Center | 73 | [ ] |
| X020 | MenuFlyoutItem | 画像のプロパティ | Properties_Click |  | 76 | [ ] |
| X021 | MenuFlyoutItem | 終了 | Exit_Click |  | 77 | [ ] |
| X022 | MenuBarItem | 編集メニュー |  |  | 79 | [ ] |
| X023 | MenuFlyoutItem | 元に戻す [UndoMenuItem] | Undo_Click |  | 80 | [ ] |
| X024 | MenuFlyoutItem | やり直し [RedoMenuItem] | Redo_Click |  | 81 | [ ] |
| X025 | MenuFlyoutSubItem | 選択 |  |  | 83 | [ ] |
| X026 | MenuFlyoutItem | 矩形選択 | RectangleSelection_Click |  | 84 | [ ] |
| X027 | MenuFlyoutItem | 自由選択 | FreeSelection_Click |  | 85 | [ ] |
| X028 | MenuFlyoutItem | すべて選択 [SelectAllMenuItem] | SelectAll_Click |  | 86 | [ ] |
| X029 | MenuFlyoutItem | 選択範囲を反転 | InvertSelectionRegion_Click |  | 87 | [ ] |
| X030 | ToggleMenuFlyoutItem | 透明な選択 [TransparentSelectionMenuItem] | ToggleTransparentSelection_Click |  | 88 | [ ] |
| X031 | MenuFlyoutItem | 選択を適用 [SelectionApplyMenuItem] | ApplySelection_Click |  | 89 | [ ] |
| X032 | MenuFlyoutItem | 選択をキャンセル [SelectionCancelMenuItem] | CancelSelection_Click |  | 90 | [ ] |
| X033 | MenuFlyoutSubItem | オブジェクト |  |  | 92 | [ ] |
| X034 | MenuFlyoutItem | オブジェクトを適用 [ObjectApplyMenuItem] | ApplyObject_Click |  | 93 | [ ] |
| X035 | MenuFlyoutItem | オブジェクトをキャンセル [ObjectCancelMenuItem] | CancelObject_Click |  | 94 | [ ] |
| X036 | MenuFlyoutItem | 切り取り [CutMenuItem] | Cut_Click |  | 96 | [ ] |
| X037 | MenuFlyoutItem | コピー [CopyMenuItem] | Copy_Click |  | 97 | [ ] |
| X038 | MenuFlyoutItem | 表示レイヤーをコピー [CopyVisibleLayersMenuItem] | CopyVisibleLayers_Click |  | 98 | [ ] |
| X039 | MenuFlyoutItem | 貼り付け [PasteMenuItem] | Paste_Click |  | 99 | [ ] |
| X040 | MenuFlyoutItem | 削除 [DeleteMenuItem] | Delete_Click |  | 101 | [ ] |
| X041 | MenuFlyoutItem | 色の反転 [InvertMenuItem] | Invert_Click |  | 102 | [ ] |
| X042 | MenuBarItem | 表示メニュー |  |  | 104 | [ ] |
| X043 | ToggleMenuFlyoutItem | ルーラー [RulerMenuItem] | Ruler_Click |  | 105 | [ ] |
| X044 | ToggleMenuFlyoutItem | グリッド線 [GridMenuItem] | Grid_Click |  | 106 | [ ] |
| X045 | ToggleMenuFlyoutItem | ステータス バー [StatusBarMenuItem] | StatusBar_Click |  | 107 | [ ] |
| X046 | MenuFlyoutItem | 100% | ResetZoom_Click |  | 109 | [ ] |
| X047 | ToggleMenuFlyoutItem | 全画面表示 [FullScreenMenuItem] | FullScreen_Click |  | 110 | [ ] |
| X048 | Button | 保存 [SaveToolbarButton] | Save_Click |  | 115 | [ ] |
| X049 | Button | 共有 [ShareToolbarButton] | Share_Click |  | 123 | [ ] |
| X050 | Button | 元に戻す | Undo_Click |  | 127 | [ ] |
| X051 | Button | やり直す | Redo_Click |  | 128 | [ ] |
| X052 | ToggleButton | ボタン表示モード [ButtonDisplayModeToggleButton] | ButtonDisplayModeToggle_Click |  | 129 | [ ] |
| X053 | Button | ズームアウト [ZoomOutToolbarButton] | ZoomOut_Click |  | 130 | [ ] |
| X054 | Button | ズームイン [ZoomInToolbarButton] | ZoomIn_Click |  | 131 | [ ] |
| X055 | Button | フィードバック | Feedback_Click |  | 134 | [ ] |
| X056 | Button | アカウント | Account_Click |  | 135 | [ ] |
| X057 | Button | 設定 | Settings_Click |  | 136 | [ ] |
| X058 | ToggleButton | 矩形選択 [RectangleSelectionButton] | RectangleSelection_Click |  | 178 | [ ] |
| X059 | ToggleButton | 自由選択 [FreeSelectionButton] | FreeSelection_Click |  | 179 | [ ] |
| X060 | Button | すべて選択 [SelectAllButton] | SelectAll_Click |  | 180 | [ ] |
| X061 | Button | 選択をキャンセル [CancelSelectionButton] | CancelSelection_Click |  | 181 | [ ] |
| X062 | ToggleButton | 透明な選択 [TransparentSelectionButton] | ToggleTransparentSelection_Click |  | 182 | [ ] |
| X063 | Button | 選択範囲を削除 [DeleteSelectionButton] | Delete_Click |  | 183 | [ ] |
| X064 | Button | トリミング [CropMenuItem] | Crop_Click |  | 194 | [ ] |
| X065 | Button | サイズ変更 | Resize_Click |  | 195 | [ ] |
| X066 | Button | キャンバスにインポート | Import_Click |  | 196 | [ ] |
| X067 | Button | 背景の削除 | BackgroundRemoval_Click |  | 197 | [ ] |
| X068 | Button | 回転 | Rotate_Click |  | 198 | [ ] |
| X069 | Button | 回転メニュー | Rotate_Click |  | 199 | [ ] |
| X070 | ToggleButton | 鉛筆・ブラシの種類 [PencilToolButton] | PencilButton_Click | Pencil | 210 | [ ] |
| X071 | ToggleButton | 塗りつぶしツール [FillToolButton] | ToolButton_Click | Fill | 211 | [ ] |
| X072 | ToggleButton | テキストツール [TextToolButton] | ToolButton_Click | Text | 212 | [ ] |
| X073 | ToggleButton | 消しゴムツール [EraserToolButton] | ToolButton_Click | Eraser | 213 | [ ] |
| X074 | ToggleButton | スポイトツール [PickerToolButton] | ToolButton_Click | Picker | 214 | [ ] |
| X075 | ToggleButton | 拡大鏡ツール [ZoomToolButton] | ToolButton_Click | Zoom | 215 | [ ] |
| X076 | Button | 直線 | ShapeButton_Click | Line | 228 | [ ] |
| X077 | Button | 曲線 | ShapeButton_Click | Curve | 228 | [ ] |
| X078 | Button | 楕円 | ShapeButton_Click | Ellipse | 228 | [ ] |
| X079 | Button | 四角形 | ShapeButton_Click | Rectangle | 228 | [ ] |
| X080 | Button | 角丸四角形 | ShapeButton_Click | RoundedRectangle | 228 | [ ] |
| X081 | Button | 平行四辺形 | ShapeButton_Click | Parallelogram | 228 | [ ] |
| X082 | Button | 三角形 | ShapeButton_Click | Triangle | 229 | [ ] |
| X083 | Button | 直角三角形 | ShapeButton_Click | RightTriangle | 229 | [ ] |
| X084 | Button | ひし形 | ShapeButton_Click | Diamond | 229 | [ ] |
| X085 | Button | 五角形 | ShapeButton_Click | Pentagon | 229 | [ ] |
| X086 | Button | 六角形 | ShapeButton_Click | Hexagon | 229 | [ ] |
| X087 | Button | 右矢印 | ShapeButton_Click | RightArrow | 229 | [ ] |
| X088 | Button | 左矢印 | ShapeButton_Click | LeftArrow | 230 | [ ] |
| X089 | Button | 上矢印 | ShapeButton_Click | UpArrow | 230 | [ ] |
| X090 | Button | 下矢印 | ShapeButton_Click | DownArrow | 230 | [ ] |
| X091 | Button | 四角星 | ShapeButton_Click | FourPointStar | 230 | [ ] |
| X092 | Button | 星 | ShapeButton_Click | Star | 230 | [ ] |
| X093 | Button | 塗りつぶし星形 | ShapeButton_Click | FilledStar | 230 | [ ] |
| X094 | Button | 吹き出し | ShapeButton_Click | Callout | 231 | [ ] |
| X095 | Button | 楕円形 | ShapeButton_Click | Oval | 231 | [ ] |
| X096 | Button | 雲 | ShapeButton_Click | Cloud | 231 | [ ] |
| X097 | Button | ハート | ShapeButton_Click | Heart | 231 | [ ] |
| X098 | Button | 稲妻 | ShapeButton_Click | Lightning | 231 | [ ] |
| X099 | Button | 輪郭の種類（ペン） [OutlineStyleButton] | OutlineStyleButton_Click |  | 235 | [ ] |
| X100 | Button | 塗りの種類（ペンキ） [FillStyleButton] | FillStyleButton_Click |  | 240 | [ ] |
| X101 | Button | ペンの太さ [StrokeWidthButton] | StrokeWidthButton_Click |  | 246 | [ ] |
| X102 | Button | ペン色 [PrimaryColorButton] | ColorTarget_Click | Primary | 260 | [ ] |
| X103 | Button | 黒 | Palette_Click | #FF000000 | 268 | [ ] |
| X104 | Button | 灰色 | Palette_Click | #FF7F7F7F | 268 | [ ] |
| X105 | Button | 茶色 | Palette_Click | #FF880015 | 268 | [ ] |
| X106 | Button | 赤 | Palette_Click | #FFED1C24 | 268 | [ ] |
| X107 | Button | オレンジ | Palette_Click | #FFFF7F27 | 268 | [ ] |
| X108 | Button | 黄色 | Palette_Click | #FFFFF200 | 268 | [ ] |
| X109 | Button | 緑 | Palette_Click | #FF22B14C | 268 | [ ] |
| X110 | Button | 水色 | Palette_Click | #FF00A2E8 | 268 | [ ] |
| X111 | Button | 青 | Palette_Click | #FF3F48CC | 268 | [ ] |
| X112 | Button | 紫 | Palette_Click | #FFA349A4 | 268 | [ ] |
| X113 | Button | 白 | Palette_Click | #FFFFFFFF | 269 | [ ] |
| X114 | Button | 薄い灰色 | Palette_Click | #FFC3C3C3 | 269 | [ ] |
| X115 | Button | 薄茶色 | Palette_Click | #FFB97A57 | 269 | [ ] |
| X116 | Button | ピンク | Palette_Click | #FFFFAEC9 | 269 | [ ] |
| X117 | Button | 金色 | Palette_Click | #FFFFC90E | 269 | [ ] |
| X118 | Button | クリーム色 | Palette_Click | #FFEFE4B0 | 269 | [ ] |
| X119 | Button | 黄緑 | Palette_Click | #FFB5E61D | 269 | [ ] |
| X120 | Button | 薄い水色 | Palette_Click | #FF99D9EA | 269 | [ ] |
| X121 | Button | 灰青 | Palette_Click | #FF7092BE | 269 | [ ] |
| X122 | Button | 薄紫 | Palette_Click | #FFC8BFE7 | 269 | [ ] |
| X123 | Button | 色の編集 [EditColorsFlyoutButton] | EditColors_Click |  | 272 | [ ] |
| X124 | Button | 背景色 [SecondaryColorButton] | ColorTarget_Click | Secondary | 278 | [ ] |
| X125 | Button | レイヤー | Layers_Click |  | 286 | [ ] |
| X126 | Button | レイヤーを追加 | AddLayer_Click |  | 320 | [ ] |
| X127 | Button | レイヤーパネルを閉じる | Layers_Click |  | 321 | [ ] |
| X128 | Button | レイヤーを上へ移動 | MoveLayerUp_Click |  | 329 | [ ] |
| X129 | Button | レイヤーを下へ移動 | MoveLayerDown_Click |  | 329 | [ ] |
| X130 | Button | 選択レイヤーを結合 [MergeSelectedLayersButton] | MergeLayerDown_Click |  | 330 | [ ] |
| X131 | Button | レイヤー名を変更 | RenameLayer_Click |  | 330 | [ ] |
| X132 | Button | ズームを100パーセントに戻す | ResetZoom_Click |  | 341 | [ ] |

## v0.2.0 統一アイコン（2026-09-12）

- 92 IDを維持し、16/20/24/32/40/48/64 pxの644 PNGを生成済み。各行は生成のみチェックし、組込・実寸は実表示の確認待ちとして未チェック。
- 決定的な生成元: `dev/assets/New-UnifiedIcons.ps1`。保存先: `src/ChunPaint/Assets/RibbonIcons/unified/`。
- 原画はスクリプト内の独自幾何学ベクター。既存の4画像素材は維持。外部画像の取得・流用はない。
- `MainWindow.UnifiedIcons.cs`が通常ボタンのアクセシビリティ名、図形23種のTag、静的メニューの表示名を対応付ける。初期化の呼出と動的メニューの接続は親統合対象。
- 動的ブラシ9種、輪郭/塗り14状態、文字ダイアログ、レイヤー表示状態には`CreateUnifiedIcon(ID, DIP)`を利用する。素材提供だけで全使用箇所の組込完了とは扱わない。
- `contact-sheet.png`は全92 IDの16/32 px比較資料。これは実際のアプリ上のDPI別確認の代用ではない。
- アプリのBitmapIconはテーマ前景色へ追従する。色付き原画の意味を色だけに依存させず、シルエットで区別する。
### v0.2.0 接続コード追記

`MainWindow.TextAndBrushes.cs`の動的9種と文字装飾4種、`MainWindow.Shapes.cs`の輪郭7種・塗り7種と現在選択状態、`MainWindow.Layers.cs`の目アイコン2状態・右クリックへ接続した。座標・寸法・スライダー見出しは初期化時に親Panel内の記号だけ差替え、数値やラベルを維持する。鉛筆ボタンは鉛筆/ブラシ総称を初期表示し、選択後はブラシ種類に対応したアイコンへ切替える。加工・設定・AIの新メニューは既存素材を共有する。

全92 IDに接続コードを用意したが、この追記時点のアプリ全経路実表示は未確認。そのため各行の組込・実寸欄は最終UI検証まで未チェックを維持する。