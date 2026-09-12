# ChunPaint アイコン出典台帳

## 運用方針

- Release成果物へ含めるリボンPNGは、ChunPaintプロジェクト内で独自生成した`status: final`の素材だけとする。
- `dev/assets/New-RibbonIcons.ps1`を再実行すると、同じ32×32 PNGをコードから再生成できる。
- `dev/assets/New-AppIcon.ps1`を再実行すると、EXE埋め込み用の16／32／48／256px ICOを独自ベクター描画から再生成できる。
- `dev/assets/New-AppCursors.ps1`を再実行すると、独自ベクター描画からカーソルリソースDLLを再生成できる。
- XAMLは機能別PNGを個別参照し、コード変更なしで差し替えられる構成を維持する。

## 最終リボンアイコン（C14）

| ファイル | 作成方法 | 作成日 | status | 確認 |
|---|---|---|---|---|
| `Assets/RibbonIcons/final/resize.png` | 独自ベクター描画。外枠と対角の寸法変更矢印 | 2026-08-31 | final | 32×32 PNG、透明背景 |
| `Assets/RibbonIcons/final/eraser.png` | 独自ベクター描画。桃色本体と水色の消去面 | 2026-08-31 | final | 32×32 PNG、透明背景 |
| `Assets/RibbonIcons/final/fill.png` | 独自ベクター描画。傾けたペンキ缶と滴 | 2026-08-31 | final | 32×32 PNG、透明背景 |
| `Assets/RibbonIcons/final/brush.png` | 独自ベクター描画。橙色の柄、水色の口金、濃色の毛先 | 2026-08-31 | final | 32×32 PNG、透明背景 |

4点とも`dev/assets/New-RibbonIcons.ps1`のWPFベクター命令だけから生成し、外部製品の画像・コード・DLL・リソースは含めていない。C8の比較用素材はC14で参照と配布対象から除去した。

## アプリブランド

| ファイル | 作成方法 | 作成日 | status | 備考 |
|---|---|---|---|---|
| `Assets/ChunPaintIcon.svg` | ChunPaint独自SVG | 2026-08-29 | final | Microsoft Paintのコード・DLL・リソースを使用していない。 |
| `Assets/ChunPaintIcon.ico` | `dev/assets/New-AppIcon.ps1`による独自ベクター描画 | 2026-08-31 | final | EXEの`ApplicationIcon`。16／32／48／256pxのPNGフレームを含む。 |
| `Assets/ChunPaintCursors.dll` | `dev/assets/New-AppCursors.ps1`による独自ベクター描画とWin32リソース化 | 2026-08-31 | final | 実カーソル用。ペン種別、消しゴム、拡大鏡などのカーソルを含む。 |

## v0.2.0 統一UIアイコン

2026-09-12に92種・各7サイズを制作した。生成元は`dev/assets/New-UnifiedIcons.ps1`。各アイコンは本プロジェクト向けに記述した独自の線・曲線・多角形で、外部素材・商用アイコンセット・生成AI画像を使用していない。WPFで透明PNGへ決定的にラスタライズする。コンタクトシートのIDラベルだけはWindowsのSegoe UIで描画する。

既存素材は置換削除せず維持した。権利表記の追加が必要な第三者図案の依存はない。再生成コマンドはリポジトリ直下で`pwsh -File dev/assets/New-UnifiedIcons.ps1`。アプリでの実表示検証は台帳の各行に分けて記録し、未検証の使用箇所は未チェックのままとする。