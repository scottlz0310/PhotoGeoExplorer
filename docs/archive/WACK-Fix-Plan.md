# PhotoGeoExplorer WACK準拠修正 - 実装計画

## 概要

WACKテストで24件失敗（必須13件、オプション11件）している問題を、サブブランチで段階的に修正します。
調査の結果、**完全な移植は不要**で、Package.appxmanifestの修正を中心とした比較的小規模な変更で大部分の問題が解決できる見込みです。

## ステータス

- 対応完了: Required 0 で合格 (Optional [88] のみ)
- 参考: `docs/WACK-TestResults.md`
- この計画は履歴として残し、次回の大きな修正時のみ見直します。

---

## 実装フェーズ

### Phase 1: 現状のコミット（10分）

**目的**: wackスクリプト整備と関連設定をmainブランチにコミット

**実行コマンド**:
```powershell
# 変更内容の確認
git status
git diff .gitignore
git diff PhotoGeoExplorer/PhotoGeoExplorer.csproj
git diff PhotoGeoExplorer.Core/PhotoGeoExplorer.Core.csproj

# ステージングとコミット
git add .gitignore PhotoGeoExplorer/PhotoGeoExplorer.csproj PhotoGeoExplorer.Core/PhotoGeoExplorer.Core.csproj PhotoGeoExplorer/app.manifest wack/
git commit -m "Chore: WACKテスト環境を整備し、ビルド設定を最適化

- WACKスクリプトをwackフォルダに集約（run-wack.ps1, analyze-wack.ps1, investigate-wack.ps1）
- .gitignoreにwack-report.xmlを追加
- app.manifestを追加してDPI awareness設定を明示化
- Releaseビルド時のデバッグシンボル出力を無効化（DebugType=none, DebugSymbols=false）

これにより、WACK準拠への準備が整い、パッケージサイズも最適化されました。"
```

---

### Phase 2: サブブランチの作成（2分）

**目的**: mainブランチを保護し、安全に修正作業を実施

**実行コマンド**:
```powershell
git checkout -b fix/wack-compliance
git branch  # 確認
```

**ブランチ名の理由**: `fix/wack-compliance` - WACK準拠を達成することを明確に示す

---

### Phase 3: Package.appxmanifest の修正（15分）⭐最重要

**目的**: DPI Awareness宣言とアセット参照の修正

#### 修正内容

**ファイル**: `C:\workspace\PhotoGeoExplorer\PhotoGeoExplorer\Package.appxmanifest`

**修正1: uap10名前空間の追加（2-6行目）**
```xml
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:uap10="http://schemas.microsoft.com/appx/manifest/uap/windows10/10"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  IgnorableNamespaces="uap uap10 rescap">
```

**修正2: Square310x310Logo の正しい参照（29行目）**
```xml
Square310x310Logo="Assets\Square310x310Logo.png"
```
（現在は誤って `Square150x150Logo.png` を参照している）

**修正3: Extensions セクションの追加（39行目の `</uap:VisualElements>` と 40行目の `</Application>` の間）**
```xml
      </uap:VisualElements>
      <Extensions>
        <uap10:Extension Category="windows.protocol">
          <uap10:Protocol Name="photogeoexplorer" />
        </uap10:Extension>
      </Extensions>
    </Application>
```

**修正の根拠**:
- WACKのDPI Awarenessテストは、Package.appxmanifestのExtensionsセクションで宣言を確認
- app.manifestの設定だけでは不十分（実行時には機能するが、WACK検証には不足）
- プロトコルハンドラは副作用がなく、将来的な機能拡張にも有用

---

### Phase 4: PhotoGeoExplorer.csproj の MSIX ビルド設定明示化（10分）

**目的**: MSIXパッケージングの動作を明示的に制御

**ファイル**: `C:\workspace\PhotoGeoExplorer\PhotoGeoExplorer\PhotoGeoExplorer.csproj`

