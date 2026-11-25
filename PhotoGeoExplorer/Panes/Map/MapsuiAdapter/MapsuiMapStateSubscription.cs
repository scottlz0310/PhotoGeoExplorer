using System;
using Mapsui;
using Mapsui.Fetcher;
using Mapsui.Layers;

namespace PhotoGeoExplorer.Panes.Map.MapsuiAdapter;

/// <summary>
/// 地図の表示状態（データ・視点・レイヤー構成）の変化を、1 つの通知にまとめて中継する購読。破棄すると購読を外す。
/// </summary>
/// <remarks>
/// 通知は、アニメーション中などは UI 以外のスレッドから呼ばれる。
/// </remarks>
internal sealed class MapsuiMapStateSubscription : IDisposable
{
    private readonly Mapsui.Map _map;
    private readonly Action _onChanged;

    internal MapsuiMapStateSubscription(MapsuiMapSurface surface, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(onChanged);

        _map = surface.Map;
        _onChanged = onChanged;
        _map.DataChanged += OnDataChanged;
        _map.Navigator.ViewportChanged += OnViewportChanged;
        _map.Layers.Changed += OnLayersChanged;
    }

    public void Dispose()
    {
        _map.DataChanged -= OnDataChanged;
        _map.Navigator.ViewportChanged -= OnViewportChanged;
        _map.Layers.Changed -= OnLayersChanged;
    }

    private void OnDataChanged(object? sender, DataChangedEventArgs e) => _onChanged();

    private void OnViewportChanged(object? sender, ViewportChangedEventArgs e) => _onChanged();

    private void OnLayersChanged(object sender, LayerCollectionChangedEventArgs args) => _onChanged();
}
