using System;
using System.Threading.Tasks;
using Mapsui;
using Mapsui.Projections;
using PhotoGeoExplorer.Services;
using Windows.Foundation;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// EXIF/GPS 位置ピックの状態機械と取得フローを担当するハンドラ。
/// マップ有無の判定・ステータス表示の退避/復元は UI 依存のためコールバックで注入する。
/// </summary>
internal sealed class MapExifLocationPicker : IExifLocationPicker
{
    private readonly Func<bool> _canPick;
    private readonly Func<bool> _hideMapStatus;
    private readonly Action _restoreMapStatus;

    private TaskCompletionSource<(double Latitude, double Longitude)?>? _pendingPick;
    private bool _isPointerActive;
    private Point? _pointerStart;
    private bool _restoreStatusAfterPick;

    public MapExifLocationPicker(Func<bool> canPick, Func<bool> hideMapStatus, Action restoreMapStatus)
    {
        _canPick = canPick ?? throw new ArgumentNullException(nameof(canPick));
        _hideMapStatus = hideMapStatus ?? throw new ArgumentNullException(nameof(hideMapStatus));
        _restoreMapStatus = restoreMapStatus ?? throw new ArgumentNullException(nameof(restoreMapStatus));
    }

    public bool CanPickExifLocation => _canPick();

    public bool IsPicking { get; private set; }

    /// <summary>ピックのためにステータス表示を退避中で、完了/キャンセル時に復元が必要な状態。</summary>
    public bool IsStatusRestorePending => _restoreStatusAfterPick;

    public Task<(double Latitude, double Longitude)?> PickExifLocationAsync()
    {
        if (!CanPickExifLocation)
        {
            return Task.FromResult<(double Latitude, double Longitude)?>(null);
        }

        if (_pendingPick is not null)
        {
            return _pendingPick.Task;
        }

        IsPicking = true;
        _restoreStatusAfterPick = _hideMapStatus();
        _pendingPick = new TaskCompletionSource<(double Latitude, double Longitude)?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        return _pendingPick.Task;
    }

    /// <summary>ピック中のポインタ押下を処理する。戻り値はイベントを Handled にすべきかどうか。</summary>
    public bool HandlePointerPressed(bool isLeftButtonPressed, bool isRightButtonPressed, Point position)
    {
        if (isRightButtonPressed)
        {
            Cancel();
            return true;
        }

        if (!isLeftButtonPressed)
        {
            return false;
        }

        _isPointerActive = true;
        _pointerStart = position;
        return false;
    }

    /// <summary>
    /// ピック中のポインタ解放を処理する。押下位置からの移動がしきい値内（クリック扱い）であれば
    /// ワールド座標を経緯度へ変換してピックを完了する。戻り値はイベントを Handled にすべきかどうか。
    /// </summary>
    public bool HandlePointerReleased(Point currentPosition, Func<MPoint?> getWorldPosition)
    {
        ArgumentNullException.ThrowIfNull(getWorldPosition);

        if (!_isPointerActive)
        {
            return false;
        }

        _isPointerActive = false;
        var startPoint = _pointerStart;
        _pointerStart = null;
        if (startPoint is null)
        {
            return false;
        }

        if (!MapPaneSelectionHelper.IsPointerMovementWithinThreshold(startPoint.Value, currentPosition))
        {
            return false;
        }

        var worldPosition = getWorldPosition();
        if (worldPosition is null)
        {
            return false;
        }

        var lonLat = SphericalMercator.ToLonLat(worldPosition);
        Complete(lonLat.Y, lonLat.X);
        return true;
    }

    /// <summary>ポインタキャプチャ喪失時の処理。ドラッグ追跡のみ破棄し、ピック自体は継続する。</summary>
    public void HandlePointerCaptureLost()
    {
        _isPointerActive = false;
        _pointerStart = null;
    }

    public void Cancel()
    {
        CompleteCore(null);
    }

    private void Complete(double latitude, double longitude)
    {
        CompleteCore((latitude, longitude));
    }

    private void CompleteCore((double Latitude, double Longitude)? result)
    {
        if (!IsPicking)
        {
            return;
        }

        IsPicking = false;
        _isPointerActive = false;
        _pointerStart = null;
        RestoreMapStatus();
        var pendingPick = _pendingPick;
        _pendingPick = null;
        pendingPick?.TrySetResult(result);
    }

    private void RestoreMapStatus()
    {
        if (!_restoreStatusAfterPick)
        {
            return;
        }

        _restoreStatusAfterPick = false;
        _restoreMapStatus();
    }
}
