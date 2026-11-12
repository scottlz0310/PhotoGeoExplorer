using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mapsui;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Widgets;
using Mapsui.Widgets.ButtonWidgets;
using PhotoGeoExplorer.Models;
using SkiaSharp;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// 背景地図の帰属表示を地図ウィジェットとして描く。
/// ウィジェットは PNG 保存の描画経路にも乗るため、画面と保存画像で同じ表示になる。
/// 帰属項目ごとに別のリンクとし、右下から左へ並べる。
/// </summary>
internal sealed class MapAttributionPresenter
{
    private const double TextSize = 11;
    private const double OuterMargin = 4;
    private const double HorizontalPadding = 4;
    private const double VerticalPadding = 2;

    private readonly HyperlinkWidget[] _linkWidgets = Enumerable
        .Range(0, MapAttribution.MaxLinkCount)
        .Select(_ => CreateLinkWidget())
        .ToArray();

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

    private IReadOnlyList<MapAttributionLink> _links = [];

    /// <summary>
    /// 表示中の帰属項目（左から右の順）
    /// </summary>
    internal IReadOnlyList<(string Text, string Url)> Links => _linkWidgets
        .Where(widget => widget.Enabled)
        .Select(widget => (widget.Text ?? string.Empty, widget.Url ?? string.Empty))
        .ToArray();

    internal string Text => string.Join(' ', Links.Select(link => link.Text));

    internal bool IsMapboxLogoVisible => _mapboxLogo.Enabled;

    internal void Attach(Mapsui.Map map, MapTileSourceType sourceType)
    {
        Update(sourceType);
        foreach (var widget in _linkWidgets)
        {
            map.Widgets.Enqueue(widget);
        }

        map.Widgets.Enqueue(_mapboxLogo);

        UpdateViewport(map.Navigator.Viewport);
        map.Navigator.ViewportChanged += (_, _) => UpdateViewport(map.Navigator.Viewport);
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
        _links = MapAttribution.GetLinks(sourceType);

        // 右端の項目から順に置き、それぞれの幅だけ左へずらす。
        var rightOffset = OuterMargin;
        for (var index = _linkWidgets.Length - 1; index >= 0; index--)
        {
            var widget = _linkWidgets[index];
            if (index >= _links.Count)
            {
                widget.Enabled = false;
                continue;
            }

            var link = _links[index];
            var width = MeasureWidth(link.Text);
            widget.Text = link.Text;
            widget.Url = link.Url;
            widget.Width = width;
            widget.Margin = new MRect(OuterMargin, OuterMargin, rightOffset, OuterMargin);
            widget.Enabled = true;
            rightOffset += width;
        }

        _mapboxLogo.Enabled = sourceType == MapTileSourceType.MapboxSatellite;
    }

    /// <summary>
    /// 表示中の位置と倍率を、地図の修正依頼リンクへ反映する
    /// </summary>
    internal void UpdateViewport(Viewport viewport)
    {
        for (var index = 0; index < _links.Count; index++)
        {
            if (!_links[index].TracksViewport)
            {
                continue;
            }

            var (longitude, latitude) = SphericalMercator.ToLonLat(viewport.CenterX, viewport.CenterY);
            _linkWidgets[index].Url = MapAttribution.BuildMapboxFeedbackUrl(longitude, latitude, viewport.Resolution);
        }
    }

    private static HyperlinkWidget CreateLinkWidget()
    {
        return new HyperlinkWidget
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Padding = new MRect(HorizontalPadding, VerticalPadding, HorizontalPadding, VerticalPadding),
            TextSize = TextSize,
            TextColor = new Color(32, 32, 32),
            BackColor = new Color(255, 255, 255, 200),
            CornerRadius = 0,
            Enabled = false
        };
    }

    private static double MeasureWidth(string text)
    {
        using var font = new SKFont(SKTypeface.Default, (float)TextSize);
        return Math.Ceiling(font.MeasureText(text)) + (HorizontalPadding * 2);
    }
}
