#!/bin/bash

# v1.5.0 Issue作成スクリプト
# このスクリプトは8件のIssueをGitHubに一括作成します

set -e

# リポジトリ指定
REPO="scottlz0310/PhotoGeoExplorer"

echo "=========================================="
echo "v1.5.0 Issue作成スクリプト"
echo "=========================================="
echo ""

# GitHub CLIの認証確認
if ! gh auth status >/dev/null 2>&1; then
    echo "エラー: GitHub CLIで認証されていません。"
    echo "以下のコマンドで認証してください："
    echo "  gh auth login"
    exit 1
fi

echo "✓ GitHub CLI認証確認完了"
echo ""

# マイルストーンの確認
echo "v1.5.0 マイルストーンを確認中..."
if ! gh api "repos/${REPO}/milestones" --jq '.[].title' | grep -q "^v1.5.0$"; then
    echo "⚠ v1.5.0 マイルストーンが見つかりません。作成しますか？ (y/n)"
    read -r response
    if [[ "$response" =~ ^([yY][eE][sS]|[yY])$ ]]; then
        gh api -X POST "repos/${REPO}/milestones" -f title="v1.5.0" -f description="次期メジャーリリース"
        echo "✓ v1.5.0 マイルストーンを作成しました"
    else
        echo "エラー: v1.5.0 マイルストーンが必要です"
        exit 1
    fi
else
    echo "✓ v1.5.0 マイルストーン確認完了"
fi
echo ""

# 重複チェック用関数
issue_exists() {
    local title="$1"
    gh issue list --repo "${REPO}" --search "in:title \"${title}\"" --state all --limit 1 --json title --jq '.[].title' | grep -q "^${title}$"
}

# Issue作成前の確認
echo "既存のIssueをチェック中..."

# 重複チェック用関数
issue_exists() {
    local title="$1"
    gh issue list --repo "${REPO}" --search "in:title \"${title}\"" --state all --limit 1 --json title --jq '.[].title' | grep -q "^${title}$"
}

# Issue作成前の確認
echo "既存のIssueをチェック中..."
echo ""

# Issue作成開始
echo "Issueを作成します..."
echo ""

# Issue 1
ISSUE1_TITLE="空のフォルダを開くとクラッシュすることがある"
echo "[1/8] Issue 1: ${ISSUE1_TITLE}"
if issue_exists "${ISSUE1_TITLE}"; then
    echo "⊘ スキップ: 同タイトルのIssueが既に存在します"
else
    gh issue create --repo "${REPO}" \
      --title "${ISSUE1_TITLE}" \
      --body "## 概要
空のフォルダを開こうとした際、アプリがクラッシュする不具合が稀に発生します。

## 詳細
- 再現性は高くありませんが、発生時にログ収集を予定
- Store版でもユーザーログを集められるか検討中

