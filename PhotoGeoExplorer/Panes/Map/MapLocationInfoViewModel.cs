using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.State;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// 地図ヘッダーに表示する撮影位置情報（字地名・最寄り道路名・緯度経度）の ViewModel。
/// 3 項目は独立に解決し、解決できたものだけを公開する。表示幅に応じた省略は View の責務
/// </summary>
internal sealed class MapLocationInfoViewModel : BindableBase
{
    private readonly IAreaNameResolver? _areaNameResolver;
    private readonly IRoadNameSuggester? _roadNameSuggester;
    private readonly Resolution _areaNameResolution = new();
    private readonly Resolution _roadNameResolution = new();
    private (double Latitude, double Longitude)? _location;
    private AreaName? _areaName;
    private string _areaNameText = string.Empty;
    private string? _areaNameToolTip;
    private string _roadNameText = string.Empty;
    private string? _roadNameToolTip;
    private IReadOnlyList<RoadCandidateItem> _roadCandidates = Array.Empty<RoadCandidateItem>();
    private string _coordinatesText = string.Empty;

    internal MapLocationInfoViewModel(
        IAreaNameResolver? areaNameResolver = null,
        IRoadNameSuggester? roadNameSuggester = null)
    {
        _areaNameResolver = areaNameResolver;
        _roadNameSuggester = roadNameSuggester;
        DisplaySettings.PropertyChanged += OnDisplaySettingsChanged;
        if (_areaNameResolver is not null)
        {
            _areaNameResolver.ResultsInvalidated += OnAreaDataChanged;
        }
    }

    public MapHeaderDisplaySettings DisplaySettings { get; } = new();

    public string AreaNameText
    {
        get => _areaNameText;
        private set
        {
            if (SetProperty(ref _areaNameText, value))
            {
                OnPropertyChanged(nameof(AreaNameVisibility));
            }
        }
    }

    /// <summary>
    /// 字地名の詳細（完全な表記・小地域コード・補正の有無）。字地名を表示していないときは null
    /// </summary>
    public string? AreaNameToolTip
    {
        get => _areaNameToolTip;
        private set => SetProperty(ref _areaNameToolTip, value);
    }

    public string RoadNameText
    {
        get => _roadNameText;
        private set
        {
            if (SetProperty(ref _roadNameText, value))
            {
                OnPropertyChanged(nameof(RoadNameVisibility));
            }
        }
    }

    /// <summary>
    /// 最寄りの道路（第 1 候補）の詳細（「候補」であること・名称・距離・路線番号）。道路名を表示していないときは null
    /// </summary>
    public string? RoadNameToolTip
    {
        get => _roadNameToolTip;
        private set => SetProperty(ref _roadNameToolTip, value);
    }

    /// <summary>
    /// 候補の一覧（近い順）。道路名を表示していないときは空
    /// </summary>
    public IReadOnlyList<RoadCandidateItem> RoadCandidates
    {
        get => _roadCandidates;
        private set => SetProperty(ref _roadCandidates, value);
    }

    public string CoordinatesText
    {
        get => _coordinatesText;
        private set
        {
            if (SetProperty(ref _coordinatesText, value))
            {
                OnPropertyChanged(nameof(CoordinatesVisibility));
            }
        }
    }

    public Visibility AreaNameVisibility => ToVisibility(_areaNameText);

    public Visibility RoadNameVisibility => ToVisibility(_roadNameText);

    public Visibility CoordinatesVisibility => ToVisibility(_coordinatesText);

    /// <summary>
    /// 単一選択された写真の撮影位置を設定し、各項目を解決する。直前の解決はキャンセルする
    /// </summary>
    public Task SetLocationAsync(double latitude, double longitude)
    {
        if (_location == (latitude, longitude))
        {
            return Task.CompletedTask;
        }

        _location = (latitude, longitude);
        UpdateCoordinatesText();
        return Task.WhenAll(ResolveAreaNameAsync(), ResolveRoadNameAsync());
    }

    /// <summary>
    /// 表示対象がない（未選択・複数選択・GPS なし）状態にする
    /// </summary>
    public void Clear()
    {
        _location = null;
        UpdateCoordinatesText();
        _areaNameResolution.Restart();
        ApplyAreaName(null);
        _roadNameResolution.Restart();
        ApplyRoadNames(null);
    }

    private static Visibility ToVisibility(string text)
        => text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

