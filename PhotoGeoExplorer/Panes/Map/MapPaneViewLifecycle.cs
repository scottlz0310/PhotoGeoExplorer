namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// View の Loaded / Unloaded 通知から、ツリー上で読み込み済みかどうかと解除処理の要否を判断する。
/// 同じインスタンスをホストへ再追加すると Loaded が Unloaded より先に届くことがあり、
/// 後から届いた古い Unloaded で解除すると ViewModel から切り離されたまま復帰できない (#278)。
/// </summary>
internal sealed class MapPaneViewLifecycle
{
    /// <summary>
    /// ツリー上で読み込み済みとみなしているか
    /// </summary>
    public bool IsViewLoaded { get; private set; }

    public void NotifyLoaded()
    {
        IsViewLoaded = true;
    }

    /// <summary>
    /// Unloaded 通知を受けたときに解除処理を実行すべきかを返す
    /// </summary>
    /// <param name="isLoadedNow">通知を処理する時点で、View が既にツリーへ戻っているか（FrameworkElement.IsLoaded）</param>
    /// <returns>解除処理を実行する場合は true。古い通知として無視する場合は false</returns>
    public bool TryNotifyUnloaded(bool isLoadedNow)
    {
        if (isLoadedNow)
        {
            return false;
        }

        IsViewLoaded = false;
        return true;
    }
}
