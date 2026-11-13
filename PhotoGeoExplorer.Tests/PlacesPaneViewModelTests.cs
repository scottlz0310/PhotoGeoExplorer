using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Places;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.Services.AreaTree;
using PhotoGeoExplorer.State;
using PhotoGeoExplorer.ViewModels;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public class PlacesPaneViewModelTests
{
    private static readonly PhotoCoordinate TottoriPoint = new(35.49, 134.22);
    private static readonly PhotoCoordinate IwamiPoint = new(35.58, 134.33);
    private static readonly AreaName Tottori = new("312010870", "鳥取県", "鳥取市", "東品治町", SnappedToNearest: false, CensusYear: 2020);
    private static readonly AreaName Iwami = new("313010010", "鳥取県", "岩美町", "浦富", SnappedToNearest: false, CensusYear: 2020);

    [Fact]
    public async Task ActivatingBuildsTheTreeOfThePhotosOfTheFolder()
    {
        using var fixture = new Fixture();
        fixture.SetPhotos(
            ("/p/a.jpg", TottoriPoint),
            ("/p/b.jpg", TottoriPoint),
            ("/p/c.jpg", IwamiPoint),
            ("/p/d.jpg", null));

        fixture.ViewModel.IsActive = true;
        await fixture.WaitUntilIdleAsync().ConfigureAwait(true);

        // テスト環境ではローカライズの文言が引けず、キーがそのまま返る
        Assert.Equal(
            ["鳥取市 (2)", "岩美町 (1)", "Places.Node.NoLocation (1)"],
            fixture.ViewModel.Roots.Select(node => node.DisplayText));
        Assert.Equal(["東品治町 (2)"], fixture.ViewModel.Roots[0].Children.Select(node => node.DisplayText));
        Assert.Equal(["/p/a.jpg", "/p/b.jpg"], fixture.ViewModel.Roots[0].Children[0].FilePaths);
        Assert.Equal("Places.Status.Done", fixture.ViewModel.StatusText);
        Assert.False(fixture.ViewModel.IsBusy);
        Assert.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, fixture.ViewModel.ProgressVisibility);
    }

    [Fact]
    public async Task ThePhotosAreNotReadWhileThePaneIsNotShown()
    {
        using var fixture = new Fixture();
        fixture.SetPhotos(("/p/a.jpg", TottoriPoint));

        await Task.Delay(100).ConfigureAwait(true);

        Assert.Empty(fixture.Reader.ReadPaths);
        Assert.Empty(fixture.ViewModel.Roots);

        // 表示されたら、そのときの写真を判定する
        fixture.ViewModel.IsActive = true;
        await fixture.WaitUntilIdleAsync().ConfigureAwait(true);
        Assert.Single(fixture.ViewModel.Roots);
    }

    [Fact]
    public async Task ChangingThePhotosWhileTheFolderIsShownBuildsTheTreeAgain()
    {
        using var fixture = new Fixture();
        fixture.SetPhotos(("/p/a.jpg", TottoriPoint));
        fixture.ViewModel.IsActive = true;
        await fixture.WaitUntilIdleAsync().ConfigureAwait(true);
        Assert.Equal(["鳥取市 (1)"], fixture.ViewModel.Roots.Select(node => node.DisplayText));

        fixture.SetPhotos(("/q/x.jpg", IwamiPoint), ("/q/y.jpg", IwamiPoint));
        await fixture.WaitUntilIdleAsync().ConfigureAwait(true);

        Assert.Equal(["岩美町 (2)"], fixture.ViewModel.Roots.Select(node => node.DisplayText));
    }

    [Fact]
    public async Task ThePhotosChangedWhileThePaneWasHiddenAreClassifiedWhenItIsShownAgain()
    {
        using var fixture = new Fixture();
        fixture.SetPhotos(("/p/a.jpg", TottoriPoint));
        fixture.ViewModel.IsActive = true;
        await fixture.WaitUntilIdleAsync().ConfigureAwait(true);

        fixture.ViewModel.IsActive = false;
        fixture.SetPhotos(("/q/x.jpg", IwamiPoint));
        Assert.Equal(["鳥取市 (1)"], fixture.ViewModel.Roots.Select(node => node.DisplayText));

        fixture.ViewModel.IsActive = true;
        await fixture.WaitUntilIdleAsync().ConfigureAwait(true);
        Assert.Equal(["岩美町 (1)"], fixture.ViewModel.Roots.Select(node => node.DisplayText));
    }

    [Fact]
    public async Task WhenNoPhotoCouldBeResolvedTheStatusGuidesTheImportOfTheAreaData()
    {
        using var fixture = new Fixture();
        fixture.Resolver.Remove(TottoriPoint);
        fixture.Resolver.Remove(IwamiPoint);
        fixture.SetPhotos(("/p/a.jpg", TottoriPoint), ("/p/b.jpg", null));

        fixture.ViewModel.IsActive = true;
        await fixture.WaitUntilIdleAsync().ConfigureAwait(true);

        Assert.Equal("Places.Status.DoneNoAreaData", fixture.ViewModel.StatusText);
    }

    [Fact]
    public async Task WhenSomePhotosWereResolvedTheStatusDoesNotGuideTheImport()
    {
        using var fixture = new Fixture();
        fixture.Resolver.Remove(IwamiPoint);
        fixture.SetPhotos(("/p/a.jpg", TottoriPoint), ("/p/b.jpg", IwamiPoint));

        fixture.ViewModel.IsActive = true;
        await fixture.WaitUntilIdleAsync().ConfigureAwait(true);

        Assert.Equal("Places.Status.Done", fixture.ViewModel.StatusText);
    }

    [Fact]
    public async Task AnEmptyFolderShowsNoPhotos()
    {
        using var fixture = new Fixture();
        fixture.ViewModel.IsActive = true;
        await fixture.WaitUntilIdleAsync().ConfigureAwait(true);

        Assert.Empty(fixture.ViewModel.Roots);
        Assert.Equal("Places.Status.NoPhotos", fixture.ViewModel.StatusText);
        Assert.False(fixture.ViewModel.IsBusy);
    }

    [Fact]
    public async Task AResultOfAFolderThatWasLeftIsNotShown()
    {
        using var fixture = new Fixture { HoldReads = true };
        fixture.SetPhotos(("/p/a.jpg", TottoriPoint));
        fixture.ViewModel.IsActive = true;
        await fixture.Reader.WaitForReadsAsync(1).ConfigureAwait(true);

        // 判定の途中で別のフォルダーへ移った
        fixture.SetPhotos(("/q/x.jpg", IwamiPoint));
        await fixture.Reader.WaitForReadsAsync(2).ConfigureAwait(true);
        fixture.Reader.Release();
        await fixture.WaitUntilIdleAsync().ConfigureAwait(true);

        Assert.Equal(["岩美町 (1)"], fixture.ViewModel.Roots.Select(node => node.DisplayText));
    }

    [Fact]
    public async Task CancelingStopsTheClassificationAndKeepsWhatWasShown()
    {
        using var fixture = new Fixture { HoldReads = true };
        fixture.SetPhotos(("/p/a.jpg", TottoriPoint), ("/p/b.jpg", IwamiPoint));
        fixture.ViewModel.IsActive = true;
        await fixture.Reader.WaitForReadsAsync(1).ConfigureAwait(true);
        Assert.True(fixture.ViewModel.IsBusy);

        fixture.ViewModel.CancelCommand.Execute(null);

        Assert.False(fixture.ViewModel.IsBusy);
        Assert.Equal("Places.Status.Canceled", fixture.ViewModel.StatusText);
        fixture.Reader.Release();
        await Task.Delay(100).ConfigureAwait(true);
        Assert.Empty(fixture.ViewModel.Roots);
        Assert.Equal("Places.Status.Canceled", fixture.ViewModel.StatusText);
    }

    [Fact]
    public async Task HidingThePaneStopsTheClassification()
    {
        using var fixture = new Fixture { HoldReads = true };
        fixture.SetPhotos(("/p/a.jpg", TottoriPoint));
        fixture.ViewModel.IsActive = true;
        await fixture.Reader.WaitForReadsAsync(1).ConfigureAwait(true);

        fixture.ViewModel.IsActive = false;

        Assert.False(fixture.ViewModel.IsBusy);
        fixture.Reader.Release();
        await Task.Delay(100).ConfigureAwait(true);
        Assert.Empty(fixture.ViewModel.Roots);
    }

    [Fact]
    public async Task ImportingAreaDataClassifiesTheFolderAgain()
    {
        using var fixture = new Fixture();
        fixture.SetPhotos(("/p/a.jpg", TottoriPoint));
        fixture.Resolver.Remove(TottoriPoint);
        fixture.ViewModel.IsActive = true;
        await fixture.WaitUntilIdleAsync().ConfigureAwait(true);
        Assert.Equal(["Places.Node.NoAreaData (1)"], fixture.ViewModel.Roots.Select(node => node.DisplayText));

        fixture.Resolver[TottoriPoint] = Tottori;
        fixture.Resolver.RaiseResultsInvalidated();
        await fixture.WaitUntilIdleAsync().ConfigureAwait(true);

        Assert.Equal(["鳥取市 (1)"], fixture.ViewModel.Roots.Select(node => node.DisplayText));
    }

    [Fact]
    public async Task AFailureToReadTheAreaDataShowsAnErrorWithoutATree()
    {
        using var fixture = new Fixture();
        fixture.SetPhotos(("/p/a.jpg", TottoriPoint));
        fixture.Resolver.Failure = new IOException("broken area data");

        fixture.ViewModel.IsActive = true;
        await fixture.WaitUntilIdleAsync().ConfigureAwait(true);

        Assert.Equal("Places.Status.Error", fixture.ViewModel.StatusText);
        Assert.Empty(fixture.ViewModel.Roots);
        Assert.False(fixture.ViewModel.IsBusy);
    }

    [Fact]
    public async Task SelectingANodeRequestsTheSelectionOfItsPhotos()
    {
        using var fixture = new Fixture();
        fixture.SetPhotos(("/p/a.jpg", TottoriPoint), ("/p/b.jpg", TottoriPoint), ("/p/c.jpg", IwamiPoint));
        fixture.ViewModel.IsActive = true;
        await fixture.WaitUntilIdleAsync().ConfigureAwait(true);
        var requested = new List<IReadOnlyList<string>>();
        fixture.Workspace.PhotoSelectionRequested += (_, e) => requested.Add(e.FilePaths);

        fixture.ViewModel.SelectNode(fixture.ViewModel.Roots[0]);
        fixture.ViewModel.SelectNode(fixture.ViewModel.Roots[1]);

        Assert.Equal(["/p/a.jpg", "/p/b.jpg"], requested[0]);
        Assert.Equal(["/p/c.jpg"], requested[1]);
    }

    [Fact]
    public async Task TheExpandedNodesStayExpandedWhenTheTreeIsBuiltAgain()
    {
        using var fixture = new Fixture();
        fixture.SetPhotos(("/p/a.jpg", TottoriPoint), ("/p/c.jpg", IwamiPoint));
        fixture.ViewModel.IsActive = true;
        await fixture.WaitUntilIdleAsync().ConfigureAwait(true);
        fixture.ViewModel.Roots[0].IsExpanded = true;
        Assert.False(fixture.ViewModel.Roots[1].IsExpanded);

        fixture.SetPhotos(("/p/a.jpg", TottoriPoint), ("/p/c.jpg", IwamiPoint), ("/p/d.jpg", IwamiPoint));
        await fixture.WaitUntilIdleAsync().ConfigureAwait(true);

        Assert.True(fixture.ViewModel.Roots[0].IsExpanded);
        Assert.False(fixture.ViewModel.Roots[1].IsExpanded);

        fixture.ViewModel.Roots[0].IsExpanded = false;
        fixture.SetPhotos(("/p/a.jpg", TottoriPoint));
        await fixture.WaitUntilIdleAsync().ConfigureAwait(true);
        Assert.False(fixture.ViewModel.Roots[0].IsExpanded);
    }

    [Fact]
    public async Task TheTreeIsUpdatedWhileTheClassificationIsRunning()
    {
        var dispatcher = new QueuedDispatcher();
        using var fixture = new Fixture { Dispatcher = dispatcher, ProgressBatchSize = 1, MaxParallelism = 1 };
        fixture.SetPhotos(("/p/a.jpg", TottoriPoint), ("/p/b.jpg", IwamiPoint));
        fixture.Reader.HoldAfter(1);
        fixture.ViewModel.IsActive = true;
        await fixture.Reader.WaitForReadsAsync(2).ConfigureAwait(true);

        // 1 枚目の結果だけが出た時点で、ツリーに反映できる
        dispatcher.RunQueued();

        Assert.True(fixture.ViewModel.IsBusy);
        Assert.Equal(["鳥取市 (1)"], fixture.ViewModel.Roots.Select(node => node.DisplayText));
        Assert.Equal(1, fixture.ViewModel.ProgressValue);
        Assert.Equal(2, fixture.ViewModel.ProgressMaximum);

        fixture.Reader.Release();
        await fixture.WaitUntilIdleAsync().ConfigureAwait(true);
        Assert.Equal(2, fixture.ViewModel.Roots.Count);
    }

    [Fact]
    public async Task CleanupStopsListeningToTheWorkspace()
    {
        using var fixture = new Fixture();
        fixture.SetPhotos(("/p/a.jpg", TottoriPoint));
        fixture.ViewModel.IsActive = true;
        await fixture.WaitUntilIdleAsync().ConfigureAwait(true);

        fixture.ViewModel.Cleanup();
        fixture.SetPhotos(("/q/x.jpg", IwamiPoint));
        await Task.Delay(100).ConfigureAwait(true);

        Assert.Equal(["鳥取市 (1)"], fixture.ViewModel.Roots.Select(node => node.DisplayText));
    }

    [Fact]
    public void ConstructorRejectsNullArguments()
    {
        using var fixture = new Fixture();
        var classifier = fixture.Classifier;
        Assert.Throws<ArgumentNullException>(() => new PlacesPaneViewModel(null!, classifier, fixture.Resolver, fixture.Dispatcher));
        Assert.Throws<ArgumentNullException>(() => new PlacesPaneViewModel(fixture.Workspace, null!, fixture.Resolver, fixture.Dispatcher));
        Assert.Throws<ArgumentNullException>(() => new PlacesPaneViewModel(fixture.Workspace, classifier, null!, fixture.Dispatcher));
        Assert.Throws<ArgumentNullException>(() => new PlacesPaneViewModel(fixture.Workspace, classifier, fixture.Resolver, null!));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly Lazy<PlacesPaneViewModel> _viewModel;
        private readonly Lazy<PhotoAreaClassifier> _classifier;

        public Fixture()
        {
            Resolver[TottoriPoint] = Tottori;
            Resolver[IwamiPoint] = Iwami;
            _classifier = new Lazy<PhotoAreaClassifier>(() => new PhotoAreaClassifier(
                Reader.ReadAsync,
                Resolver,
                MaxParallelism,
                ProgressBatchSize));
            _viewModel = new Lazy<PlacesPaneViewModel>(() => new PlacesPaneViewModel(Workspace, Classifier, Resolver, Dispatcher));
        }

        public WorkspaceState Workspace { get; } = new();

        public FakeReader Reader { get; } = new();

        public FakeResolver Resolver { get; } = new();

        public IUiDispatcher Dispatcher { get; init; } = new NoDispatcher();

        public int MaxParallelism { get; init; } = PhotoAreaClassifier.DefaultMaxDegreeOfParallelism;

        public int ProgressBatchSize { get; init; } = PhotoAreaClassifier.DefaultProgressBatchSize;

        public bool HoldReads
        {
            init => Reader.HoldAll = value;
        }

        public PhotoAreaClassifier Classifier => _classifier.Value;

        public PlacesPaneViewModel ViewModel => _viewModel.Value;

        public void SetPhotos(params (string Path, PhotoCoordinate? Coordinate)[] photos)
        {
            _ = ViewModel;
            foreach (var (path, coordinate) in photos)
            {
                Reader.Set(path, coordinate);
            }

            Workspace.PhotoItems = photos.Select(photo => CreateItem(photo.Path)).ToList();
        }

        public async Task WaitUntilIdleAsync()
        {
            var deadline = Environment.TickCount64 + 5000;
            await Task.Yield();
            while (ViewModel.IsBusy || ViewModel.StatusText.Length == 0)
            {
                Assert.True(Environment.TickCount64 < deadline, "判定が終わりませんでした。");
                await Task.Delay(10).ConfigureAwait(true);
            }

            // 終わった直後の状態更新を待つ
            await Task.Delay(30).ConfigureAwait(true);
        }

        public void Dispose()
        {
            if (_viewModel.IsValueCreated)
            {
                _viewModel.Value.Cleanup();
            }
        }

        private static PhotoListItem CreateItem(string path)
            => new(new PhotoItem(path, 1000, DateTimeOffset.UnixEpoch, isFolder: false), thumbnail: null);
    }

    private sealed class FakeReader
    {
        private readonly ConcurrentDictionary<string, PhotoCoordinate?> _coordinates = new(StringComparer.Ordinal);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _reads;
        private int _holdAfter = -1;

        public ConcurrentQueue<string> ReadPaths { get; } = new();

        public bool HoldAll { get; set; }

        public void Set(string path, PhotoCoordinate? coordinate) => _coordinates[path] = coordinate;

        // 指定の枚数を読んだ後の読み取りを止める
        public void HoldAfter(int count) => _holdAfter = count;

        public void Release() => _release.TrySetResult();

        public async Task WaitForReadsAsync(int count)
        {
            var deadline = Environment.TickCount64 + 5000;
            while (Volatile.Read(ref _reads) < count)
            {
                Assert.True(Environment.TickCount64 < deadline, $"{count} 件の読み取りが始まりませんでした。");
                await Task.Delay(10).ConfigureAwait(false);
            }
        }

        public async Task<PhotoCoordinate?> ReadAsync(string path, CancellationToken cancellationToken)
        {
            ReadPaths.Enqueue(path);
            var number = Interlocked.Increment(ref _reads);
            if (HoldAll || (_holdAfter >= 0 && number > _holdAfter))
            {
                await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            return _coordinates.GetValueOrDefault(path);
        }
    }

    private sealed class FakeResolver : IAreaNameResolver
    {
        private readonly ConcurrentDictionary<PhotoCoordinate, AreaName> _areas = new();

        public event EventHandler? ResultsInvalidated;

        public IOException? Failure { get; set; }

        public AreaName this[PhotoCoordinate coordinate]
        {
            set => _areas[coordinate] = value;
        }

        public void Remove(PhotoCoordinate coordinate) => _areas.TryRemove(coordinate, out _);

        public void RaiseResultsInvalidated() => ResultsInvalidated?.Invoke(this, EventArgs.Empty);

        public Task<AreaName?> ResolveAsync(double latitude, double longitude, CancellationToken cancellationToken)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult(_areas.GetValueOrDefault(new PhotoCoordinate(latitude, longitude)));
        }
    }

    private sealed class NoDispatcher : IUiDispatcher
    {
        public bool IsAvailable => false;

        public Task RunAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task<T> EnqueueAsync<T>(Func<Task<T>> asyncFunc) => asyncFunc();

        public bool TryEnqueue(Action action) => false;

        public IUiDispatcherTimer? CreateTimer() => null;
    }

    private sealed class QueuedDispatcher : IUiDispatcher
    {
        private readonly ConcurrentQueue<Action> _queue = new();

        public bool IsAvailable => true;

        public void RunQueued()
        {
            while (_queue.TryDequeue(out var action))
            {
                action();
            }
        }

        public Task RunAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task<T> EnqueueAsync<T>(Func<Task<T>> asyncFunc) => asyncFunc();

        public bool TryEnqueue(Action action)
        {
            _queue.Enqueue(action);
            return true;
        }

        public IUiDispatcherTimer? CreateTimer() => null;
    }
}
