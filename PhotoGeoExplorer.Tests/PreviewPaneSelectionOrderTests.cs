using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Preview;
using PhotoGeoExplorer.State;
using PhotoGeoExplorer.ViewModels;
using Xunit;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// 表示の更新は UI のキューへ後回しになるため、その間に届いた次の選択を打ち消してはならない（#388）。
/// BitmapImage は単体テストで作れないため、どの写真を表示しているかはメタデータの表示で確かめる
/// </summary>
public sealed class PreviewPaneSelectionOrderTests
{
    private const string PhotoA = "a.jpg";
    private const string PhotoB = "b.jpg";

    // 地図の矩形選択は、一覧の選択を空にしてから入れ直すため、「空」と「選択」が続けて届く
    [Theory]
    [InlineData(PhotoA, 1)]
    [InlineData(PhotoB, 1)]
    public void SelectionAfterAClearThatHasNotRunYetIsShown(string reselected, int expectedLoads)
    {
        var context = new Context();
        context.Select(PhotoA);
        context.Queue.RunAll();

        context.Select();
        context.Select(reselected);
        context.Queue.RunAll();

        Assert.Equal(reselected, context.ViewModel.MetadataSummary);
        Assert.Equal(expectedLoads, context.Service.LoadCount(reselected));
    }

    [Fact]
    public void ClearingTheSelectionEmptiesThePreviewAndReselectingLoadsAgain()
    {
        var context = new Context();
        context.Select(PhotoA);
        context.Queue.RunAll();

        context.Select();
        context.Queue.RunAll();
        Assert.Null(context.ViewModel.MetadataSummary);

        context.Select(PhotoA);
        context.Queue.RunAll();

        Assert.Equal(PhotoA, context.ViewModel.MetadataSummary);
        Assert.Equal(2, context.Service.LoadCount(PhotoA));
    }

    [Theory]
    [InlineData(PhotoB, PhotoB)]
    [InlineData(null, null)]
    public void LoadThatFinishesAfterANewerSelectionIsDiscarded(string? newer, string? expected)
    {
        var context = new Context();
        var slowLoad = context.Service.Hold(PhotoA);
        context.Select(PhotoA);

        context.Select(newer is null ? [] : [newer]);
        context.Queue.RunAll();

        slowLoad.SetResult(null);
        context.Queue.RunNext();

        Assert.Equal(expected, context.ViewModel.MetadataSummary);
    }

    private sealed class Context
    {
        private readonly WorkspaceState _workspaceState = new();

        public Context()
        {
            ViewModel = new PreviewPaneViewModel(Service, _workspaceState);
            ViewModel.InitializeUiPost(Queue.Post);
        }

        public FakeService Service { get; } = new();

        public ManualUiQueue Queue { get; } = new();

        public PreviewPaneViewModel ViewModel { get; }

        public void Select(params string[] fileNames)
            => _workspaceState.SelectedPhotos = fileNames.Select(CreateItem).ToList();

        private static PhotoListItem CreateItem(string fileName)
        {
            var item = new PhotoItem(
                filePath: fileName,
                sizeBytes: 1,
                modifiedAt: DateTimeOffset.UnixEpoch,
                isFolder: false,
                thumbnailPath: null,
                pixelWidth: null,
                pixelHeight: null);
            return new PhotoListItem(item, thumbnail: null, toolTipText: null, thumbnailKey: null);
        }
    }

    private sealed class ManualUiQueue
    {
        private readonly ConcurrentQueue<Action> _actions = new();

        public void Post(Action action) => _actions.Enqueue(action);

        public void RunAll()
        {
            while (_actions.TryDequeue(out var action))
            {
                action();
            }
        }

        // 読み込みの完了後の投入は別のスレッドから届くので、届くまで待つ
        public void RunNext()
        {
            Action? action = null;
            Assert.True(SpinWait.SpinUntil(() => _actions.TryDequeue(out action), TimeSpan.FromSeconds(10)));
            action!();
        }
    }

    // カメラ名にファイル名を返すので、メタデータの表示が写真を表す
    private sealed class FakeService : IPreviewPaneService
    {
        private readonly Dictionary<string, TaskCompletionSource<BitmapImage?>> _held = [];
        private readonly List<string> _loads = [];

        public TaskCompletionSource<BitmapImage?> Hold(string filePath)
            => _held[filePath] = new TaskCompletionSource<BitmapImage?>();

        public int LoadCount(string filePath) => _loads.Count(path => path == filePath);

        public Task<BitmapImage?> LoadImageAsync(string filePath)
        {
            _loads.Add(filePath);
            return _held.TryGetValue(filePath, out var held) ? held.Task : Task.FromResult<BitmapImage?>(null);
        }

        public Task<PhotoMetadata?> GetMetadataAsync(string filePath, CancellationToken cancellationToken)
            => Task.FromResult<PhotoMetadata?>(new PhotoMetadata(null, null, filePath, null, null));
    }
}
