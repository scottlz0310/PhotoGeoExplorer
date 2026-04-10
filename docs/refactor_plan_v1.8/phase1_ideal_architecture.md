# Phase 1: 理想アーキテクチャ定義

> 作成日: 2026-04-11
> 対象: PhotoGeoExplorer v1.8 リファクタリング計画
> 位置づけ: Phase 2 以降の監査・是正計画・実装判断の基準

---

## 1. 目的

本ドキュメントは、PhotoGeoExplorer における理想的な MVVM アーキテクチャを明文化する。
目的はカバレッジの増加ではなく、**ViewModel と Service を単体テスト可能にすること**である。

本ドキュメントは実装案ではなく、Phase 2 の監査で Yes/No 判定に使えるルールセットとして扱う。

---

## 2. アーキテクチャ原則

1. UI とアプリケーションロジックを分離する。
2. ViewModel は UI フレームワーク非依存とする。
3. 外部依存は Service に閉じ込め、ViewModel は抽象に依存する。
4. MainWindow は Shell 責務に限定し、機能ロジックを保持しない。
5. 変更後も常にビルド可能で、段階的に差し替え可能な構造を維持する。

---

## 3. レイヤー責務

### 3.1 View

View は表示と入力受付だけを担当する。

許可:

- XAML による見た目の定義
- `*.xaml.cs` でのイベント受け取り
- ダイアログ、フライアウト、ピッカーの表示
- `XamlRoot`、`DispatcherQueue`、WinRT Interop など UI 固有 API の使用
- ViewModel へのコマンド実行、または UI コールバックの受け渡し

禁止:

- 複数要素を走査する業務ループ
- 成功件数、失敗件数、スキップ件数の集計
- パス比較、ファイル名生成、入力値正規化
- 永続化、ファイル移動、EXIF 更新などの副作用処理
- リトライ、タイムアウト、キャンセル制御の本体

### 3.2 ViewModel

ViewModel は画面状態とユースケース進行を担当する。

許可:

- バインディング用状態の公開
- コマンド公開
- Service 呼び出し順序の制御
- `CancellationToken` の生成と受け渡し
- 正常系、異常系、キャンセル時の状態遷移管理

禁止:

- `FrameworkElement`、`Window`、`ContentDialog`、`XamlRoot` など UI 型への依存
- `File.Open`、`Directory.*` など直接 I/O
- `new` による具象 Service の生成
- Service Locator の利用
- static 状態への依存

### 3.3 Model / Core

Model はアプリケーション固有データと純粋ルールを表す。

許可:

- 値オブジェクト、DTO、純粋なドメインルール
- UI 非依存、I/O 非依存の計算

禁止:

- UI 参照
- インフラ実装参照
- ログ出力やファイルアクセスなどの副作用

### 3.4 Service

Service は外部依存と副作用を隔離する。

対象:

- ファイルシステム
- EXIF 読み書き
- マップタイルや永続設定
- OS 連携
- ダイアログ表示の抽象化

ルール:

- ViewModel からはインターフェース経由で利用する
- UI API を必要とする場合は UI 専用 Service として境界を明示する
- 返却値は ViewModel が検証可能な形にする

### 3.5 State / Shell

`WorkspaceState` などの共有状態は、ペイン間の疎結合な連携に限定する。

`MainWindow` は以下の責務に限定する。

- レイアウト
- DI 初期化
- ライフサイクル
- Pane 配置

`MainWindow` に機能別ユースケースを実装してはならない。

---

## 4. 依存ルール

### 4.1 許可される依存

| From | To | 許可 |
|---|---|---|
| View | ViewModel | Yes |
| View | UI 専用 Service | Yes |
| ViewModel | Service Interface | Yes |
| ViewModel | Model / Core | Yes |
| Service | Model / Core | Yes |
| Shell | View / ViewModel / State | Yes |
| State | Model / Core | Yes |

### 4.2 禁止される依存

| From | To | 許可 |
|---|---|---|
| ViewModel | View / UI 型 | No |
| ViewModel | 具象 Service 実装 | No |
| ViewModel | `System.IO` 直接呼び出し | No |
| Model / Core | ViewModel / View / Service 実装 | No |
| Service | View | No |
| MainWindow | 個別機能の詳細ロジック | No |

### 4.3 依存方向

依存方向は原則として以下に限定する。

`View -> ViewModel -> Service Interface -> Service Implementation`

横断連携が必要な場合は、直接参照ではなく `WorkspaceState` または専用インターフェースを使う。

---

## 5. ViewModel 制約

ViewModel が使用可能な API は以下に限定する。

- `INotifyPropertyChanged`
- `ICommand`
- `Task` / `CancellationToken`
- 純粋な .NET 型
- アプリ内で抽象化された Service Interface

ViewModel で禁止する具体例:

- `DispatcherQueue`
- `DependencyObject`
- `ContentDialog`
- `FileOpenPicker`
- `FolderPicker`
- `MapControl`
- `WebView2`
- `Application.Current`
- `App.MainWindow`

判断基準:

