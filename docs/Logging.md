# ログとトラブルシューティング

## ログの場所

PhotoGeoExplorer は詳細なログファイルを出力します。

### プライマリログ場所
```
%LocalAppData%\PhotoGeoExplorer\Logs\app.log
```

### フォールバックログ場所
プライマリの場所に書き込めない場合、以下の場所に保存されます：
```
<インストール先>\Logs\app.log
```

### 世代管理（ローテーション）

ログは起動時に削除されません。`app.log` が 1 MB を超えると、`app.1.log`（直前の世代）、`app.2.log`、… へ繰り下げます。`app.log` を含めて最大 5 ファイル（`app.log` と `app.1.log`〜`app.4.log`、合計約 5 MB）を保持し、最も古い世代から削除します。フォールバックのログも同じ規則です。

- 異常終了したセッションのログは、次回の起動後も残ります。ただし、古い世代は新しいログの増加に応じて削除されるため、問題が起きたら早めに回収してください。
- 1 つのファイルに複数のセッションが含まれることがあります。セッションは `SessionId` で区別します。
- 繰り下げ中に他のプロセス（ログビューアーなど）が世代ファイルを開いていて移動できない場合は、ログの出力を止めません。現行ログは `app.rotating.log` として残し、次回以降の書き込みで世代管理に戻します（その間は `app.log` が 1 MB を超え、ファイルが 1 つ増えることがあります）。

## ログへのアクセス

1. アプリのメニューから「ヘルプ」→「ログフォルダーを開く」を選択
2. エクスプローラーでログフォルダーが開きます
3. `app.log`（最新）と、必要に応じて `app.1.log` 以降（過去の世代）を開いて内容を確認

## ログの形式

ログは以下の形式で記録されます：

```
yyyy-MM-dd HH:mm:ss.fff zzz [SessionId] [ThreadId] LEVEL Message

Exception Type: <例外の完全な型名>
Exception Message: <例外メッセージ>
Stack Trace:
<スタックトレース>
```

### フィールドの説明

- **タイムスタンプ**: ログエントリの日時（ミリ秒単位、タイムゾーン付き）
- **SessionId**: アプリセッションを識別する8文字のID（アプリ起動ごとに一意）
- **ThreadId**: スレッドID（UIスレッドと非UIスレッドを識別）
- **LEVEL**: `INFO` または `ERROR`
- **Message**: ログメッセージ
- **Exception Type**: 例外の完全な型名（例: `System.ArgumentException`）
- **Exception Message**: 例外のメッセージ
- **Stack Trace**: スタックトレース（利用可能な場合）

## ログ例

### 正常起動
```
2026-01-14 14:30:15.123 +09:00 [a1b2c3d4] [T1] INFO Program.Main starting. SessionId: a1b2c3d4
2026-01-14 14:30:15.234 +09:00 [a1b2c3d4] [T1] INFO App constructed.
```

### 空フォルダ読み込み
```
2026-01-14 14:30:20.456 +09:00 [a1b2c3d4] [T1] INFO LoadFolderCoreAsync: Loading folder 'C:\Users\Test\Empty', previousPath='C:\Users\Test\Pictures', isNavigating=False, selectedCount=0
2026-01-14 14:30:20.478 +09:00 [a1b2c3d4] [T5] INFO EnumerateFiles: folderPath='C:\Users\Test\Empty', imagesOnly=True, searchText='(null)'
2026-01-14 14:30:20.491 +09:00 [a1b2c3d4] [T5] INFO EnumerateFiles: Completed. Total: 0 items (0 dirs + 0 files)
2026-01-14 14:30:20.493 +09:00 [a1b2c3d4] [T1] INFO LoadFolderCoreAsync: Folder 'C:\Users\Test\Empty' loaded successfully. Item count: 0
```

### エラー例
```
2026-01-14 14:35:22.123 +09:00 [a1b2c3d4] [T1] ERROR Failed to load preview image. FilePath: 'C:\Users\Test\photo.jpg'
Exception Type: System.ArgumentException
Exception Message: パラメーターが間違っています。
Stack Trace:
   at Microsoft.UI.Xaml.Media.Imaging.BitmapImage..ctor(Uri uriSource)
   at PhotoGeoExplorer.ViewModels.MainViewModel.UpdatePreview(PhotoListItem item)
```

## トラブルシューティング

### クラッシュが発生した場合

1. ログフォルダーを開く（「ヘルプ」→「ログフォルダーを開く」）
2. `app.log` を開く（再起動後に調べる場合は、`app.1.log` 以降も含めて探す）
3. SessionId で該当セッションを特定
4. ERROR エントリを探す
5. スタックトレースと例外情報を確認
6. 必要に応じて開発者に報告

### 問題報告時に含める情報

- ログファイル（`app.log` と `app.1.log` 以降の世代、または該当セッションのログ）
- 再現手順
- 発生した日時
- SessionId
- 発生頻度（常に/時々/稀に）

## プライバシーとセキュリティ

ログには以下が含まれる場合があります：
- ファイルパス
- フォルダー名
- システム情報（タイムゾーン、スレッドID）
- エラーメッセージとスタックトレース

ログには以下は含まれません：
- 写真の内容
- 個人情報（EXIF データの詳細など）
- ネットワーク通信内容

ログは世代管理により、最大 5 ファイル（約 5 MB）分の過去のセッションが `%LocalAppData%\PhotoGeoExplorer\Logs\` に残ります。パスなどが複数のセッションにわたって残るため、共有する場合は機密情報が含まれていないか確認してください。不要になったログは、フォルダー内の `app*.log` を削除して構いません（次回の起動で再作成されます）。