**追加内容**（24行目の `</ApplicationManifest>` の後）:
```xml
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <!-- MSIX Packaging Configuration for WACK Compliance -->
    <GenerateAppxPackageOnBuild Condition="'$(Configuration)'=='Release' and '$(Platform)'=='x64'">true</GenerateAppxPackageOnBuild>
    <AppxPackageSigningEnabled>false</AppxPackageSigningEnabled>
    <AppxBundle>Always</AppxBundle>
    <AppxBundlePlatforms>x64</AppxBundlePlatforms>
  </PropertyGroup>
```

**効果**: Release/x64ビルド時に自動的にMSIXパッケージが生成され、ビルドプロセスが安定化

---

### Phase 5: ビルドとWACKテストサイクル（30-60分）

**目的**: 修正の効果を検証し、必要に応じて追加修正

#### 5.1 初回ビルドとWACKテスト

```powershell
# クリーンビルド
dotnet clean PhotoGeoExplorer.sln -c Release
dotnet restore PhotoGeoExplorer.sln

# MSIXパッケージ生成
dotnet publish PhotoGeoExplorer/PhotoGeoExplorer.csproj -c Release -p:Platform=x64 -p:WindowsPackageType=MSIX

# WACKテスト実行
.\wack\run-wack.ps1

# 結果分析
.\wack\analyze-wack.ps1
```

**期待される改善**:
- ✅ DPI Awareness テストが PASS になる可能性が高い
- ✅ アプリマニフェスト検証エラーが減少
- ✅ ブランディングテストが改善（Square310x310Logo参照修正）

#### 5.2 結果に基づく追加修正パターン

**パターンA: Windows Runtime メタデータエラーが残る場合**
- 原因: サードパーティライブラリ（Mapsui, ImageSharp, MetadataExtractor）のWinMD問題
- 対処: ライブラリ側の問題のため完全修正は困難。Microsoft Store提出時に説明文を添付

**パターンB: runFullTrust 機能の説明が必要な場合**
- 対処: Store提出時に以下の理由を説明
  - ファイルシステムアクセス（写真の読み込み）
  - EXIFメタデータ抽出
  - 地図タイルキャッシュ管理
  - サムネイル生成

#### 5.3 反復サイクル

各修正後に以下を実行:
```powershell
dotnet publish PhotoGeoExplorer/PhotoGeoExplorer.csproj -c Release -p:Platform=x64 -p:WindowsPackageType=MSIX
.\wack\run-wack.ps1
.\wack\analyze-wack.ps1

# 改善が確認できた場合
git add .
git commit -m "Fix: [具体的な修正内容]"
```

---

### Phase 6: 最終検証とマージ準備（30分）

#### 6.1 WACK結果の評価基準

**PASS条件**:
- 必須テスト（Required）の失敗が0件、または説明可能な既知の問題のみ
- オプションテスト（Optional）は参考情報として扱う

**許容される失敗例**:
- サードパーティライブラリのWinMDメタデータ警告（説明文で対応可能）

#### 6.2 最終確認チェックリスト

```powershell
# アプリケーション起動確認
dotnet run --project PhotoGeoExplorer/PhotoGeoExplorer.csproj -c Release -p:Platform=x64

# 主要機能が動作するか手動テスト
# - フォルダ選択
# - 写真一覧表示
# - 地図表示
# - GPS情報の表示

# MSIXインストール確認
# PhotoGeoExplorer\AppPackages\PhotoGeoExplorer_*_Test\ 内の .msixbundle をダブルクリック
```

#### 6.3 ブランチのマージ

```powershell
# 最新のmainを取得
git checkout main
git pull origin main

# リベースしてマージ
git checkout fix/wack-compliance
git rebase main
git checkout main
git merge --no-ff fix/wack-compliance -m "Merge: WACK準拠修正を統合

Package.appxmanifestにuap10名前空間とExtensionsセクションを追加し、
DPI awareness宣言を明示化。WACK必須テストの主要な失敗項目を解決。"

# プッシュ
git push origin main

# 作業ブランチ削除（オプション）
git branch -d fix/wack-compliance
```

---

### Phase 7: Microsoft Store提出準備（次のステップ）