- 単体テストプロジェクトから、その型を参照せずに ViewModel を生成できなければ NG。
- ヘッドレス環境で実行できない処理を ViewModel に書いていれば NG。

---

## 6. テスタビリティ要件

1. ViewModel はテストで直接 new できること。
2. コンストラクタ引数だけで依存関係が明示されること。
3. 外部依存はすべてモックまたはフェイクで差し替え可能であること。
4. 非同期処理の完了条件が `Task` として観測可能であること。
5. 成功、失敗、キャンセル時の状態が公開プロパティまたは戻り値で観測できること。
6. 例外を握りつぶさず、テストで期待結果を判定できること。

NG パターン:

- hidden dependency
- `new Service()` の直書き
- static ヘルパーに副作用を埋め込む構造
- fire-and-forget
- イベントハンドラ内部に閉じた重要ロジック

---

## 7. 非同期設計ルール

1. 非同期メソッドは原則 `Task` または `Task<T>` を返す。
2. `async void` は UI イベントハンドラ以外で禁止する。
3. fire-and-forget は禁止する。
4. キャンセル可能な長時間処理は `CancellationToken` を受け取る。
5. 例外は呼び出し元に伝播させ、ViewModel が表示用状態へ変換する。
6. `Task.Run` は CPU バウンド隔離が必要な場合のみ許可し、理由を説明できること。
7. 進捗報告は UI 部品ではなく、状態値または通知インターフェース経由で行う。

---

## 8. DI 方針

### 8.1 インターフェース導入基準

次のいずれかを満たす依存はインターフェース化する。

- ファイルシステム、ネットワーク、OS、時刻、設定、EXIF など外部依存を持つ
- テストで成功系と失敗系を切り替えたい
- 将来差し替え実装が発生しうる
- UI とロジックの境界を跨ぐ

### 8.2 導入しないもの

- 値オブジェクト
- 純粋関数のみを持つ stateless な小規模 helper
- テスト差し替え不要で副作用を持たない計算ロジック

### 8.3 注入ルール

1. 注入方式はコンストラクタインジェクションを標準とする。
2. optional 依存を避ける。
3. 依存が多すぎる場合は責務分割を先に検討する。
4. ViewModel の DI 不能を回避するため、Service Locator は禁止する。

---

## 9. PhotoGeoExplorer 向け補足ルール

1. `MainWindow.xaml.cs` は Shell に留め、機能ロジック追加先にしない。
2. 各 Pane のユースケース制御は `Panes/*ViewModel` に置く。
3. ファイル操作、EXIF、設定保存、ヘルプ表示、OS 連携は Service に隔離する。
4. ペイン間連携は `WorkspaceState` または専用抽象経由で行う。
5. `PhotoGeoExplorer.Core` には UI 非依存ロジックを優先配置する。

---

## 10. Yes/No チェックリスト

### 10.1 View

- View のコードビハインドに業務ループがないか: Yes
- View のコードビハインドに `System.IO` の直接呼び出しがないか: Yes
- View がダイアログ表示後の処理本体を ViewModel に委譲しているか: Yes

### 10.2 ViewModel

- ViewModel が UI 型を参照していないか: Yes
- ViewModel が具象 Service を `new` していないか: Yes
- ViewModel が static な外部依存に直接触れていないか: Yes
- ViewModel が単体テストから生成可能か: Yes
- ViewModel の主要ユースケースが `Task` として完了待機可能か: Yes
- ViewModel が成功、失敗、キャンセル状態を観測可能にしているか: Yes

### 10.3 Service

- 副作用処理が Service に集約されているか: Yes
- Service が UI ではなく抽象的な結果を返しているか: Yes
- Service 実装が ViewModel に直接依存していないか: Yes

### 10.4 構造

- `MainWindow` が Shell 責務に限定されているか: Yes
- ペイン間連携が直接参照ではなく共有状態または抽象経由か: Yes
- `PhotoGeoExplorer.Core` に UI 依存が混入していないか: Yes

### 10.5 非同期

- `async void` が UI イベントハンドラ以外に存在しないか: Yes
- fire-and-forget が存在しないか: Yes
- 長時間処理にキャンセル経路があるか: Yes
- 非同期例外が黙殺されていないか: Yes

---

## 11. Phase 2 への受け渡し

Phase 2 では、本ドキュメントを監査基準として扱う。
監査時は「理想とのギャップ」を列挙し、実装都合による曖昧な免責は認めない。

---

## 12. 例外ポリシー

本ルールセットに例外は認めない。

- 「既存コードの都合」は免責理由にならない
- 「段階的対応」は Phase 3 の Issue 分割で扱う
- Phase 2 では違反はすべて違反として記録する

違反を許容する場合は「例外」ではなく「未対応Issue」として扱うこと。

---

## 13. アンチパターン

以下は明確な設計違反とみなす。

- ViewModel 内でのダイアログ表示
- ViewModel 内での `File` / `Directory` 操作
- static ユーティリティに副作用を隠す
- `async void` による処理本体
- イベントハンドラに閉じた業務ロジック
- Service を経由しない外部アクセス

これらを検出した場合、重大度 High 以上として扱う。
