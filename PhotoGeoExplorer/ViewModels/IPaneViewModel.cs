using System.ComponentModel;

namespace PhotoGeoExplorer.ViewModels;

/// <summary>
/// Paneの ViewModel が実装すべき基本インターフェース
/// </summary>
internal interface IPaneViewModel : INotifyPropertyChanged
{
    /// <summary>
    /// Paneのタイトル（表示切替時などに使用）
    /// </summary>
    string Title { get; }

    /// <summary>
    /// Paneがアクティブかどうか
    /// </summary>
    bool IsActive { get; set; }

    /// <summary>
    /// Paneの初期化処理（非同期）
    /// </summary>
    System.Threading.Tasks.Task InitializeAsync();

    /// <summary>
    /// Paneのクリーンアップ処理
    /// </summary>
    void Cleanup();
}