    private void OnDisplaySettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MapHeaderDisplaySettings.ShowCoordinates):
                UpdateCoordinatesText();
                break;
            case nameof(MapHeaderDisplaySettings.IncludePrefecture):
                ApplyAreaName(_areaName);
                break;
            case nameof(MapHeaderDisplaySettings.ShowAddress):
                _ = ResolveAreaNameAsync();
                break;
            case nameof(MapHeaderDisplaySettings.ShowRoadName):
                _ = ResolveRoadNameAsync();
                break;
        }
    }

    // 取り込み・削除の直後に、表示中の写真の字地名を新しいデータで求め直す
    private void OnAreaDataChanged(object? sender, EventArgs e) => _ = ResolveAreaNameAsync();

    private void UpdateCoordinatesText()
    {
        CoordinatesText = DisplaySettings.ShowCoordinates && _location is { } location
            ? LocationTextFormatter.FormatCoordinates(location.Latitude, location.Longitude)
            : string.Empty;
    }

    private Task ResolveAreaNameAsync()
        => ResolveAsync<AreaName>(
            _areaNameResolution,
            DisplaySettings.ShowAddress && _areaNameResolver is { } resolver ? resolver.ResolveAsync : null,
            ApplyAreaName,
            "area name");

    private Task ResolveRoadNameAsync()
        => ResolveAsync<IReadOnlyList<RoadNameCandidate>>(
            _roadNameResolution,
            DisplaySettings.ShowRoadName && _roadNameSuggester is { } suggester
                ? async (latitude, longitude, cancellationToken) =>
                    await suggester.SuggestAsync(latitude, longitude, cancellationToken).ConfigureAwait(true)
                : null,
            ApplyRoadNames,
            "road name");

    private void ApplyAreaName(AreaName? areaName)
    {
        _areaName = areaName;

        // 水面調査区には住所としての字地名がないので、表示しない
        var displayed = areaName is { IsWater: false } ? areaName : null;
        AreaNameText = displayed is null
            ? string.Empty
            : LocationTextFormatter.FormatAreaName(displayed, DisplaySettings.IncludePrefecture);
        AreaNameToolTip = displayed is null ? null : BuildAreaNameToolTip(displayed);
    }

    private static string BuildAreaNameToolTip(AreaName areaName)
    {
        var lines = new List<string>
        {
            LocationTextFormatter.FormatAreaName(areaName, includePrefecture: true),
            LocalizationService.Format("AreaName.ToolTip.Source", areaName.KeyCode, areaName.CensusYear)
        };
        if (areaName.SnappedToNearest)
        {
            lines.Add(LocalizationService.GetString("AreaName.ToolTip.Snapped"));
        }

        return string.Join('\n', lines);
    }

    private void ApplyRoadNames(IReadOnlyList<RoadNameCandidate>? candidates)
    {
        RoadNameText = candidates is null
            ? string.Empty
            : LocationTextFormatter.FormatRoadName(candidates, FormatRouteNumber);
        var items = candidates is null
            ? Array.Empty<RoadCandidateItem>()
            : candidates.Select(ToRoadCandidateItem).ToArray();
        RoadCandidates = items;
        RoadNameToolTip = items.Length == 0 ? null : BuildRoadNameToolTip(items[0]);
    }

    // 名称のない道路は、名称の欄に「路線番号 N」を出す。名称と路線番号が別にあるときだけ、路線番号を別の行にする
    private static RoadCandidateItem ToRoadCandidateItem(RoadNameCandidate candidate)
        => new(
            LocationTextFormatter.FormatRoadCandidate(candidate, FormatRouteNumber),
            candidate.Name is not null && candidate.Ref is { } route ? FormatRouteNumber(route) : null,
            LocationTextFormatter.FormatDistance(candidate.DistanceMeters));

    private static string BuildRoadNameToolTip(RoadCandidateItem first)
    {
        var lines = new List<string>
        {
            LocalizationService.GetString("RoadName.ToolTip.Header"),
            $"{first.Name}  {first.DistanceText}"
        };
        if (first.RouteText is { } route)
        {
            lines.Add(route);
        }

        return string.Join('\n', lines);
    }

    private static string FormatRouteNumber(string route)
        => LocalizationService.Format("RoadName.RouteNumber", route);

    // resolve が null のとき（設定でオフ・解決器なし）は解決処理を走らせず、表示だけ消す。
    private async Task ResolveAsync<T>(
        Resolution resolution,
        Func<double, double, CancellationToken, Task<T?>>? resolve,
        Action<T?> apply,
        string description)
        where T : class
    {
        var generation = resolution.Restart();

        // 前の地点の結果を、別の地点の値として見せない
        apply(null);
        if (resolve is null || _location is not { } location)
        {
            return;
        }

        var cancellationToken = resolution.Begin();
        T? result;
        try
        {
            // PropertyChanged を UI スレッドで発火させるため、呼び出し元のコンテキストへ戻る
            result = await resolve(location.Latitude, location.Longitude, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or UnauthorizedAccessException)
        {
            AppLog.Error($"Failed to resolve the {description} of the photo location.", ex);
            return;
        }

        if (resolution.IsCurrent(generation))
        {
            apply(result);
        }
    }

    // CancellationTokenSource は Restart() で破棄する。MapPaneViewModel の Cleanup() から Clear() 経由で呼ばれる
    private sealed class Resolution
    {
        private CancellationTokenSource? _cts;
        private int _generation;

        public bool IsCurrent(int generation) => generation == _generation;

        public int Restart()
        {
            var previous = _cts;
            _cts = null;
            previous?.Cancel();
            previous?.Dispose();
            return ++_generation;
        }

        public CancellationToken Begin()
        {
            var cts = new CancellationTokenSource();
            _cts = cts;
            return cts.Token;
        }
    }
}
