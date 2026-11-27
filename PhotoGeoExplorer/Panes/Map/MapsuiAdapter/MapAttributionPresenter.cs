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

namespace PhotoGeoExplorer.Panes.Map.MapsuiAdapter;

/// <summary>
/// 背景地図の帰属表示を地図ウィジェットとして描く。
/// ウィジェットは PNG 保存の描画経路にも乗るため、画面と保存画像で同じ表示になる。
/// 帰属項目ごとに別のリンクとし、右下から左へ並べる。
/// </summary>
internal sealed class MapAttributionPresenter
{
    private readonly HyperlinkWidget[] _linkWidgets = Enumerable
        .Range(0, MapAttribution.MaxLinkCount)
        .Select(_ => CreateLinkWidget())
        .ToArray();

    private readonly ImageButtonWidget _mapboxLogo = new()
    {
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new MRect(
            MapAttributionLayout.LogoMargin,
            MapAttributionLayout.LogoMargin,
            MapAttributionLayout.LogoMargin,
            MapAttributionLayout.LogoMargin),
        Padding = new MRect(0, 0, 0, 0),
        Width = MapAttributionLayout.LogoWidth,
        Height = MapAttributionLayout.LogoHeight,
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
        var descriptor = MapBackgroundCatalog.Get(sourceType);
        _links = descriptor.AttributionLinks;

        for (var index = _links.Count; index < _linkWidgets.Length; index++)
        {
            _linkWidgets[index].Enabled = false;
        }

        // 下の行から、各行は右の項目から並べる
        var bottomOffset = MapAttributionLayout.OuterMargin;
        var linkIndex = _links.Count - 1;
        foreach (var rowLength in descriptor.AttributionRows.Reverse())
        {
            var rightOffset = MapAttributionLayout.OuterMargin;
            for (var item = 0; item < rowLength; item++, linkIndex--)
            {
                var widget = _linkWidgets[linkIndex];
                var link = _links[linkIndex];
                var width = MeasureWidth(link.Text);
                widget.Text = link.Text;
                widget.Url = link.Url;
                widget.Width = width;
                widget.Margin = new MRect(MapAttributionLayout.OuterMargin, bottomOffset, rightOffset, bottomOffset);
                widget.Enabled = true;
                rightOffset += width;
            }

            bottomOffset += MapAttributionLayout.StackedLineHeight;
        }

        _mapboxLogo.Enabled = descriptor.ShowsMapboxLogo;
    }

    /// <summary>
    /// 帰属表示の文言とロゴが、切れたり重なったりせずに収まる地図の大きさ（DIP）
    /// </summary>
    internal static (double Width, double Height) GetRequiredSize(MapBackgroundDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var logoWidth = descriptor.ShowsMapboxLogo
            ? MapAttributionLayout.LogoWidth + (2 * MapAttributionLayout.LogoMargin)
            : 0;
        var width = logoWidth;
        var bottomOffset = MapAttributionLayout.OuterMargin;
        var linkIndex = descriptor.AttributionLinks.Count - 1;
        foreach (var rowLength in descriptor.AttributionRows.Reverse())
        {
            var rowWidth = 2 * MapAttributionLayout.OuterMargin;
            for (var item = 0; item < rowLength; item++, linkIndex--)
            {
                rowWidth += MeasureWidth(descriptor.AttributionLinks[linkIndex].Text);
            }

            // 左下のロゴと同じ高さにある行は、ロゴの右側に収める必要がある
            var besideLogo = bottomOffset < MapAttributionLayout.LogoMargin + MapAttributionLayout.LogoHeight;
            width = Math.Max(width, besideLogo ? rowWidth + logoWidth : rowWidth);
            bottomOffset += MapAttributionLayout.StackedLineHeight;
        }

        return (width, MapAttributionLayout.GetReservedHeight(descriptor));
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
            Padding = new MRect(
                MapAttributionLayout.HorizontalPadding,
                MapAttributionLayout.VerticalPadding,
                MapAttributionLayout.HorizontalPadding,
                MapAttributionLayout.VerticalPadding),
            TextSize = MapAttributionLayout.TextSize,
            TextColor = new Color(32, 32, 32),
            BackColor = new Color(255, 255, 255, 200),
            CornerRadius = 0,
            Enabled = false
        };
    }

    private static double MeasureWidth(string text)
    {
        using var font = new SKFont(SKTypeface.Default, (float)MapAttributionLayout.TextSize);
        return Math.Ceiling(font.MeasureText(text)) + (MapAttributionLayout.HorizontalPadding * 2);
    }
}
