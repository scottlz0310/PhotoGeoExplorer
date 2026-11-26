using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia;
using Mapsui.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PhotoGeoExplorer.Services.PanZoom;

namespace PhotoGeoExplorer.Panes.Map.MapsuiAdapter;

/// <summary>
/// Mapsui の <c>MapControl</c> を包む表示用のコントロール。<c>MapControl</c> の型・イベント・描画フックはここに閉じ込め、
/// View は <see cref="IMapSurface"/> とこのコントロールの公開部分だけを使う（#362 §11.6-3）。
/// </summary>
/// <remarks>
/// WinUI の要素を持つため単体テストしにくい。テストできる判断（表示面の型の確認・状態の購読・入力の反映）は
/// <see cref="MapsuiMapSurface"/>・<see cref="MapsuiMapStateSubscription"/>・<see cref="MapsuiMarkerHitTest"/> へ切り出してある。
/// 移動・拡大の入力は、画像プレビューと同じ <see cref="PanZoomInputAdapter"/> で受ける（#362 段階 1）。
/// </remarks>
[ExcludeFromCodeCoverage(Justification = "WinUI の MapControl を包む UI 配線で、実ウィンドウが必要なため。テストできる判断は MapsuiMapSurface.From・MapsuiMapStateSubscription・MapsuiMarkerHitTest へ切り出して、追跡している。")]
[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "MapControl は XAML の要素として、このコントロールの寿命と同じ。従来も View が破棄していなかった（地図は表示面が破棄する）。")]
internal sealed class MapsuiMapView : UserControl
{
    private readonly Mapsui.UI.WinUI.MapControl _mapControl = new();
    private MapsuiMapSurface? _surface;
    private MapsuiMapStateSubscription? _subscription;

    internal MapsuiMapView()
    {
        _mapControl.SetMapRenderer(new ObservingMapRenderer(new MapRenderer(), OnRenderStarting));

        // 自動操作（UI Automation）が地図を見つける識別子。内側のコントロールへ付ける
        AutomationProperties.SetAutomationId(_mapControl, "MapControl");
        _mapControl.Info += OnMapInfo;
        Content = _mapControl;

        // MapControl 自身の移動・拡大は、表示面がロックして止める。慣性は表示面が Mapsui の処理を呼ぶため、OS の慣性は使わない
        _mapControl.UseFling = false;
        _ = new PanZoomInputAdapter(_mapControl, new MapSurfaceInputTarget(() => _surface), useSystemInertia: false);
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
            var surface = MapsuiMapSurface.From(value);
            if (ReferenceEquals(_surface, surface))
            {
                return;
            }

            _subscription?.Dispose();
            _subscription = null;
            _surface = surface;
            if (surface is not null)
            {
                _mapControl.Map = surface.Map;
                _subscription = new MapsuiMapStateSubscription(surface, RaiseMapStateChanged);
            }

            RaiseMapStateChanged();
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

    private void RaiseMapStateChanged() => MapStateChanged?.Invoke(this, EventArgs.Empty);
}
