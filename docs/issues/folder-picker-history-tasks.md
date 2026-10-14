# フォルダー選択の履歴タスク（#235）

関連: [Issue #235](https://github.com/scottlz0310/PhotoGeoExplorer/issues/235)

- [x] 前回と同じフォルダーを復元すると決定ボタンが無効になることを確認（ユーザー実機確認）
- [x] Windows App SDK の FolderPicker に移行し、WindowId と結果の Path を使用
- [x] 移動・コピー・開くの履歴を機能別の SettingsIdentifier に分離
- [x] 手動確認手順・CHANGELOG を更新
- [ ] 移動・コピー・開くの連続選択とキャンセルを確認
- [ ] ビルド・単体テスト・format を確認

現在表示中のフォルダーを初期候補にする UX の変更は含めない。
