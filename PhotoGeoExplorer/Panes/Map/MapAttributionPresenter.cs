using System;
using System.Threading.Tasks;
using Mapsui;
using Mapsui.Styles;
using Mapsui.Widgets;
using Mapsui.Widgets.ButtonWidgets;
using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// 背景地図の帰属表示を地図ウィジェットとして描く。
/// ウィジェットは PNG 保存の描画経路にも乗るため、画面と保存画像で同じ表示になる。
/// </summary>
internal sealed class MapAttributionPresenter
{
    private readonly HyperlinkWidget _widget = new()
    {
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new MRect(4, 4, 4, 4),
        Padding = new MRect(4, 2, 4, 2),
        TextSize = 11,
        TextColor = new Color(32, 32, 32),
        BackColor = new Color(255, 255, 255, 200),
        CornerRadius = 2
    };

    // 大きさは Mapbox 公式の帰属表示コントロール（88×23）に合わせる。ロゴの加工は認められていない。
    private readonly ImageButtonWidget _mapboxLogo = new()
    {
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new MRect(6, 6, 6, 6),
        Padding = new MRect(0, 0, 0, 0),
        Width = 88,
        Height = 23,
        Image = new Image { Source = new Uri(MapAttribution.MapboxLogoPath).AbsoluteUri },
        Enabled = false
    };

    internal string Text => _widget.Text ?? string.Empty;

    internal bool IsMapboxLogoVisible => _mapboxLogo.Enabled;

    internal string Url => _widget.Url ?? string.Empty;

    internal void Attach(Mapsui.Map map, MapTileSourceType sourceType)
    {
        Update(sourceType);
        map.Widgets.Enqueue(_widget);
        map.Widgets.Enqueue(_mapboxLogo);
    }

    /// <summary>
    /// ロゴ画像を地図の画像キャッシュへ読み込む。ウィジェットの画像は自動では読み込まれないため、明示的に登録する。
    /// </summary>
    /// <returns>読み込めた場合 true</returns>
    internal Task<bool> LoadImagesAsync(Mapsui.Map map)
    {
        var image = _mapboxLogo.Image
            ?? throw new InvalidOperationException("The Mapbox logo widget has no image.");
        return map.RenderService.ImageSourceCache.TryRegisterAsync(image.SourceId, image.Source);
    }

    internal void Update(MapTileSourceType sourceType)
    {
        var (text, url) = MapAttribution.Get(sourceType);
        _widget.Text = text;
        _widget.Url = url;
        _mapboxLogo.Enabled = sourceType == MapTileSourceType.MapboxSatellite;
    }
}
