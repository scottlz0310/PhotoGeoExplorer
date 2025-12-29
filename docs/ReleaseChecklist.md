# リリースチェックリスト

## 事前準備（タグ作成前）

- [ ] `PhotoGeoExplorer.Installer/PhotoGeoExplorer.Installer.wixproj` の `ProductVersion` を更新
- [ ] `PhotoGeoExplorer/Package.appxmanifest` の `Identity Version` を更新（例: `1.2.3.0`）
- [ ] `CHANGELOG.md` に該当バージョンのセクションを追加
- [ ] リリースノートに含める内容を整理（tasks.md の完了/未完了、docs の要点）

## リリース実行

- [ ] `vX.Y.Z` タグを作成して push
- [ ] GitHub Release が自動作成されていることを確認
- [ ] MSI アーティファクトが添付されていることを確認
- [ ] リリースノートを手動編集（必要に応じて）

## リリース後

- [ ] クリーン環境でインストール/起動を確認
- [ ] ランタイム導線（Windows App SDK Runtime）が機能するか確認
- [ ] tasks.md の進捗を更新