#### 必要なドキュメント

1. **プライバシーポリシー** ✅ 既存
   - 公開URL: https://scottlz0310.github.io/PhotoGeoExplorer/privacy-policy/

2. **Store説明文**（英語・日本語）
   - カテゴリ: Photo & video または Utilities & tools
   - 年齢区分: 3+（全年齢対象）
   - 価格: Free

3. **スクリーンショット**
   - 最低4枚、最大10枚（1920x1080または1366x768）
   - 既存: screenshot1.png, screenshot2.png

4. **Known Issues説明**（必要に応じて）
   ```
   WACK Test Notes:
   This application may show warnings for third-party library Windows Runtime metadata
   (Mapsui, ImageSharp, MetadataExtractor). These libraries are essential for the app's
   core functionality and do not affect user experience or security.

   The runFullTrust capability is required for:
   - User-initiated file system access for photo browsing
   - EXIF/GPS metadata extraction from photos
   - Local map tile caching for offline use
   - High-quality thumbnail generation
   ```

---

## Critical Files

実装時に最も重要なファイル:

1. **Package.appxmanifest** - `C:\workspace\PhotoGeoExplorer\PhotoGeoExplorer\Package.appxmanifest`
   - Extensions セクション追加
   - Square310x310Logo参照修正
   - uap10名前空間追加

2. **PhotoGeoExplorer.csproj** - `C:\workspace\PhotoGeoExplorer\PhotoGeoExplorer\PhotoGeoExplorer.csproj`
   - MSIX ビルド設定明示化

3. **WACKスクリプト** - `C:\workspace\PhotoGeoExplorer\wack\run-wack.ps1`, `analyze-wack.ps1`
   - 各修正後の効果測定

---

## 優先度とリスク評価

### 高優先度（必須）
1. ⭐ **Phase 3**: Package.appxmanifest の Extensions セクション追加
   - リスク: 低、効果: 大
2. ⭐ **Phase 3**: Square310x310Logo の参照修正
   - リスク: 非常に低、効果: 中
3. ⭐ **Phase 1**: 現在の変更をコミット
   - リスク: なし、効果: 作業の保護

### 中優先度（推奨）
4. **Phase 4**: .csproj の MSIX設定明示化
   - リスク: 低、効果: 中
5. **Phase 5**: 反復的なWACKテストサイクル
   - リスク: 低、効果: 大

---

## トラブルシューティング

### 問題1: ビルドエラー "uap10名前空間が認識されない"
**解決策**: Windows SDK 10.0.26100.0 以上をインストール

### 問題2: WACKテスト実行時に "Package could not be opened"
**解決策**: クリーンビルドと再パッケージング

### 問題3: Extensions セクション追加後もDPI Awareness テスト失敗
**解決策**: MSIXパッケージ内のAppxManifest.xmlを確認
```powershell
$tempDir = "$env:TEMP\PhotoGeoExplorer_Extract"
Expand-Archive -Path "PhotoGeoExplorer\AppPackages\PhotoGeoExplorer_*_Test\*.msix" -DestinationPath $tempDir -Force
notepad "$tempDir\AppxManifest.xml"
```

---

## 成功指標

### 短期目標（Phase 5完了時）
- WACK必須テストの失敗が **5件以下** に削減
- DPI Awareness テストが **PASS**
- アプリマニフェスト検証エラーが **0件**

### 中期目標（Phase 6完了時）
- WACK必須テストの失敗が **3件以下**（サードパーティライブラリ起因のみ）
- すべての失敗項目に説明文が準備済み
- mainブランチにマージ完了

### 長期目標（Microsoft Store提出後）
- Store認証を **1回の提出で通過**
- ユーザーからのWACK関連の不具合報告が **0件**

---

## 所要時間見積もり

- Phase 1: 10分
- Phase 2: 2分
- Phase 3: 15分
- Phase 4: 10分
- Phase 5.1: 20分
- Phase 5.2-5.3: 30-60分
- Phase 6: 30分

**合計**: 約2-3時間
