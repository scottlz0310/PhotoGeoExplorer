using System.Threading.Tasks;

namespace PhotoGeoExplorer.ViewModels;

/// <summary>
/// Pane ViewModel の基底クラス
/// 共通的な振る舞いと状態管理を提供
/// </summary>
internal abstract class PaneViewModelBase : BindableBase, IPaneViewModel
{
    private string _title = string.Empty;
    private bool _isActive;
    private bool _isInitialized;

    /// <inheritdoc/>
    public string Title
    {
        get => _title;
        protected set => SetProperty(ref _title, value);
    }

    /// <inheritdoc/>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetProperty(ref _isActive, value))
            {
                OnActiveChanged();
            }
        }
    }

    /// <summary>
    /// Paneが初期化済みかどうか
    /// </summary>
    protected bool IsInitialized => _isInitialized;

    /// <inheritdoc/>
    public virtual async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        await OnInitializeAsync().ConfigureAwait(false);
        _isInitialized = true;
    }

    /// <inheritdoc/>
    public virtual void Cleanup()
    {
        OnCleanup();
        _isInitialized = false;
    }

    /// <summary>
    /// 派生クラスで初期化処理を実装
    /// </summary>
    protected virtual Task OnInitializeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 派生クラスでクリーンアップ処理を実装
    /// </summary>
    protected virtual void OnCleanup()
    {
    }

    /// <summary>
    /// IsActiveが変更されたときの処理
    /// </summary>
    protected virtual void OnActiveChanged()
    {
    }
}
