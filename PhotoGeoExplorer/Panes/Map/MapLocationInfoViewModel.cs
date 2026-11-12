using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    private string _roadNameText = string.Empty;
    private string _coordinatesText = string.Empty;

    internal MapLocationInfoViewModel(
        IAreaNameResolver? areaNameResolver = null,
        IRoadNameSuggester? roadNameSuggester = null)
    {
        _areaNameResolver = areaNameResolver;
        _roadNameSuggester = roadNameSuggester;
        DisplaySettings.PropertyChanged += OnDisplaySettingsChanged;
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
        AreaNameText = areaName is null
            ? string.Empty
            : LocationTextFormatter.FormatAreaName(areaName, DisplaySettings.IncludePrefecture);
    }

    private void ApplyRoadNames(IReadOnlyList<RoadNameCandidate>? candidates)
    {
        RoadNameText = candidates is null
            ? string.Empty
            : LocationTextFormatter.FormatRoadName(candidates);
    }

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
