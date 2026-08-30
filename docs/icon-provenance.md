# ChunPaint アイコン出典台帳

## 運用方針

- `status: temporary` はUI比較用の暫定素材であり、Release成果物へ含めない。
- C14完了前に独自作成または利用条件を満たす最終素材へ差し替え、`status: final`へ更新する。
- XAMLは機能別PNGを個別参照し、コード変更なしで差し替えられる構成を維持する。

## 暫定リボンアイコン（C8）

| ファイル | 切り出し元 | 切り出し矩形（x, y, w, h） | 作成日 | status | C14差し替え確認 |
|---|---|---:|---|---|---|
| `Assets/RibbonIcons/temporary/resize.png` | `docs/mspaint-capture/スクリーンショット (5).png` | `260, 120, 32, 32` | 2026-08-30 | temporary | 未完了 |
| `Assets/RibbonIcons/temporary/eraser.png` | `docs/mspaint-capture/スクリーンショット (5).png` | `330, 145, 32, 32` | 2026-08-30 | temporary | 未完了 |
| `Assets/RibbonIcons/temporary/fill.png` | `docs/mspaint-capture/スクリーンショット (5).png` | `380, 94, 32, 32` | 2026-08-30 | temporary | 未完了 |
| `Assets/RibbonIcons/temporary/brush.png` | `docs/mspaint-capture/スクリーンショット (5).png` | `506, 94, 32, 32` | 2026-08-30 | temporary | 未完了 |

切り出し後は左上画素と同色の背景を透明化した。元画像はPaint 11.2605.81.0、日本語UI、Windows表示スケール125%で取得した保存済み観察資料である。

## アプリブランド

| ファイル | 作成方法 | 作成日 | status | 備考 |
|---|---|---|---|---|
| `Assets/ChunPaintIcon.svg` | ChunPaint独自SVG | 2026-08-29 | final | Microsoft Paintのコード・DLL・リソースを使用していない。 |
