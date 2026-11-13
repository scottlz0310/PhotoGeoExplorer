using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Linq;
using Microsoft.UI.Xaml;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Map;
using PhotoGeoExplorer.Services;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public class MapLocationInfoViewModelTests
{
    private static readonly AreaName Miyamachi = new("01460001000", "北海道", "上富良野町", "宮町", SnappedToNearest: false, CensusYear: 2020);
    private static readonly AreaName Marunouchi = new("13101001000", "東京都", "千代田区", "丸の内", SnappedToNearest: false, CensusYear: 2020);

    [Fact]
    public void InitialStateShowsNothing()
    {
        var viewModel = new MapLocationInfoViewModel();

        Assert.Equal(string.Empty, viewModel.AreaNameText);
        Assert.Equal(string.Empty, viewModel.RoadNameText);
        Assert.Equal(string.Empty, viewModel.CoordinatesText);
        Assert.Equal(Visibility.Collapsed, viewModel.AreaNameVisibility);
        Assert.Equal(Visibility.Collapsed, viewModel.RoadNameVisibility);
        Assert.Equal(Visibility.Collapsed, viewModel.CoordinatesVisibility);
    }

    [Fact]
    public async Task SetLocationAsyncShowsCoordinatesWithoutResolvers()
    {
        var viewModel = new MapLocationInfoViewModel();

        await viewModel.SetLocationAsync(43.468212, 142.482015).ConfigureAwait(true);

        Assert.Equal("43.468212, 142.482015", viewModel.CoordinatesText);
        Assert.Equal(Visibility.Visible, viewModel.CoordinatesVisibility);
        Assert.Equal(Visibility.Collapsed, viewModel.AreaNameVisibility);
        Assert.Equal(Visibility.Collapsed, viewModel.RoadNameVisibility);
    }

    [Fact]
    public async Task SetLocationAsyncShowsResolvedItems()
    {
        var areaResolver = new FakeAreaNameResolver();
        var roadSuggester = new FakeRoadNameSuggester();
        var viewModel = new MapLocationInfoViewModel(areaResolver, roadSuggester);

        var update = viewModel.SetLocationAsync(43.468212, 142.482015);

        // 緯度経度は解決を待たずに出る
        Assert.Equal("43.468212, 142.482015", viewModel.CoordinatesText);
        Assert.Equal(string.Empty, viewModel.AreaNameText);

        areaResolver.Calls[0].Complete(Miyamachi);
        roadSuggester.Calls[0].Complete(new[]
        {
            new RoadNameCandidate("富良野平原広域農道", Ref: null, DistanceMeters: 4),
            new RoadNameCandidate("国道 237 号", Ref: "237", DistanceMeters: 31)
        });
        await update.ConfigureAwait(true);

        Assert.Equal("上富良野町 宮町", viewModel.AreaNameText);
        Assert.Equal(Visibility.Visible, viewModel.AreaNameVisibility);
        Assert.Equal("富良野平原広域農道 (+1)", viewModel.RoadNameText);
        Assert.Equal(Visibility.Visible, viewModel.RoadNameVisibility);
        Assert.Equal((43.468212, 142.482015), (areaResolver.Calls[0].Latitude, areaResolver.Calls[0].Longitude));
        Assert.Equal((43.468212, 142.482015), (roadSuggester.Calls[0].Latitude, roadSuggester.Calls[0].Longitude));
    }

    [Fact]
    public async Task SetLocationAsyncShowsNothingForAWaterSurfaceArea()
    {
        var areaResolver = new FakeAreaNameResolver();
        var viewModel = new MapLocationInfoViewModel(areaResolver);

        var update = viewModel.SetLocationAsync(35.65, 139.8);
        areaResolver.Calls[0].Complete(Miyamachi with { IsWater = true });
        await update.ConfigureAwait(true);

        Assert.Equal(string.Empty, viewModel.AreaNameText);
        Assert.Equal(Visibility.Collapsed, viewModel.AreaNameVisibility);
        Assert.Null(viewModel.AreaNameToolTip);
    }

    [Fact]
    public async Task AreaNameToolTipShowsTheFullNotationAndTheSourceRegardlessOfThePrefectureSetting()
    {
        var areaResolver = new FakeAreaNameResolver();
        var viewModel = new MapLocationInfoViewModel(areaResolver);
        Assert.False(viewModel.DisplaySettings.IncludePrefecture);

        var update = viewModel.SetLocationAsync(43.468212, 142.482015);
        areaResolver.Calls[0].Complete(Miyamachi);
        await update.ConfigureAwait(true);

        // テスト環境ではローカライズの文言が引けず、キーがそのまま返る
        Assert.Equal("上富良野町 宮町", viewModel.AreaNameText);
        Assert.Equal("北海道 上富良野町 宮町\nAreaName.ToolTip.Source", viewModel.AreaNameToolTip);
    }

    [Fact]
    public async Task AreaNameToolTipExplainsWhenTheLocationWasSnappedToTheNearestArea()
    {
        var areaResolver = new FakeAreaNameResolver();
        var viewModel = new MapLocationInfoViewModel(areaResolver);

        var update = viewModel.SetLocationAsync(43.468212, 142.482015);
        areaResolver.Calls[0].Complete(Miyamachi with { SnappedToNearest = true });
        await update.ConfigureAwait(true);

        Assert.EndsWith("\nAreaName.ToolTip.Snapped", viewModel.AreaNameToolTip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolvingAgainAfterTheAreaDataChangesReplacesTheAreaName()
    {
        var areaResolver = new FakeAreaNameResolver();
        var viewModel = new MapLocationInfoViewModel(areaResolver);
        var first = viewModel.SetLocationAsync(43.468212, 142.482015);
        areaResolver.Calls[0].Complete(null);
        await first.ConfigureAwait(true);
        Assert.Equal(string.Empty, viewModel.AreaNameText);

        areaResolver.RaiseResultsInvalidated();

        Assert.Equal(2, areaResolver.Calls.Count);
        Assert.Equal((43.468212, 142.482015), (areaResolver.Calls[1].Latitude, areaResolver.Calls[1].Longitude));
        areaResolver.Calls[1].Complete(Miyamachi);
        await Task.Yield();
        Assert.Equal("上富良野町 宮町", viewModel.AreaNameText);
    }

    [Fact]
    public void AreaDataChangesDoNotResolveWhenNothingIsSelected()
    {
        var areaResolver = new FakeAreaNameResolver();
        _ = new MapLocationInfoViewModel(areaResolver);

        areaResolver.RaiseResultsInvalidated();

        Assert.Empty(areaResolver.Calls);
    }

    [Fact]
    public async Task RoadCandidatesListEachCandidateWithItsDistanceAndRouteNumber()
    {
        var roadSuggester = new FakeRoadNameSuggester();
        var viewModel = new MapLocationInfoViewModel(roadNameSuggester: roadSuggester);

        var update = viewModel.SetLocationAsync(35.5036, 134.2381);
        roadSuggester.Calls[0].Complete(new[]
        {
            new RoadNameCandidate("山の手通り", Ref: "203", DistanceMeters: 12.4),
            new RoadNameCandidate(Name: null, Ref: "1167", DistanceMeters: 30.6),
            new RoadNameCandidate("丸山通", Ref: null, DistanceMeters: 49.5)
        });
        await update.ConfigureAwait(true);

        // 名称のない道路は、名称の欄に路線番号を出す（別の行にはしない）
        Assert.Equal(
            new[]
            {
                new RoadCandidateItem("山の手通り", "RoadName.RouteNumber", "12 m"),
                new RoadCandidateItem("RoadName.RouteNumber", null, "31 m"),
                new RoadCandidateItem("丸山通", null, "50 m")
            },
            viewModel.RoadCandidates);
    }

    [Theory]
    [InlineData(true, Visibility.Visible)]
    [InlineData(false, Visibility.Collapsed)]
    public void RoadCandidateItemShowsTheRouteLineOnlyWhenThereIsOne(bool hasRoute, Visibility expected)
    {
        var item = new RoadCandidateItem("山の手通り", hasRoute ? "RoadName.RouteNumber" : null, "12 m");

        Assert.Equal(expected, item.RouteVisibility);
    }

    [Fact]
    public async Task RoadNameToolTipDescribesTheFirstCandidateAsACandidate()
    {
        var roadSuggester = new FakeRoadNameSuggester();
        var viewModel = new MapLocationInfoViewModel(roadNameSuggester: roadSuggester);

        var update = viewModel.SetLocationAsync(35.5036, 134.2381);
        roadSuggester.Calls[0].Complete(new[]
        {
            new RoadNameCandidate("山の手通り", Ref: "203", DistanceMeters: 12.4),
            new RoadNameCandidate("丸山通", Ref: null, DistanceMeters: 20)
        });
        await update.ConfigureAwait(true);

        Assert.Equal("RoadName.ToolTip.Header\n山の手通り  12 m\nRoadName.RouteNumber", viewModel.RoadNameToolTip);
    }

    [Fact]
    public async Task RoadNameToolTipHasNoRouteLineForARoadWithoutAnotherRouteNumber()
    {
        var roadSuggester = new FakeRoadNameSuggester();
        var viewModel = new MapLocationInfoViewModel(roadNameSuggester: roadSuggester);

        var update = viewModel.SetLocationAsync(43.588, 142.466);
        roadSuggester.Calls[0].Complete(new[] { new RoadNameCandidate("丸山通", Ref: null, DistanceMeters: 9) });
        await update.ConfigureAwait(true);

        Assert.Equal("RoadName.ToolTip.Header\n丸山通  9 m", viewModel.RoadNameToolTip);
    }

    [Fact]
    public async Task RoadCandidatesAndTheToolTipAreClearedWithTheSelection()
    {
        var roadSuggester = new FakeRoadNameSuggester();
        var viewModel = new MapLocationInfoViewModel(roadNameSuggester: roadSuggester);
        var update = viewModel.SetLocationAsync(35.5036, 134.2381);
        roadSuggester.Calls[0].Complete(new[] { new RoadNameCandidate("山の手通り", Ref: null, DistanceMeters: 5) });
        await update.ConfigureAwait(true);

        viewModel.Clear();

        Assert.Empty(viewModel.RoadCandidates);
        Assert.Null(viewModel.RoadNameToolTip);
    }

    [Fact]
    public async Task NoRoadCandidateMeansNoToolTip()
    {
        var roadSuggester = new FakeRoadNameSuggester();
        var viewModel = new MapLocationInfoViewModel(roadNameSuggester: roadSuggester);

        var update = viewModel.SetLocationAsync(51.5, -0.12);
        roadSuggester.Calls[0].Complete(Array.Empty<RoadNameCandidate>());
        await update.ConfigureAwait(true);

        Assert.Empty(viewModel.RoadCandidates);
        Assert.Null(viewModel.RoadNameToolTip);
    }

    [Theory]
    [InlineData("ja-JP")]
    [InlineData("en-US")]
    public void RoadNameTextsExistInBothLanguages(string language)
    {
        var path = Path.Combine(RepositoryPaths.FindRoot(), "PhotoGeoExplorer", "Strings", language, "Resources.resw");
        var keys = XDocument.Load(path).Root!.Elements("data").Select(element => (string)element.Attribute("name")!).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("RoadName.RouteNumber", keys);
        Assert.Contains("RoadName.ToolTip.Header", keys);
        Assert.Contains("RoadCandidatesFlyoutTitle.Text", keys);
    }

    [Theory]
    [InlineData("ja-JP")]
    [InlineData("en-US")]
    public void AreaNameToolTipTextsExistInBothLanguages(string language)
    {
        var path = Path.Combine(RepositoryPaths.FindRoot(), "PhotoGeoExplorer", "Strings", language, "Resources.resw");
        var keys = XDocument.Load(path).Root!.Elements("data").Select(element => (string)element.Attribute("name")!).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("AreaName.ToolTip.Source", keys);
        Assert.Contains("AreaName.ToolTip.Snapped", keys);
    }

    [Fact]
    public async Task SetLocationAsyncShowsNothingForUnresolvedItems()
    {
        var areaResolver = new FakeAreaNameResolver();
        var roadSuggester = new FakeRoadNameSuggester();
        var viewModel = new MapLocationInfoViewModel(areaResolver, roadSuggester);

        var update = viewModel.SetLocationAsync(51.5, -0.12);
        areaResolver.Calls[0].Complete(null);
        roadSuggester.Calls[0].Complete(Array.Empty<RoadNameCandidate>());
        await update.ConfigureAwait(true);

        Assert.Equal(Visibility.Collapsed, viewModel.AreaNameVisibility);
        Assert.Equal(Visibility.Collapsed, viewModel.RoadNameVisibility);
        Assert.Equal(Visibility.Visible, viewModel.CoordinatesVisibility);
    }

    [Fact]
    public async Task SetLocationAsyncCancelsThePreviousResolutionAndIgnoresItsResult()
    {
        var areaResolver = new FakeAreaNameResolver { IgnoreCancellation = true };
        var roadSuggester = new FakeRoadNameSuggester { IgnoreCancellation = true };
        var viewModel = new MapLocationInfoViewModel(areaResolver, roadSuggester);

        var first = viewModel.SetLocationAsync(43.468212, 142.482015);
        var second = viewModel.SetLocationAsync(35.681236, 139.767125);

        Assert.True(areaResolver.Calls[0].CancellationToken.IsCancellationRequested);
        Assert.True(roadSuggester.Calls[0].CancellationToken.IsCancellationRequested);
        Assert.False(areaResolver.Calls[1].CancellationToken.IsCancellationRequested);

        // キャンセルを無視して結果を返す解決器でも、古い地点の結果は表示しない
        areaResolver.Calls[0].Complete(Miyamachi);
        roadSuggester.Calls[0].Complete(new[] { new RoadNameCandidate("富良野平原広域農道", Ref: null, DistanceMeters: 4) });
        await first.ConfigureAwait(true);
        Assert.Equal(string.Empty, viewModel.AreaNameText);
        Assert.Equal(string.Empty, viewModel.RoadNameText);

        areaResolver.Calls[1].Complete(Marunouchi);
        roadSuggester.Calls[1].Complete(new[] { new RoadNameCandidate("行幸通り", Ref: null, DistanceMeters: 12) });
        await second.ConfigureAwait(true);

        Assert.Equal("35.681236, 139.767125", viewModel.CoordinatesText);
        Assert.Equal("千代田区 丸の内", viewModel.AreaNameText);
        Assert.Equal("行幸通り", viewModel.RoadNameText);
    }

    [Fact]
    public async Task SetLocationAsyncClearsThePreviousLocationResultsWhileResolving()
    {
        var areaResolver = new FakeAreaNameResolver();
        var viewModel = new MapLocationInfoViewModel(areaResolver);
        var first = viewModel.SetLocationAsync(43.468212, 142.482015);
        areaResolver.Calls[0].Complete(Miyamachi);
        await first.ConfigureAwait(true);

        var second = viewModel.SetLocationAsync(35.681236, 139.767125);

        Assert.Equal(string.Empty, viewModel.AreaNameText);
        areaResolver.Calls[1].Complete(null);
        await second.ConfigureAwait(true);
    }

    [Fact]
    public async Task SetLocationAsyncDoesNotResolveAgainForTheSameLocation()
    {
        var areaResolver = new FakeAreaNameResolver();
        var viewModel = new MapLocationInfoViewModel(areaResolver);
        var first = viewModel.SetLocationAsync(43.468212, 142.482015);
        areaResolver.Calls[0].Complete(Miyamachi);
        await first.ConfigureAwait(true);

        await viewModel.SetLocationAsync(43.468212, 142.482015).ConfigureAwait(true);

        Assert.Single(areaResolver.Calls);
        Assert.Equal("上富良野町 宮町", viewModel.AreaNameText);
    }

    [Fact]
    public async Task ClearCancelsResolutionAndHidesAllItems()
    {
        var areaResolver = new FakeAreaNameResolver();
        var roadSuggester = new FakeRoadNameSuggester();
        var viewModel = new MapLocationInfoViewModel(areaResolver, roadSuggester);
        var update = viewModel.SetLocationAsync(43.468212, 142.482015);

        viewModel.Clear();
        await update.ConfigureAwait(true);

        Assert.True(areaResolver.Calls[0].CancellationToken.IsCancellationRequested);
        Assert.True(roadSuggester.Calls[0].CancellationToken.IsCancellationRequested);
        Assert.Equal(Visibility.Collapsed, viewModel.AreaNameVisibility);
        Assert.Equal(Visibility.Collapsed, viewModel.RoadNameVisibility);
        Assert.Equal(Visibility.Collapsed, viewModel.CoordinatesVisibility);
    }

    [Theory]
    [InlineData(false, true, 0, 1)]
    [InlineData(true, false, 1, 0)]
    [InlineData(false, false, 0, 0)]
    public async Task DisabledItemsAreNotResolved(
        bool showAddress,
        bool showRoadName,
        int expectedAreaCalls,
        int expectedRoadCalls)
    {
        var areaResolver = new FakeAreaNameResolver { CompleteImmediatelyWith = Miyamachi };
        var roadSuggester = new FakeRoadNameSuggester
        {
            CompleteImmediatelyWith = new[] { new RoadNameCandidate("富良野平原広域農道", Ref: null, DistanceMeters: 4) }
        };
        var viewModel = new MapLocationInfoViewModel(areaResolver, roadSuggester);
        viewModel.DisplaySettings.ShowAddress = showAddress;
        viewModel.DisplaySettings.ShowRoadName = showRoadName;

        await viewModel.SetLocationAsync(43.468212, 142.482015).ConfigureAwait(true);

        Assert.Equal(expectedAreaCalls, areaResolver.Calls.Count);
        Assert.Equal(expectedRoadCalls, roadSuggester.Calls.Count);
        Assert.Equal(showAddress ? Visibility.Visible : Visibility.Collapsed, viewModel.AreaNameVisibility);
        Assert.Equal(showRoadName ? Visibility.Visible : Visibility.Collapsed, viewModel.RoadNameVisibility);
    }

    [Fact]
    public async Task TurningAnItemOffCancelsItsResolutionAndTurningItOnResolvesIt()
    {
        var areaResolver = new FakeAreaNameResolver();
        var roadSuggester = new FakeRoadNameSuggester
        {
            CompleteImmediatelyWith = new[] { new RoadNameCandidate("富良野平原広域農道", Ref: null, DistanceMeters: 4) }
        };
        var viewModel = new MapLocationInfoViewModel(areaResolver, roadSuggester);
        var update = viewModel.SetLocationAsync(43.468212, 142.482015);

        viewModel.DisplaySettings.ShowAddress = false;
        await update.ConfigureAwait(true);

        Assert.True(areaResolver.Calls[0].CancellationToken.IsCancellationRequested);
        Assert.Equal(string.Empty, viewModel.AreaNameText);

        viewModel.DisplaySettings.ShowAddress = true;
        areaResolver.Calls[1].Complete(Miyamachi);
        await areaResolver.Calls[1].Completed.ConfigureAwait(true);
        await WaitForAsync(() => viewModel.AreaNameText.Length > 0).ConfigureAwait(true);

        Assert.Equal("上富良野町 宮町", viewModel.AreaNameText);
        // 道路名は切り替えていないので、解決し直さない
        Assert.Single(roadSuggester.Calls);
    }

    [Fact]
    public async Task ShowCoordinatesTogglesCoordinatesText()
    {
        var viewModel = new MapLocationInfoViewModel();
        await viewModel.SetLocationAsync(43.468212, 142.482015).ConfigureAwait(true);

        viewModel.DisplaySettings.ShowCoordinates = false;
        Assert.Equal(Visibility.Collapsed, viewModel.CoordinatesVisibility);

        viewModel.DisplaySettings.ShowCoordinates = true;
        Assert.Equal("43.468212, 142.482015", viewModel.CoordinatesText);
    }

    [Fact]
    public async Task IncludePrefectureReformatsTheAreaNameWithoutResolvingAgain()
    {
        var areaResolver = new FakeAreaNameResolver { CompleteImmediatelyWith = Miyamachi };
        var viewModel = new MapLocationInfoViewModel(areaResolver);
        await viewModel.SetLocationAsync(43.468212, 142.482015).ConfigureAwait(true);

        viewModel.DisplaySettings.IncludePrefecture = true;

        Assert.Equal("北海道 上富良野町 宮町", viewModel.AreaNameText);
        Assert.Single(areaResolver.Calls);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(HttpRequestException))]
    public async Task ResolverFailureHidesOnlyThatItem(Type exceptionType)
    {
        var areaResolver = new FakeAreaNameResolver { CompleteImmediatelyWith = Miyamachi };
        var roadSuggester = new FakeRoadNameSuggester();
        var viewModel = new MapLocationInfoViewModel(areaResolver, roadSuggester);

        var update = viewModel.SetLocationAsync(43.468212, 142.482015);
        roadSuggester.Calls[0].Fail((Exception)Activator.CreateInstance(exceptionType)!);
        await update.ConfigureAwait(true);

        Assert.Equal("上富良野町 宮町", viewModel.AreaNameText);
        Assert.Equal(Visibility.Collapsed, viewModel.RoadNameVisibility);
        Assert.Equal(Visibility.Visible, viewModel.CoordinatesVisibility);
    }

    [Fact]
    public async Task PropertyChangedIsRaisedOnTheCallerContextWhenResolversCompleteOnAnotherThread()
    {
        var areaResolver = new FakeAreaNameResolver();
        var roadSuggester = new FakeRoadNameSuggester();
        var viewModel = new MapLocationInfoViewModel(areaResolver, roadSuggester);
        var threadIds = new List<int>();
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, e) =>
        {
            lock (threadIds)
            {
                threadIds.Add(Environment.CurrentManagedThreadId);
                changedProperties.Add(e.PropertyName);
            }
        };

        using var context = new SingleThreadSynchronizationContext();
        var run = context.RunAsync(async () =>
        {
            var update = viewModel.SetLocationAsync(43.468212, 142.482015);

            // 解決器はスレッドプール上で完了する
            _ = Task.Run(() =>
            {
                areaResolver.Calls[0].Complete(Miyamachi);
                roadSuggester.Calls[0].Complete(new[] { new RoadNameCandidate("富良野平原広域農道", Ref: null, DistanceMeters: 4) });
            });
            await update.ConfigureAwait(true);
        });

        await TestTasks.AssertCompletesWithinAsync(run, TimeSpan.FromSeconds(5)).ConfigureAwait(true);
        await run.ConfigureAwait(true);

        lock (threadIds)
        {
            Assert.Contains(nameof(MapLocationInfoViewModel.AreaNameText), changedProperties);
            Assert.Contains(nameof(MapLocationInfoViewModel.RoadNameText), changedProperties);
            Assert.Contains(nameof(MapLocationInfoViewModel.CoordinatesText), changedProperties);
            Assert.All(threadIds, id => Assert.Equal(context.ThreadId, id));
        }
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            Assert.True(Environment.TickCount64 < deadline, "Timed out waiting for the condition.");
            await Task.Delay(10).ConfigureAwait(true);
        }
    }

    private sealed class ResolverCall<T>
    {
        // 完了スレッド上で await の続きを走らせない（UI スレッドへ戻ることを検証するため）
        private readonly TaskCompletionSource<T> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ResolverCall(double latitude, double longitude, bool ignoreCancellation, CancellationToken cancellationToken)
        {
            Latitude = latitude;
            Longitude = longitude;
            CancellationToken = cancellationToken;
            if (!ignoreCancellation)
            {
                cancellationToken.Register(() => _completion.TrySetCanceled(cancellationToken));
            }
        }

        public double Latitude { get; }

        public double Longitude { get; }

        public CancellationToken CancellationToken { get; }

        public Task<T> Completed => _completion.Task;

        public void Complete(T result) => _completion.TrySetResult(result);

        public void Fail(Exception exception) => _completion.TrySetException(exception);
    }

    private sealed class FakeAreaNameResolver : IAreaNameResolver
    {
        public event EventHandler? ResultsInvalidated;

        public void RaiseResultsInvalidated() => ResultsInvalidated?.Invoke(this, EventArgs.Empty);

        public List<ResolverCall<AreaName?>> Calls { get; } = new();

        public AreaName? CompleteImmediatelyWith { get; init; }

        public bool IgnoreCancellation { get; init; }

        public Task<AreaName?> ResolveAsync(double latitude, double longitude, CancellationToken cancellationToken)
        {
            var call = new ResolverCall<AreaName?>(latitude, longitude, IgnoreCancellation, cancellationToken);
            Calls.Add(call);
            if (CompleteImmediatelyWith is not null)
            {
                call.Complete(CompleteImmediatelyWith);
            }

            return call.Completed;
        }
    }

    private sealed class FakeRoadNameSuggester : IRoadNameSuggester
    {
        public List<ResolverCall<IReadOnlyList<RoadNameCandidate>>> Calls { get; } = new();

        public IReadOnlyList<RoadNameCandidate>? CompleteImmediatelyWith { get; init; }

        public bool IgnoreCancellation { get; init; }

        public Task<IReadOnlyList<RoadNameCandidate>> SuggestAsync(double latitude, double longitude, CancellationToken cancellationToken)
        {
            var call = new ResolverCall<IReadOnlyList<RoadNameCandidate>>(latitude, longitude, IgnoreCancellation, cancellationToken);
            Calls.Add(call);
            if (CompleteImmediatelyWith is not null)
            {
                call.Complete(CompleteImmediatelyWith);
            }

            return call.Completed;
        }
    }
}
