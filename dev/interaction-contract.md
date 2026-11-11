# 編集操作の共通契約

ドラッグ操作を追加するときや既存操作を修正するときは、下表を設計・レビュー・関連試験の観点として使う。操作しない属性を試験だけのために増やさない。

| 契約 | 現行の確認先 |
|---|---|
| 枠・ハンドルの描画と当たり判定は同じ幾何・座標変換を使う。外側半分も操作できる | ScreenshotHandleGeometry、ScreenshotHandleRenderer、HandleGeometryTests |
| 押した位置からの差分で動かし、ハンドル中心へ飛ばさない | HandleGeometryTests.GrabbingAnOutsideHalfDoesNotJumpAndDragUsesTheActualPointerDelta |
| ドラッグ中は仮状態。離したときに文書へ1回確定し、Undoの1単位にする | ScreenshotRegionLift、RegionLiftTests、ScreenshotEditingTests |
| Esc・キャプチャ喪失では文書と選択を操作前へ戻す。履歴を増やさない | EditorWindow.CancelGesture、RegionLiftTestsの仮状態不変。イベント配線はUI確認も行う |
| Shift／Ctrl／Altの変化はプレビューへ反映し、離した時点の状態で確定する | ScreenshotManipulation、ScreenshotModifierTests、RegionLiftTests |
| ラスタ移動は整数画素。倍率・DPIが変わっても表示枠と処理画素を一致させる | HandleGeometryTests、RegionLiftRenderingTests、FixedResizePreviewRenderingTests |
| 部分編集で範囲外のRGBAを変えず、PNG保存後もalphaを維持する | RegionLiftRenderingTests、ShapeStyleRenderingTests、画像入出力試験 |
| 選択の値と新規作図設定を分け、明示的な設定取得だけで移す | ShapeStylesTests、設定保持試験 |

自動試験の成功は実マウス・キーボード・IMEの成功とは分けて記録する。UI確認では押下・途中・解放・取消に加え、無効状態・フォーカス・ツールチップ・狭い画面の折り返しを変更箇所に応じて見る。実入力は対象が確定できない場合は中止し、未実施とする。
