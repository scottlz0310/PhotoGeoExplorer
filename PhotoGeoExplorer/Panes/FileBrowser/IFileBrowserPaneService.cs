using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Panes.FileBrowser;

/// <summary>
/// ファイルブラウザPane専用のサービスインターフェース
/// ファイルシステム操作、ナビゲーション履歴、ソート処理を分離
/// </summary>
internal interface IFileBrowserPaneService
{
    /// <summary>
    /// 指定フォルダのファイル一覧を読み込む
    /// </summary>
    /// <param name="folderPath">フォルダパス</param>
    /// <param name="showImagesOnly">画像のみ表示するか</param>
    /// <param name="searchText">検索テキスト</param>
    /// <returns>PhotoListItem のリスト</returns>
    Task<List<PhotoListItem>> LoadFolderAsync(string folderPath, bool showImagesOnly, string? searchText);

    /// <summary>
    /// ブレッドクラムを生成する
    /// </summary>
    /// <param name="folderPath">フォルダパス</param>
    /// <returns>ブレッドクラムセグメントのコレクション</returns>
    ObservableCollection<BreadcrumbSegment> GetBreadcrumbs(string folderPath);

    /// <summary>
    /// 指定された列とソート方向でアイテムをソートする
    /// </summary>
    /// <param name="items">ソート対象のアイテム</param>
    /// <param name="column">ソート列</param>
    /// <param name="direction">ソート方向</param>
    /// <returns>ソート済みのリスト</returns>
    List<PhotoListItem> ApplySort(IEnumerable<PhotoListItem> items, FileSortColumn column, SortDirection direction);

    /// <summary>
    /// ファイルパスから一覧内の対応項目を取得する
    /// </summary>
    /// <param name="items">検索対象の一覧</param>
    /// <param name="filePath">検索対象のファイルパス</param>
    /// <returns>一致する項目。見つからない場合は null</returns>
    PhotoListItem? FindItemByFilePath(IEnumerable<PhotoListItem> items, string filePath);

    /// <summary>
    /// ファイルパス一覧を一覧項目へ解決する
    /// </summary>
    /// <param name="items">検索対象の一覧</param>
    /// <param name="filePaths">検索対象のファイルパス一覧</param>
    /// <returns>一致した項目の一覧（filePaths の順序を維持）</returns>
    IReadOnlyList<PhotoListItem> ResolveItemsByFilePaths(IEnumerable<PhotoListItem> items, IReadOnlyList<string> filePaths);

    /// <summary>
    /// ナビゲーション履歴を戻る
    /// </summary>
    /// <param name="currentPath">現在のフォルダパス</param>
    /// <returns>戻り先のフォルダパス。戻れない場合は null</returns>
    string? NavigateBack(string currentPath);

    /// <summary>
    /// ナビゲーション履歴を進む
    /// </summary>
    /// <param name="currentPath">現在のフォルダパス</param>
    /// <returns>進み先のフォルダパス。進めない場合は null</returns>
    string? NavigateForward(string currentPath);

    /// <summary>
    /// 戻る履歴にパスを追加する
    /// </summary>
    /// <param name="path">追加するパス</param>
    void PushToBackStack(string path);

    /// <summary>
    /// 進む履歴にパスを追加する
    /// </summary>
    /// <param name="path">追加するパス</param>
    void PushToForwardStack(string path);

    /// <summary>
    /// 進む履歴をクリアする
    /// </summary>
    void ClearForwardStack();

    /// <summary>
    /// 戻る履歴が存在するか
    /// </summary>
    bool CanNavigateBack { get; }

    /// <summary>
    /// 進む履歴が存在するか
    /// </summary>
    bool CanNavigateForward { get; }
}
