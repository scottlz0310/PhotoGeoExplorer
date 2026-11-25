using System;
using System.Collections.Generic;
using Mapsui;
using Mapsui.Fetcher;
using Mapsui.Layers;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia;
using Mapsui.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace PhotoGeoExplorer.Panes.Map.MapsuiAdapter;

/// <summary>
/// Mapsui の <c>MapControl</c> を包む表示用のコントロール。<c>MapControl</c> の型・イベント・描画フックはここに閉じ込め、
/// View は <see cref="IMapSurface"/> とこのコントロールの公開部分だけを使う（#362 §11.6-3）。
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "MapControl は XAML の要素として、このコントロールの寿命と同じ。従来も View が破棄していなかった（地図は表示面が破棄する）。")]
internal sealed class MapsuiMapView : UserControl
{
    private readonly Mapsui.UI.WinUI.MapControl _mapControl = new();
    private MapsuiMapSurface? _surface;

    internal MapsuiMapView()
    {
        _mapControl.SetMapRenderer(new ObservingMapRenderer(new MapRenderer(), OnRenderStarting));

        // 自動操作（UI Automation）が地図を見つける識別子。内側のコントロールへ付ける
        AutomationProperties.SetAutomationId(_mapControl, "MapControl");
        _mapControl.Info += OnMapInfo;
        Content = _mapControl;
    }

    /// <summary>
    /// 写真マーカーがクリックされたとき（UI スレッド）。
    /// </summary>
    internal event EventHandler<MapMarker>? MarkerClicked;

    /// <summary>
    /// 地図の表示状態（データ・視点・レイヤー）が変わったとき。アニメーション中などは UI 以外のスレッドから呼ばれる。
    /// </summary>
    internal event EventHandler? MapStateChanged;

    /// <summary>
    /// ポインタ入力を受ける要素。ポインタのイベントの購読・キャプチャ・フライアウトの基準位置に使う。
    /// </summary>
    internal FrameworkElement InputElement => _mapControl;

    /// <summary>
    /// 表示する表示面。<see cref="MapsuiMapSurface"/> だけを受け付ける。<see langword="null"/> にすると表示面との接続を外す。
    /// </summary>
    internal IMapSurface? Surface
    {
        get => _surface;
        set
        {
            var surface = value switch
            {
                null => null,
                MapsuiMapSurface mapsuiSurface => mapsuiSurface,
                _ => throw new ArgumentException("The surface is not a Mapsui surface.", nameof(value))
            };

            if (ReferenceEquals(_surface, surface))
            {
                return;
            }

            Unsubscribe(_surface);
            _surface = surface;
            if (surface is not null)
            {
                _mapControl.Map = surface.Map;
                Subscribe(surface);
            }

            MapStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private Action? OnRenderStarting(Viewport viewport, IReadOnlyList<ILayer> layers, RenderService renderService)
        => _surface?.BeginMapRender(viewport, layers, renderService);

    private void OnMapInfo(object? sender, MapInfoEventArgs e)
    {
        var marker = MapsuiMarkerHitTest.FindMarker(_surface?.Map, e);
        if (marker is not null)
        {
            MarkerClicked?.Invoke(this, marker);
        }
    }

    private void Subscribe(MapsuiMapSurface surface)
    {
        var map = surface.Map;
        map.DataChanged += OnDataChanged;
        map.Navigator.ViewportChanged += OnViewportChanged;
        map.Layers.Changed += OnLayersChanged;
    }

    private void Unsubscribe(MapsuiMapSurface? surface)
    {
        if (surface is null)
        {
            return;
        }

        var map = surface.Map;
        map.DataChanged -= OnDataChanged;
        map.Navigator.ViewportChanged -= OnViewportChanged;
        map.Layers.Changed -= OnLayersChanged;
    }

    private void OnDataChanged(object? sender, DataChangedEventArgs e)
        => MapStateChanged?.Invoke(this, EventArgs.Empty);

    private void OnViewportChanged(object? sender, ViewportChangedEventArgs e)
        => MapStateChanged?.Invoke(this, EventArgs.Empty);

    private void OnLayersChanged(object sender, LayerCollectionChangedEventArgs args)
        => MapStateChanged?.Invoke(this, EventArgs.Empty);
}