## 関連ファイル
- \`PhotoGeoExplorer/ViewModels/MainViewModel.cs\` (LoadFolderAsync メソッド)
- \`PhotoGeoExplorer.Core/Services/FileSystemService.cs\`

## 優先度
High" \
      --label "bug" \
      --assignee "scottlz0310" \
      --milestone "v1.5.0"
    echo "✓ Issue 1 作成完了"
fi
echo ""

# Issue 2
ISSUE2_TITLE="戻る・進むボタンが正しく機能しない"
echo "[2/8] Issue 2: ${ISSUE2_TITLE}"
if issue_exists "${ISSUE2_TITLE}"; then
    echo "⊘ スキップ: 同タイトルのIssueが既に存在します"
else
    gh issue create --repo "${REPO}" \
      --title "${ISSUE2_TITLE}" \
      --body "## 概要
「戻る」「進む」ボタンの操作時に、意図通りに画面遷移しない不具合が発生しています。常に再現します。

## 詳細
現状、ナビゲーション履歴機能の実装が必要です。

## 関連ファイル
- \`PhotoGeoExplorer/ViewModels/MainViewModel.cs\` (ナビゲーション関連メソッド)
- \`PhotoGeoExplorer/MainWindow.xaml.cs\` (OnNavigateUpClicked など)

## 優先度
High" \
      --label "bug" \
      --assignee "scottlz0310" \
      --milestone "v1.5.0"
    echo "✓ Issue 2 作成完了"
fi
echo ""

# Issue 3
ISSUE3_TITLE="lastfolderpath のパスリカバリ動作を改善する"
echo "[3/8] Issue 3: ${ISSUE3_TITLE}"
if issue_exists "${ISSUE3_TITLE}"; then
    echo "⊘ スキップ: 同タイトルのIssueが既に存在します"
else
    gh issue create --repo "${REPO}" \
      --title "${ISSUE3_TITLE}" \
      --body "## 概要
lastfolderpathに保存されているパスが無効な場合、自動的に picture フォルダにフォールバックしています。

## 改善案
親フォルダに順次フォールバックするようにして、ユーザーの作業ディレクトリ復元性を向上させる。

## 関連ファイル
- \`PhotoGeoExplorer.Core/Services/SettingsService.cs\`
- \`PhotoGeoExplorer.Core/Models/AppSettings.cs\` (LastFolderPath)
- \`PhotoGeoExplorer/MainWindow.xaml.cs\` (ApplySettingsAsync)

## 優先度
High" \
      --label "enhancement" \
      --assignee "scottlz0310" \
      --milestone "v1.5.0"
    echo "✓ Issue 3 作成完了"
fi
echo ""

# Issue 4
ISSUE4_TITLE="フォルダ読み込み時のパフォーマンス改善"
echo "[4/8] Issue 4: ${ISSUE4_TITLE}"
if issue_exists "${ISSUE4_TITLE}"; then
    echo "⊘ スキップ: 同タイトルのIssueが既に存在します"
else
    gh issue create --repo "${REPO}" \
      --title "${ISSUE4_TITLE}" \
      --body "## 概要
大量のファイルが含まれるフォルダやクラウドドライブの読み込みで遅延が発生することがあります。

## 改善案
サムネイルが全て生成されるのを待つのではなく、プレースホルダーアイコンを先に表示し、その後サムネイルを非同期で読み込み表示させる方式に改善します。

## 関連ファイル
- \`PhotoGeoExplorer.Core/Services/ThumbnailService.cs\`
- \`PhotoGeoExplorer/ViewModels/MainViewModel.cs\` (LoadFolderAsync)
- \`PhotoGeoExplorer.Core/Services/FileSystemService.cs\`

## 優先度
Medium" \
      --label "performance" \
      --label "enhancement" \
      --assignee "scottlz0310" \
      --milestone "v1.5.0"
    echo "✓ Issue 4 作成完了"
fi
echo ""

# Issue 5
ISSUE5_TITLE="ファイルビュー画面でマウスオン時に詳細情報を表示する"
echo "[5/8] Issue 5: ${ISSUE5_TITLE}"
if issue_exists "${ISSUE5_TITLE}"; then
    echo "⊘ スキップ: 同タイトルのIssueが既に存在します"
else
    gh issue create --repo "${REPO}" \
      --title "${ISSUE5_TITLE}" \
      --body "## 概要
ファイルビュー画面でファイルにマウスオーバー（オン）した際に、詳細情報（EXIFや撮影日時、サイズ等）をツールチップ等で表示する機能を追加します。

## 目的
ユーザーの利便性向上

## 関連ファイル
- \`PhotoGeoExplorer/MainWindow.xaml.cs\` (ファイルリストイベントハンドラ)
- \`PhotoGeoExplorer.Core/ViewModels/FileViewMode.cs\`
- XAML ファイル (GridView/ListView テンプレートに ToolTip 追加が必要)

## 優先度
Medium" \
      --label "enhancement" \
      --assignee "scottlz0310" \
      --milestone "v1.5.0"
    echo "✓ Issue 5 作成完了"
fi
echo ""

# Issue 6
ISSUE6_TITLE="Windows Store製品ページのSEO対策強化"
echo "[6/8] Issue 6: ${ISSUE6_TITLE}"
if issue_exists "${ISSUE6_TITLE}"; then
    echo "⊘ スキップ: 同タイトルのIssueが既に存在します"
else
    gh issue create --repo "${REPO}" \
      --title "${ISSUE6_TITLE}" \
      --body "## 概要
Windows Store版アプリの説明エリアやキーワード等、ストアページのSEO対策を行う必要があります。

## 改善案
listingData.csvのキーワード補足や説明文見直し、スクリーンショットキャプションの充実化など。

## 関連ファイル
- \`PhotoGeoExplorer/Assets/propose/listingData-9P0WNR54441B-1152921505700361349.csv\` (350行以降のカラム数調整済み)

## 優先度
Low" \
      --label "enhancement" \
      --label "seo" \
      --assignee "scottlz0310" \
      --milestone "v1.5.0"
    echo "✓ Issue 6 作成完了"
fi
echo ""

# Issue 7
ISSUE7_TITLE="地図上で複数写真を矩形選択できる機能を追加する"
echo "[7/8] Issue 7: ${ISSUE7_TITLE}"
if issue_exists "${ISSUE7_TITLE}"; then
    echo "⊘ スキップ: 同タイトルのIssueが既に存在します"
else
    gh issue create --repo "${REPO}" \
      --title "${ISSUE7_TITLE}" \
      --body "## 概要
複数選択時、地図上で矩形選択エリアを作り、そのエリア内の近い場所の写真を一括選択できる新機能を実装します。

## 目的
大量写真の効率的な管理のため

## 関連ファイル
- \`PhotoGeoExplorer/MainWindow.xaml.cs\` (UpdateMapFromSelectionAsync, SetMapMarkers)
- Mapsui 地図コントロール (新機能実装が必要)

## 優先度
Low" \
      --label "enhancement" \
      --label "feature" \
      --assignee "scottlz0310" \
      --milestone "v1.5.0"
    echo "✓ Issue 7 作成完了"
fi
echo ""

# Issue 8
ISSUE8_TITLE="EXIF情報（撮影日時等）の編集機能を追加する"
echo "[8/8] Issue 8: ${ISSUE8_TITLE}"
if issue_exists "${ISSUE8_TITLE}"; then
    echo "⊘ スキップ: 同タイトルのIssueが既に存在します"
else
    gh issue create --repo "${REPO}" \
      --title "${ISSUE8_TITLE}" \
      --body "## 概要
画像ファイルのEXIF情報、特に撮影日時を編集可能にする機能を実装します。

## 補足
あわせてファイル更新日時を撮影日時に揃えるオプションも検討中。

## 関連ファイル
- \`PhotoGeoExplorer.Core/Services/ExifService.cs\` (現在は読み取りのみ)
- 新規実装が必要: EXIF書き込み機能 (MetadataExtractorは読み取り専用のため、別ライブラリが必要)

## 優先度
Low" \
      --label "enhancement" \
      --label "feature" \
      --assignee "scottlz0310" \
      --milestone "v1.5.0"
    echo "✓ Issue 8 作成完了"
fi
echo ""

echo "=========================================="
echo "✓ すべてのIssueの作成が完了しました！"
echo "=========================================="
echo ""
echo "作成されたIssueを確認するには："
echo "  gh issue list --milestone v1.5.0"
echo ""
