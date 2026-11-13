using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Panes.Places;

/// <summary>
/// 字地名のツリーの 1 ノードの表示用 ViewModel
/// </summary>
internal sealed class PlacesNodeViewModel : BindableBase
{
    private readonly Action<string, bool> _expansionChanged;
    private bool _isExpanded;

    public PlacesNodeViewModel(
        AreaTreeNode node,
        Func<string, bool> isExpanded,
        Action<string, bool> expansionChanged)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(isExpanded);

        _expansionChanged = expansionChanged ?? throw new ArgumentNullException(nameof(expansionChanged));
        Kind = node.Kind;
        ExpansionKey = GetExpansionKey(node);
        FilePaths = node.FilePaths;
        DisplayText = string.Create(CultureInfo.CurrentCulture, $"{GetLabel(node)} ({node.Count})");
        _isExpanded = isExpanded(ExpansionKey);
        Children = new ObservableCollection<PlacesNodeViewModel>(
            node.Children.Select(child => new PlacesNodeViewModel(child, isExpanded, expansionChanged)));
    }

    public AreaTreeNodeKind Kind { get; }

    /// <summary>ツリーを作り直しても、開いていたノードを開いたままにするためのキー</summary>
    public string ExpansionKey { get; }

    /// <summary>このノードとその子孫の写真のパス</summary>
    public IReadOnlyList<string> FilePaths { get; }

    /// <summary>「鳥取市 (42)」のような、名称と写真の枚数</summary>
    public string DisplayText { get; }

    public ObservableCollection<PlacesNodeViewModel> Children { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                _expansionChanged(ExpansionKey, value);
            }
        }
    }

    private static string GetExpansionKey(AreaTreeNode node)
        => string.Create(CultureInfo.InvariantCulture, $"{node.Kind}:{node.Key}");

    private static string GetLabel(AreaTreeNode node)
        => node.Kind switch
        {
            AreaTreeNodeKind.Water => LocalizationService.GetString("Places.Node.Water"),
            AreaTreeNodeKind.NoAreaData => LocalizationService.GetString("Places.Node.NoAreaData"),
            AreaTreeNodeKind.NoLocation => LocalizationService.GetString("Places.Node.NoLocation"),
            _ => string.IsNullOrWhiteSpace(node.Label) ? LocalizationService.GetString("Places.Node.Unnamed") : node.Label
        };
}
