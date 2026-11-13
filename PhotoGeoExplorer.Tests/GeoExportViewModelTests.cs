using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Export;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.Services.AreaTree;
using PhotoGeoExplorer.Services.Export;
using PhotoGeoExplorer.State;
using PhotoGeoExplorer.ViewModels;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public class GeoExportViewModelTests
{
    private static readonly AreaName Higashi = new("312010870", "鳥取県", "鳥取市", "東品治町", SnappedToNearest: false, CensusYear: 2020);
    private static readonly DateTimeOffset TakenAt = new(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(9));

    [Fact]
    public async Task ExportCsvAsyncWritesTheSelectedPhotosToTheChosenFile()
    {
        using var fixture = new Fixture { CsvPath = "/out/list.csv" };
        fixture.Select("/p/a.jpg", "/p/b.jpg");

        await fixture.ViewModel.ExportCsvAsync().ConfigureAwait(true);

        Assert.Equal("/out/list.csv", fixture.Service.CsvPath);
        Assert.Equal(["/p/a.jpg", "/p/b.jpg"], fixture.Service.CsvRecords.Select(record => record.FilePath).Order());
        Assert.Equal(PhotoAreaKind.Resolved, fixture.Service.CsvRecords[0].Kind);
        var (message, severity) = Assert.Single(fixture.Ui.Notifications);
        Assert.Equal(("GeoExport.Message.CsvDone", InfoBarSeverity.Success), (message, severity));
        Assert.False(fixture.ViewModel.IsBusy);
        Assert.Equal(string.Empty, fixture.ViewModel.ProgressText);
    }

    [Fact]
    public async Task ExportCsvAsyncDoesNothingWhenThePickerIsCanceled()
    {
        using var fixture = new Fixture { CsvPath = null };
        fixture.Select("/p/a.jpg");

        await fixture.ViewModel.ExportCsvAsync().ConfigureAwait(true);

        Assert.Null(fixture.Service.CsvPath);
        Assert.Empty(fixture.Ui.Notifications);
        Assert.Equal(0, fixture.Reader.ReadCount);
    }

    [Fact]
    public async Task ExportCsvAsyncTellsTheUserWhenTheFileCannotBeWritten()
    {
        using var fixture = new Fixture { CsvPath = "/out/list.csv" };
        fixture.Select("/p/a.jpg");
        fixture.Service.CsvFailure = new IOException("disk full");

        await fixture.ViewModel.ExportCsvAsync().ConfigureAwait(true);

        var (message, severity) = Assert.Single(fixture.Ui.Notifications);
        Assert.Equal(("GeoExport.Message.CsvFailed", InfoBarSeverity.Error), (message, severity));
        Assert.False(fixture.ViewModel.IsBusy);
    }

    [Fact]
    public async Task ExportCsvAsyncIncludesAConfirmedRoadNameWithoutFetchingCandidates()
    {
        using var fixture = new Fixture { CsvPath = "/out/list.csv" };
        fixture.RoadStore.Items["/p/a.jpg"] = new ConfirmedRoadName("丸山通", null);
        fixture.SuggestRoadNames = false;
        fixture.Select("/p/a.jpg", "/p/b.jpg");

        await fixture.ViewModel.ExportCsvAsync().ConfigureAwait(true);

        // 設定がオフなら通信しない。確定した写真だけに、道路名が付く
        var roads = fixture.Service.CsvRecords.ToDictionary(record => record.FilePath, record => record.Road);
        Assert.Equal(new RoadNameInfo(RoadNameStatus.Confirmed, "丸山通", null), roads["/p/a.jpg"]);
        Assert.Null(roads["/p/b.jpg"]);
        Assert.Equal(0, fixture.RoadSuggester.CallCount);
        Assert.Equal(("GeoExport.Message.CsvDone", InfoBarSeverity.Success), Assert.Single(fixture.Ui.Notifications));
    }

    [Fact]
    public async Task ExportCsvAsyncFetchesTheFirstCandidateOnlyWhenRoadNamesAreEnabled()
    {
        using var fixture = new Fixture { CsvPath = "/out/list.csv" };
        fixture.RoadStore.Items["/p/a.jpg"] = new ConfirmedRoadName("丸山通", null);
        fixture.SuggestRoadNames = true;
        fixture.Select("/p/a.jpg", "/p/b.jpg");

        await fixture.ViewModel.ExportCsvAsync().ConfigureAwait(true);

        // 確定した写真は確定値を優先し、未確定の写真だけが第 1 候補を取る
        var roads = fixture.Service.CsvRecords.ToDictionary(record => record.FilePath, record => record.Road);
        Assert.Equal(new RoadNameInfo(RoadNameStatus.Confirmed, "丸山通", null), roads["/p/a.jpg"]);
        Assert.Equal(new RoadNameInfo(RoadNameStatus.Candidate, "山の手通り", "203"), roads["/p/b.jpg"]);
        Assert.Equal(1, fixture.RoadSuggester.CallCount);
    }

    [Fact]
    public async Task ExportCsvAsyncTellsTheUserWhenSomeRoadNamesCouldNotBeObtained()
    {
        using var fixture = new Fixture { CsvPath = "/out/list.csv" };
        fixture.SuggestRoadNames = true;
        fixture.RoadSuggester.Failure = new HttpRequestException("offline");
        fixture.Select("/p/a.jpg", "/p/b.jpg");

        await fixture.ViewModel.ExportCsvAsync().ConfigureAwait(true);

        // 書き出しは続け、道路名は空にする。完了の通知に、そのことを添えて、警告にする
        Assert.All(fixture.Service.CsvRecords, record => Assert.Null(record.Road));
        var (message, severity) = Assert.Single(fixture.Ui.Notifications);
        Assert.Contains("GeoExport.Message.CsvDone", message, StringComparison.Ordinal);
        Assert.Contains("GeoExport.Message.RoadNameIncomplete", message, StringComparison.Ordinal);
        Assert.Equal(InfoBarSeverity.Warning, severity);
    }

    [Fact]
    public async Task ExportRenamedAsyncUsesAConfirmedRoadNameInTheFileName()
    {
        using var fixture = new Fixture { Folder = "/dest" };
        fixture.RoadStore.Items["/p/a.jpg"] = new ConfirmedRoadName("丸山通", null);
        fixture.Select("/p/a.jpg", "/p/b.jpg");

        await fixture.ViewModel.ExportRenamedAsync().ConfigureAwait(true);

        // 候補は、ファイル名には入れない（確定した値と見分けられないため）
        var names = fixture.Service.CopyPlan.ToDictionary(item => item.SourcePath, item => item.RelativePath);
        Assert.Equal("20240102_東品治町_丸山通_001.jpg", names["/p/a.jpg"]);
        Assert.Equal("20240102_東品治町_GeoExport.NoRoad_001.jpg", names["/p/b.jpg"]);
    }

    [Fact]
    public async Task CopyByAreaAsyncCopiesToTownFoldersOfTheChosenDestination()
    {
        using var fixture = new Fixture { Folder = "/dest" };
        fixture.Select("/p/a.jpg");

        await fixture.ViewModel.CopyByAreaAsync().ConfigureAwait(true);

        Assert.Equal("/dest", fixture.Service.CopyDestination);
        var item = Assert.Single(fixture.Service.CopyPlan);
        Assert.Equal("/p/a.jpg", item.SourcePath);
        Assert.Equal(Path.Combine("鳥取市", "東品治町", "a.jpg"), item.RelativePath);
        Assert.Equal(("GeoExport.Message.CopyDone", InfoBarSeverity.Success), Assert.Single(fixture.Ui.Notifications));
    }

    [Fact]
    public async Task ExportRenamedAsyncCopiesWithDateAndAreaNames()
    {
        using var fixture = new Fixture { Folder = "/dest" };
        fixture.Select("/p/IMG_0001.JPG");

        await fixture.ViewModel.ExportRenamedAsync().ConfigureAwait(true);

        Assert.Equal("20240102_東品治町_GeoExport.NoRoad_001.JPG", Assert.Single(fixture.Service.CopyPlan).RelativePath);
    }

    [Fact]
    public async Task CopyAsyncHandsTheConflictChoiceToTheUi()
    {
        using var fixture = new Fixture { Folder = "/dest" };
        fixture.Select("/p/a.jpg");
        fixture.Service.AskAboutFileName = "a.jpg";
        fixture.Ui.ConflictChoice = ConflictResolution.Skip;

        await fixture.ViewModel.CopyByAreaAsync().ConfigureAwait(true);

        Assert.Equal(["a.jpg"], fixture.Ui.ConflictQuestions);
        Assert.Equal(ConflictResolution.Skip, fixture.Service.LastChoice);
    }

    [Fact]
    public async Task CopyAsyncDoesNothingWhenTheFolderPickerIsCanceled()
    {
        using var fixture = new Fixture { Folder = null };
        fixture.Select("/p/a.jpg");

        await fixture.ViewModel.CopyByAreaAsync().ConfigureAwait(true);

        Assert.Null(fixture.Service.CopyDestination);
        Assert.Empty(fixture.Ui.Notifications);
    }

    [Theory]
    [InlineData(2, 1, 0, false, "GeoExport.Message.CopyDone", InfoBarSeverity.Success)]
    [InlineData(2, 1, 3, false, "GeoExport.Message.CopyFailed", InfoBarSeverity.Warning)]
    [InlineData(1, 0, 0, true, "GeoExport.Message.CopyCanceled", InfoBarSeverity.Warning)]
    public async Task CopyAsyncSummarizesTheResult(int success, int skipped, int failed, bool canceled, string expectedMessage, InfoBarSeverity expectedSeverity)
    {
        using var fixture = new Fixture { Folder = "/dest" };
        fixture.Select("/p/a.jpg");
        fixture.Service.CopyResult = new ExportCopyResult(
            success,
            skipped,
            Enumerable.Range(0, failed).Select(i => new FileOperationFailure($"/p/{i}.jpg", $"{i}.jpg", FileOperationError.IoError)).ToList(),
            canceled);

        await fixture.ViewModel.CopyByAreaAsync().ConfigureAwait(true);

        Assert.Equal((expectedMessage, expectedSeverity), Assert.Single(fixture.Ui.Notifications));
    }

    [Fact]
    public async Task CommandsAreAvailableOnlyWhilePhotosAreSelected()
    {
        using var fixture = new Fixture();
        Assert.False(fixture.ViewModel.ExportCsvCommand.CanExecute(null));
        Assert.False(fixture.ViewModel.CopyByAreaCommand.CanExecute(null));
        Assert.False(fixture.ViewModel.ExportRenamedCommand.CanExecute(null));
        Assert.False(fixture.ViewModel.CancelCommand.CanExecute(null));

        fixture.Select("/p/a.jpg");
        Assert.True(fixture.ViewModel.ExportCsvCommand.CanExecute(null));
        Assert.True(fixture.ViewModel.CopyByAreaCommand.CanExecute(null));
        Assert.True(fixture.ViewModel.ExportRenamedCommand.CanExecute(null));

        fixture.Select();
        Assert.False(fixture.ViewModel.ExportCsvCommand.CanExecute(null));

        // 何も選んでいなければ、ピッカーも出さない
        await fixture.ViewModel.CopyByAreaAsync().ConfigureAwait(true);
        Assert.Equal(0, fixture.Ui.FolderPickCount);
    }

    [Fact]
    public async Task CommandsAreUnavailableWhileAnExportIsRunningAndCancelIsAvailable()
    {
        using var fixture = new Fixture { Folder = "/dest", HoldReads = true };
        fixture.Select("/p/a.jpg");
        var run = fixture.ViewModel.CopyByAreaAsync();
        await fixture.Reader.WaitForReadsAsync(1).ConfigureAwait(true);

        Assert.True(fixture.ViewModel.IsBusy);
        Assert.Equal(Visibility.Visible, fixture.ViewModel.ProgressVisibility);
        Assert.False(fixture.ViewModel.CopyByAreaCommand.CanExecute(null));
        Assert.False(fixture.ViewModel.ExportCsvCommand.CanExecute(null));
        Assert.True(fixture.ViewModel.CancelCommand.CanExecute(null));
        Assert.Equal("GeoExport.Progress.Loading", fixture.ViewModel.ProgressText);

        fixture.Reader.Release();
        await run.ConfigureAwait(true);
        Assert.False(fixture.ViewModel.IsBusy);
        Assert.Equal(Visibility.Collapsed, fixture.ViewModel.ProgressVisibility);
        Assert.True(fixture.ViewModel.CopyByAreaCommand.CanExecute(null));
    }

    [Fact]
    public async Task CancelingWhileTheLocationsAreBeingReadStopsBeforeCopying()
    {
        using var fixture = new Fixture { Folder = "/dest", HoldReads = true };
        fixture.Select("/p/a.jpg", "/p/b.jpg");
        var run = fixture.ViewModel.CopyByAreaAsync();
        await fixture.Reader.WaitForReadsAsync(1).ConfigureAwait(true);

        fixture.ViewModel.CancelCommand.Execute(null);
        await run.ConfigureAwait(true);

        Assert.Equal(("GeoExport.Message.Canceled", InfoBarSeverity.Warning), Assert.Single(fixture.Ui.Notifications));
        Assert.Null(fixture.Service.CopyDestination);
        Assert.False(fixture.ViewModel.IsBusy);
    }

    [Fact]
    public async Task AFailureToReadTheAreaDataIsReportedToTheUser()
    {
        using var fixture = new Fixture { Folder = "/dest" };
        fixture.Select("/p/a.jpg");
        fixture.Resolver.Failure = new IOException("broken area data");

        await fixture.ViewModel.CopyByAreaAsync().ConfigureAwait(true);

        Assert.Equal(("GeoExport.Message.AreaDataFailed", InfoBarSeverity.Error), Assert.Single(fixture.Ui.Notifications));
        Assert.Null(fixture.Service.CopyDestination);
        Assert.False(fixture.ViewModel.IsBusy);
    }

    [Fact]
    public async Task TheProgressTextFollowsTheCopyAndIsClearedAtTheEnd()
    {
        using var fixture = new Fixture { Folder = "/dest" };
        fixture.Select("/p/a.jpg");
        var seen = new List<string>();
        fixture.ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GeoExportViewModel.ProgressText))
            {
                seen.Add(fixture.ViewModel.ProgressText);
            }
        };

        await fixture.ViewModel.CopyByAreaAsync().ConfigureAwait(true);

        Assert.Contains("GeoExport.Progress.Copying", seen);
        Assert.Equal(string.Empty, fixture.ViewModel.ProgressText);
    }

    [Fact]
    public void ConstructorRejectsNullArguments()
    {
        var workspace = new WorkspaceState();
        var loader = new PhotoGeoRecordLoader((_, _) => Task.FromResult<PhotoMetadata?>(null), new FakeResolver());
        var service = new FakeService();
        var ui = new FakeUi();
        Assert.Throws<ArgumentNullException>(() => new GeoExportViewModel(null!, loader, service, ui));
        Assert.Throws<ArgumentNullException>(() => new GeoExportViewModel(workspace, null!, service, ui));
        Assert.Throws<ArgumentNullException>(() => new GeoExportViewModel(workspace, loader, null!, ui));
        Assert.Throws<ArgumentNullException>(() => new GeoExportViewModel(workspace, loader, service, null!));
    }

    [Fact]
    public void DisposeStopsListeningToTheSelection()
    {
        using var fixture = new Fixture();
        var changed = 0;
        fixture.ViewModel.ExportCsvCommand.CanExecuteChanged += (_, _) => changed++;
        fixture.Select("/p/a.jpg");
        Assert.True(changed > 0);

        fixture.ViewModel.Dispose();
        changed = 0;
        fixture.Select("/p/a.jpg", "/p/b.jpg");

        Assert.Equal(0, changed);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly Lazy<GeoExportViewModel> _viewModel;

        public Fixture()
        {
            Resolver = new FakeResolver { Result = Higashi };
            _viewModel = new Lazy<GeoExportViewModel>(() => new GeoExportViewModel(
                Workspace,
                new PhotoGeoRecordLoader(Reader.ReadAsync, Resolver),
                Service,
                Ui,
                new PhotoRoadNameResolver(RoadStore, RoadSuggester, _ => 1000L),
                () => SuggestRoadNames));
        }

        public FakeRoadStore RoadStore { get; } = new();

        public FakeRoadSuggester RoadSuggester { get; } = new();

        // 設定の道路名表示（オンのときだけ、未確定の写真の道路名の候補を通信して求める）
        public bool SuggestRoadNames { get; set; }

        public WorkspaceState Workspace { get; } = new();

        public FakeReader Reader { get; } = new();

        public FakeResolver Resolver { get; }

        public FakeService Service { get; } = new();

        public FakeUi Ui { get; } = new();

        public GeoExportViewModel ViewModel => _viewModel.Value;

        public string? CsvPath
        {
            init => Ui.CsvPath = value;
        }

        public string? Folder
        {
            init => Ui.Folder = value;
        }

        public bool HoldReads
        {
            init => Reader.Hold = value;
        }

        public void Select(params string[] paths)
        {
            _ = ViewModel;
            var items = paths
                .Select(path => new PhotoListItem(new PhotoItem(path, 1000, DateTimeOffset.UnixEpoch, isFolder: false), thumbnail: null))
                .ToList();
            Workspace.SelectedPhotos = items;
            Workspace.SelectedPhotoCount = items.Count;
        }

        public void Dispose()
        {
            if (_viewModel.IsValueCreated)
            {
                _viewModel.Value.Dispose();
            }
        }
    }

    private sealed class FakeRoadStore : IRoadNameConfirmationStore
    {
        public Dictionary<string, ConfirmedRoadName> Items { get; } = new();

        public Task<ConfirmedRoadName?> FindAsync(PhotoIdentity photo, CancellationToken cancellationToken)
            => Task.FromResult(Items.TryGetValue(photo.FilePath, out var road) && photo.FileSize == 1000 && photo.TakenAt == TakenAt ? road : null);

        public Task SaveAsync(PhotoIdentity photo, ConfirmedRoadName road, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RemoveAsync(string filePath, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeRoadSuggester : IRoadNameSuggester
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public IReadOnlyList<RoadNameCandidate> Result { get; set; } = new[] { new RoadNameCandidate("山の手通り", "203", 12) };

        public Exception? Failure { get; set; }

        public Task<IReadOnlyList<RoadNameCandidate>> SuggestAsync(double latitude, double longitude, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            return Failure is not null ? Task.FromException<IReadOnlyList<RoadNameCandidate>>(Failure) : Task.FromResult(Result);
        }
    }

    private sealed class FakeReader
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _readCount;

        public bool Hold { get; set; }

        public int ReadCount => Volatile.Read(ref _readCount);

        public void Release() => _release.TrySetResult();

        public async Task WaitForReadsAsync(int count)
        {
            var deadline = Environment.TickCount64 + 5000;
            while (ReadCount < count)
            {
                Assert.True(Environment.TickCount64 < deadline, $"{count} 件の読み取りが始まりませんでした。");
                await Task.Delay(10).ConfigureAwait(false);
            }
        }

        public async Task<PhotoMetadata?> ReadAsync(string path, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _readCount);
            if (Hold)
            {
                await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            return new PhotoMetadata(TakenAt, null, null, 35.49, 134.22, hasGpsData: true);
        }
    }

    private sealed class FakeResolver : IAreaNameResolver
    {
        public event EventHandler? ResultsInvalidated
        {
            add { }
            remove { }
        }

        public AreaName? Result { get; init; }

        public IOException? Failure { get; set; }

        public Task<AreaName?> ResolveAsync(double latitude, double longitude, CancellationToken cancellationToken)
            => Failure is not null ? throw Failure : Task.FromResult(Result);
    }

    private sealed class FakeUi : IGeoExportUi
    {
        public string? CsvPath { get; set; }

        public string? Folder { get; set; }

        public ConflictResolution ConflictChoice { get; set; } = ConflictResolution.Overwrite;

        public int FolderPickCount { get; private set; }

        public List<(string Message, InfoBarSeverity Severity)> Notifications { get; } = [];

        public List<string> ConflictQuestions { get; } = [];

        public Task<string?> PickCsvFileAsync(string suggestedFileName) => Task.FromResult(CsvPath);

        public Task<string?> PickDestinationFolderAsync()
        {
            FolderPickCount++;
            return Task.FromResult(Folder);
        }

        public Task<ConflictResolution> ResolveConflictAsync(string fileName)
        {
            ConflictQuestions.Add(fileName);
            return Task.FromResult(ConflictChoice);
        }

        public void Notify(string message, InfoBarSeverity severity) => Notifications.Add((message, severity));
    }

    private sealed class FakeService : IGeoExportService
    {
        public string? CsvPath { get; private set; }

        public IReadOnlyList<PhotoGeoRecord> CsvRecords { get; private set; } = [];

        public IOException? CsvFailure { get; set; }

        public string? CopyDestination { get; private set; }

        public IReadOnlyList<ExportPlanItem> CopyPlan { get; private set; } = [];

        public ExportCopyResult CopyResult { get; set; } = new(1, 0, [], false);

        public string? AskAboutFileName { get; set; }

        public ConflictResolution? LastChoice { get; private set; }

        public async Task<ExportCopyResult> CopyAsync(
            IReadOnlyList<ExportPlanItem> plan,
            string destinationRoot,
            Func<string, Task<ConflictResolution>>? resolveConflictAsync,
            IProgress<int>? progress,
            CancellationToken cancellationToken)
        {
            CopyPlan = plan;
            CopyDestination = destinationRoot;
            if (AskAboutFileName is not null && resolveConflictAsync is not null)
            {
                LastChoice = await resolveConflictAsync(AskAboutFileName).ConfigureAwait(true);
            }

            progress?.Report(plan.Count);
            return CopyResult;
        }

        public Task WriteCsvAsync(string filePath, IReadOnlyList<PhotoGeoRecord> records, GeoCsvLabels labels, CancellationToken cancellationToken)
        {
            if (CsvFailure is not null)
            {
                return Task.FromException(CsvFailure);
            }

            CsvPath = filePath;
            CsvRecords = records;
            return Task.CompletedTask;
        }
    }
}
