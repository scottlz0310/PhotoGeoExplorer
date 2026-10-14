# フォルダー選択の履歴タスク（#235）

関連: [Issue #235](https://github.com/scottlz0310/PhotoGeoExplorer/issues/235)

- [x] 前回と同じフォルダーを復元すると決定ボタンが無効になることを確認（ユーザー実機確認）
- [x] Windows App SDK の FolderPicker に移行し、WindowId と結果の Path を使用
- [x] 移動・コピー・開くの履歴を機能別の SettingsIdentifier に分離
- [x] 手動確認手順・CHANGELOG を更新
- [x] 開く・コピー・移動の3経路で、同じフォルダーを続けて確定できることを実機確認（非パッケージ起動、ユーザー確認）
- [ ] キャンセル時にファイル操作・フォルダー遷移が行われないことを実機確認
- [ ] MSIX 配布版で `SettingsIdentifier` による前回フォルダーの復元と3経路の履歴分離を確認
- [x] Debug/x64・Release/x64 analyzer ビルド、単体テスト788件、format を確認

E2E は通常の実行ゲートにより14件スキップ。

非パッケージ起動では `SettingsIdentifier` による前回フォルダーの復元は確認できず、初期位置は常にピクチャ（`SuggestedStartFolder` に替えても同じ）。復元の扱いは MSIX の挙動確認後に別 Issue で判断する。現在表示中のフォルダーを初期候補にする UX の変更は含めない。
