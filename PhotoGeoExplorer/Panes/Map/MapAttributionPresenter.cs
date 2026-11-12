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

    internal string Text => _widget.Text ?? string.Empty;

    internal string Url => _widget.Url ?? string.Empty;

    internal void Attach(Mapsui.Map map, MapTileSourceType sourceType)
    {
        Update(sourceType);
        map.Widgets.Enqueue(_widget);
    }

    internal void Update(MapTileSourceType sourceType)
    {
        var (text, url) = MapAttribution.Get(sourceType);
        _widget.Text = text;
        _widget.Url = url;
    }
}
