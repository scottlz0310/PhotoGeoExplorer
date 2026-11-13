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
    private const string ConfirmedMark = "\u2713";

    private readonly IAreaNameResolver? _areaNameResolver;
    private readonly IRoadNameSuggester? _roadNameSuggester;
    private readonly IRoadNameConfirmationStore? _confirmationStore;
    private readonly Resolution _areaNameResolution = new();
    private readonly Resolution _roadNameResolution = new();
    private (double Latitude, double Longitude)? _location;
    private PhotoIdentity? _photo;
    private RoadNameResult? _roadResult;
    private Task _confirmationQueue = Task.CompletedTask;
    private AreaName? _areaName;
    private string _areaNameText = string.Empty;
    private string? _areaNameToolTip;
    private string _roadNameText = string.Empty;
    private string? _roadNameToolTip;
    private IReadOnlyList<RoadCandidateItem> _roadCandidates = Array.Empty<RoadCandidateItem>();
    private string _coordinatesText = string.Empty;

    internal MapLocationInfoViewModel(
        IAreaNameResolver? areaNameResolver = null,
        IRoadNameSuggester? roadNameSuggester = null,
        IRoadNameConfirmationStore? roadNameConfirmationStore = null)
    {
        _areaNameResolver = areaNameResolver;
        _roadNameSuggester = roadNameSuggester;
        _confirmationStore = roadNameConfirmationStore;
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
    /// 最寄りの道路（第 1 候補。確定済みなら確定した道路）の詳細（「候補」「確定」の別・名称・距離・路線番号）。道路名を表示していないときは null
    /// </summary>
    public string? RoadNameToolTip
    {
        get => _roadNameToolTip;
        private set => SetProperty(ref _roadNameToolTip, value);
    }

    /// <summary>
    /// 候補の一覧（近い順）。確定済みで、今の候補にない道路は先頭に加える。道路名を表示していないときは空
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
    /// <param name="photo">写真の識別。道路名の確定の保存と読み出しに使う（null なら確定できない）</param>
    public Task SetLocationAsync(double latitude, double longitude, PhotoIdentity? photo = null)
    {
        // 同じ座標の別の写真（連写）でも、確定は写真ごとなので解決し直す
        if (_location == (latitude, longitude) && _photo == photo)
        {
            return Task.CompletedTask;
        }

        _location = (latitude, longitude);
        _photo = photo;
        UpdateCoordinatesText();
        return Task.WhenAll(ResolveAreaNameAsync(), ResolveRoadNameAsync());
    }

    /// <summary>
    /// 表示対象がない（未選択・複数選択・GPS なし）状態にする
    /// </summary>
    public void Clear()
    {
        _location = null;
        _photo = null;
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
        => ResolveAsync<RoadNameResult>(
            _roadNameResolution,
            DisplaySettings.ShowRoadName ? FindRoadNamesAsync : null,
            ApplyRoadNames,
            "road name");

    // 確定済みの値は端末内の保存から読む。候補は通信が要るので、取れなくても確定済みの値は見せる
    private async Task<RoadNameResult?> FindRoadNamesAsync(double latitude, double longitude, CancellationToken cancellationToken)
    {
        var confirmed = _photo is { } photo && _confirmationStore is { } store
            ? await store.FindAsync(photo, cancellationToken).ConfigureAwait(true)
            : null;

        IReadOnlyList<RoadNameCandidate> candidates = Array.Empty<RoadNameCandidate>();
        if (_roadNameSuggester is { } suggester)
        {
            try
            {
                candidates = await suggester.SuggestAsync(latitude, longitude, cancellationToken).ConfigureAwait(true);
            }
            catch (Exception ex) when (confirmed is not null && (ex is IOException or HttpRequestException))
            {
                AppLog.Error("Failed to suggest road names for the confirmed photo location.", ex);
            }
        }

        return new RoadNameResult(confirmed, candidates);
    }

    /// <summary>
    /// 候補の道路を、選択中の写真の道路名として確定する。保存できなかったときは、確定した表示にしない。
    /// 確定・解除は 1 つずつ順に行う（前の操作の保存が終わってから始める）
    /// </summary>
    public Task ConfirmRoadAsync(RoadCandidateItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return RunInOrderAsync(() => ConfirmCoreAsync(item));
    }

    /// <summary>
    /// 選択中の写真の道路名の確定を解除する。解除できなかったときは、確定した表示のままにする
    /// </summary>
    public Task UnconfirmRoadAsync() => RunInOrderAsync(UnconfirmCoreAsync);

    // 操作が重なると、保存した値と表示が食い違う（A・B を続けて確定したとき、B の保存が後でも、表示が A になる）
    private Task RunInOrderAsync(Func<Task> operation)
    {
        var task = RunAfterAsync(_confirmationQueue, operation);
        _confirmationQueue = task;
        return task;
    }

    // 前の操作の失敗は、その呼び出し元へ伝わる。ここでは、前の操作が終わるのだけを待つ
    private static async Task RunAfterAsync(Task previous, Func<Task> operation)
    {
        await Task.WhenAny(previous).ConfigureAwait(true);
        await operation().ConfigureAwait(true);
    }

    private async Task ConfirmCoreAsync(RoadCandidateItem item)
    {
        if (_photo is not { } photo || _confirmationStore is not { } store || _roadResult is null)
        {
            return;
        }

        try
        {
            await store.SaveAsync(photo, item.Road, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Failed to save the confirmed road name.", ex);
            return;
        }

        // 保存の間に別の写真へ移っていたら、その写真の表示は変えない
        if (_photo == photo && _roadResult is { } current)
        {
            ApplyRoadNames(current with { Confirmed = item.Road });
        }
    }

    private async Task UnconfirmCoreAsync()
    {
        if (_photo is not { } photo || _confirmationStore is not { } store || _roadResult is null)
        {
            return;
        }

        try
        {
            await store.RemoveAsync(photo.FilePath, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Failed to remove the confirmed road name.", ex);
            return;
        }

        if (_photo == photo && _roadResult is { } current)
        {
            ApplyRoadNames(current with { Confirmed = null });
        }
    }
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

    private void ApplyRoadNames(RoadNameResult? result)
    {
        _roadResult = result;
        if (result is null)
        {
            RoadNameText = string.Empty;
            RoadCandidates = Array.Empty<RoadCandidateItem>();
            RoadNameToolTip = null;
            return;
        }

        var canConfirm = _photo is not null && _confirmationStore is not null;
        var items = BuildRoadCandidateItems(result, canConfirm);
        RoadCandidates = items;
        if (items.FirstOrDefault(item => item.IsConfirmed) is { } confirmed)
        {
            RoadNameText = $"{ConfirmedMark} {confirmed.Name}";
            RoadNameToolTip = BuildRoadNameToolTip("RoadName.ToolTip.ConfirmedHeader", confirmed);
            return;
        }

        RoadNameText = LocationTextFormatter.FormatRoadName(result.Candidates, FormatRouteNumber);
        RoadNameToolTip = items.Count == 0 ? null : BuildRoadNameToolTip("RoadName.ToolTip.Header", items[0]);
    }

    // 確定済みの道路が今の候補にないとき（通信できない・位置が離れている）は、距離なしで先頭に加える
    private static List<RoadCandidateItem> BuildRoadCandidateItems(RoadNameResult result, bool canConfirm)
    {
        var items = result.Candidates
            .Select(candidate => ToRoadCandidateItem(
                candidate.Name,
                candidate.Ref,
                LocationTextFormatter.FormatDistance(candidate.DistanceMeters),
                isConfirmed: result.Confirmed is { } confirmed && confirmed.Matches(candidate),
                canConfirm))
            .ToList();
        if (result.Confirmed is { } missing && !items.Any(item => item.IsConfirmed))
        {
            items.Insert(0, ToRoadCandidateItem(missing.Name, missing.Ref, string.Empty, isConfirmed: true, canConfirm));
        }

        return items;
    }

    // 名称のない道路は、名称の欄に「路線番号 N」を出す。名称と路線番号が別にあるときだけ、路線番号を別の行にする
    private static RoadCandidateItem ToRoadCandidateItem(string? name, string? route, string distanceText, bool isConfirmed, bool canConfirm)
        => new(
            name ?? FormatRouteNumber(route ?? string.Empty),
            name is not null && route is not null ? FormatRouteNumber(route) : null,
            distanceText,
            new ConfirmedRoadName(name, route),
            isConfirmed,
            canConfirm);

    private static string BuildRoadNameToolTip(string headerKey, RoadCandidateItem item)
    {
        var lines = new List<string>
        {
            LocalizationService.GetString(headerKey),
            item.DistanceText.Length == 0 ? item.Name : $"{item.Name}  {item.DistanceText}"
        };
        if (item.RouteText is { } route)
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

    // 道路名の解決の結果（確定済みの値と、今の位置の候補）
    private sealed record RoadNameResult(ConfirmedRoadName? Confirmed, IReadOnlyList<RoadNameCandidate> Candidates);

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
