using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoGeoExplorer.Panes.Map;
using PhotoGeoExplorer.Services.AreaNames;
using Xunit;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// 地域データが無い地域の写真を選んだときの、確認 → 取得 → 通知の流れ。
/// 同意の記録・拒否や失敗の記録（同じ都道府県を自動では再び尋ねない）・1 回に 1 つだけの取得・中止を確かめる
/// </summary>
public sealed class AreaDataAcquisitionViewModelTests
{
    private static readonly string[] Tottori = ["31"];
    private static readonly string[] TokyoAndKanagawa = ["13", "14"];

    // 内部の列挙型を public な型の公開メンバーに出せないため、名前で渡す
    public static TheoryData<string> DownloadErrors => new() { nameof(AreaDataDownloadError.Network), nameof(AreaDataDownloadError.HttpStatus), nameof(AreaDataDownloadError.NotArchive), nameof(AreaDataDownloadError.TooLarge) };

    [Fact]
    public async Task OnLocationAsyncDoesNothingWhenNoAreaDataIsNeeded()
    {
        var fixture = new Fixture(missing: []);

        await fixture.ViewModel.OnLocationAsync(35.0, 133.0).ConfigureAwait(true);

        Assert.Empty(fixture.Prompts);
        Assert.Empty(fixture.Downloader.Requested);
        Assert.Empty(fixture.Notifications);
    }

    [Fact]
    public async Task OnLocationAsyncDownloadsAfterTheUserAccepts()
    {
        var fixture = new Fixture(Tottori) { Consent = AreaDataConsent.Accept };

        await fixture.ViewModel.OnLocationAsync(35.0, 133.0).ConfigureAwait(true);

        Assert.Equal([["鳥取県"]], fixture.Prompts);
        Assert.Equal(Tottori, fixture.Downloader.Requested);
        Assert.Equal([InfoBarSeverity.Success], fixture.Notifications.Select(notification => notification.Severity));
        Assert.False(fixture.ViewModel.AutoDownload);
        Assert.False(fixture.ViewModel.IsDownloading);
    }

    [Fact]
    public async Task AcceptAndRememberTurnsOnAutoDownloadAndLaterRequestsSkipTheDialog()
    {
        var fixture = new Fixture(Tottori) { Consent = AreaDataConsent.AcceptAndRemember };

        await fixture.ViewModel.OnLocationAsync(35.0, 133.0).ConfigureAwait(true);
        fixture.Detector.Missing = ["13"];
        await fixture.ViewModel.OnLocationAsync(35.7, 139.7).ConfigureAwait(true);

        Assert.True(fixture.ViewModel.AutoDownload);
        Assert.Single(fixture.Prompts);
        Assert.Equal(["31", "13"], fixture.Downloader.Requested);
    }

    [Fact]
    public async Task OnLocationAsyncSkipsTheDialogWhenAutoDownloadIsAlreadyOn()
    {
        var fixture = new Fixture(Tottori);
        fixture.ViewModel.AutoDownload = true;

        await fixture.ViewModel.OnLocationAsync(35.0, 133.0).ConfigureAwait(true);

        Assert.Empty(fixture.Prompts);
        Assert.Equal(Tottori, fixture.Downloader.Requested);
    }

    [Fact]
    public async Task DecliningDoesNotDownloadAndDoesNotAskAgainForTheSamePrefectureDuringTheSession()
    {
        var fixture = new Fixture(Tottori) { Consent = AreaDataConsent.Decline };

        await fixture.ViewModel.OnLocationAsync(35.0, 133.0).ConfigureAwait(true);
        await fixture.ViewModel.OnLocationAsync(35.1, 133.1).ConfigureAwait(true);
        fixture.Detector.Missing = ["13"];
        await fixture.ViewModel.OnLocationAsync(35.7, 139.7).ConfigureAwait(true);

        Assert.Equal([["鳥取県"], ["東京都"]], fixture.Prompts);
        Assert.Empty(fixture.Downloader.Requested);
        Assert.False(fixture.ViewModel.AutoDownload);
    }

    [Theory]
    [MemberData(nameof(DownloadErrors))]
    public async Task ADownloadFailureIsReportedAndTheSamePrefectureIsNotRetriedAutomatically(string errorName)
    {
        var error = Enum.Parse<AreaDataDownloadError>(errorName);
        var fixture = new Fixture(Tottori) { Consent = AreaDataConsent.Accept };
        fixture.Downloader.Failure = new AreaDataDownloadException(error, "failed");

        await fixture.ViewModel.OnLocationAsync(35.0, 133.0).ConfigureAwait(true);
        await fixture.ViewModel.OnLocationAsync(35.0, 133.0).ConfigureAwait(true);

        Assert.Equal([InfoBarSeverity.Error], fixture.Notifications.Select(notification => notification.Severity));
        Assert.Single(fixture.Prompts);
        Assert.Single(fixture.Downloader.Requested);
        Assert.False(fixture.ViewModel.IsDownloading);
    }

    [Fact]
    public async Task AnImportFailureAndAnIoFailureAreReportedAsErrors()
    {
        var fixture = new Fixture(Tottori) { Consent = AreaDataConsent.Accept };
        fixture.Downloader.Failure = new AreaDataFormatException(AreaDataFormatError.InvalidArchive, "bad archive");
        await fixture.ViewModel.OnLocationAsync(35.0, 133.0).ConfigureAwait(true);

        fixture.Detector.Missing = ["13"];
        fixture.Downloader.Failure = new IOException("disk full");
        await fixture.ViewModel.OnLocationAsync(35.7, 139.7).ConfigureAwait(true);

        Assert.Equal([InfoBarSeverity.Error, InfoBarSeverity.Error], fixture.Notifications.Select(notification => notification.Severity));
    }

    [Fact]
    public async Task ACancellationIsReportedAsInformationAndTheSamePrefectureIsNotRetried()
    {
        var fixture = new Fixture(Tottori) { Consent = AreaDataConsent.Accept };
        fixture.Downloader.Failure = new OperationCanceledException();

        await fixture.ViewModel.OnLocationAsync(35.0, 133.0).ConfigureAwait(true);
        await fixture.ViewModel.OnLocationAsync(35.0, 133.0).ConfigureAwait(true);

        Assert.Equal([InfoBarSeverity.Informational], fixture.Notifications.Select(notification => notification.Severity));
        Assert.Single(fixture.Downloader.Requested);
    }

    [Fact]
    public async Task WhenSeveralCandidatesAreMissingAFailureStopsTheRemainingOnes()
    {
        var fixture = new Fixture(TokyoAndKanagawa) { Consent = AreaDataConsent.Accept };
        fixture.Downloader.Failure = new AreaDataDownloadException(AreaDataDownloadError.Network, "failed");

        await fixture.ViewModel.OnLocationAsync(35.5, 139.5).ConfigureAwait(true);
        await fixture.ViewModel.OnLocationAsync(35.5, 139.5).ConfigureAwait(true);

        Assert.Equal([["東京都", "神奈川県"]], fixture.Prompts);
        Assert.Equal(["13"], fixture.Downloader.Requested);
    }

    [Fact]
    public async Task WhenSeveralCandidatesAreMissingTheyAreDownloadedInOrder()
    {
        var fixture = new Fixture(TokyoAndKanagawa) { Consent = AreaDataConsent.Accept };

        await fixture.ViewModel.OnLocationAsync(35.5, 139.5).ConfigureAwait(true);

        Assert.Equal(TokyoAndKanagawa, fixture.Downloader.Requested);
        Assert.Equal(2, fixture.Notifications.Count);
    }

    [Fact]
    public async Task ALocationChecksFailureDoesNotPropagateOrAsk()
    {
        var fixture = new Fixture(Tottori) { Consent = AreaDataConsent.Accept };
        fixture.Detector.Failure = new IOException("cannot list");

        await fixture.ViewModel.OnLocationAsync(35.0, 133.0).ConfigureAwait(true);

        Assert.Empty(fixture.Prompts);
        Assert.Empty(fixture.Downloader.Requested);
    }

    [Fact]
    public async Task ASecondLocationWhileBusyIsIgnored()
    {
        var gate = new TaskCompletionSource();
        var fixture = new Fixture(Tottori) { Consent = AreaDataConsent.Accept };
        fixture.Downloader.Gate = gate.Task;

        var first = fixture.ViewModel.OnLocationAsync(35.0, 133.0);
        await fixture.Downloader.Started.Task.ConfigureAwait(true);
        Assert.True(fixture.ViewModel.IsDownloading);
        Assert.Equal(Visibility.Visible, fixture.ViewModel.StatusVisibility);
        await fixture.ViewModel.OnLocationAsync(35.1, 133.1).ConfigureAwait(true);
        gate.SetResult();
        await first.ConfigureAwait(true);

        Assert.Equal(1, fixture.Detector.CallCount);
        Assert.Single(fixture.Downloader.Requested);
        Assert.False(fixture.ViewModel.IsDownloading);
        Assert.Equal(Visibility.Collapsed, fixture.ViewModel.StatusVisibility);
    }

    [Fact]
    public async Task TheCancelCommandStopsTheRunningDownload()
    {
        var fixture = new Fixture(Tottori) { Consent = AreaDataConsent.Accept };
        fixture.Downloader.WaitForCancellation = true;

        var run = fixture.ViewModel.OnLocationAsync(35.0, 133.0);
        await fixture.Downloader.Started.Task.ConfigureAwait(true);
        fixture.ViewModel.CancelCommand.Execute(null);
        await run.ConfigureAwait(true);

        Assert.Equal([InfoBarSeverity.Informational], fixture.Notifications.Select(notification => notification.Severity));
        Assert.False(fixture.ViewModel.IsDownloading);
    }

    [Fact]
    public async Task DownloadAsyncDownloadsWithoutAskingEvenForAPrefectureTheUserDeclined()
    {
        var fixture = new Fixture(Tottori) { Consent = AreaDataConsent.Decline };
        await fixture.ViewModel.OnLocationAsync(35.0, 133.0).ConfigureAwait(true);

        await fixture.ViewModel.DownloadAsync("31").ConfigureAwait(true);

        Assert.Single(fixture.Prompts);
        Assert.Equal(Tottori, fixture.Downloader.Requested);
        Assert.Equal([InfoBarSeverity.Success], fixture.Notifications.Select(notification => notification.Severity));
    }

    [Fact]
    public async Task TheProgressTextIsShownWhileDownloadingAndClearedAfterwards()
    {
        var gate = new TaskCompletionSource();
        var fixture = new Fixture(Tottori) { Consent = AreaDataConsent.Accept };
        fixture.Downloader.Gate = gate.Task;
        fixture.Downloader.ProgressToReport = 3 * 1024 * 1024;

        var run = fixture.ViewModel.OnLocationAsync(35.0, 133.0);
        await fixture.Downloader.Started.Task.ConfigureAwait(true);
        Assert.NotEmpty(fixture.ViewModel.StatusText);
        gate.SetResult();
        await run.ConfigureAwait(true);

        Assert.Empty(fixture.ViewModel.StatusText);
    }

    [Fact]
    public async Task ADefaultViewModelWithoutDependenciesDoesNothing()
    {
        var viewModel = new AreaDataAcquisitionViewModel();

        await viewModel.OnLocationAsync(35.0, 133.0).ConfigureAwait(true);
        await viewModel.DownloadAsync("31").ConfigureAwait(true);

        Assert.False(viewModel.IsDownloading);
    }

    [Theory]
    [InlineData(0L, "0 KB")]
    [InlineData(1536L, "2 KB")]
    [InlineData(1048575L, "1024 KB")]
    [InlineData(1048576L, "1.0 MB")]
    [InlineData(5L * 1024 * 1024 + 524288, "5.5 MB")]
    public void FormatBytesShowsKilobytesBelowOneMegabyteAndMegabytesAbove(long bytes, string expected)
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        try
        {
            Assert.Equal(expected, AreaDataAcquisitionViewModel.FormatBytes(bytes));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("Network", "AreaData.Download.Error.Network")]
    [InlineData("HttpStatus", "AreaData.Download.Error.HttpStatus")]
    [InlineData("NotArchive", "AreaData.Download.Error.NotArchive")]
    [InlineData("TooLarge", "AreaData.Download.Error.TooLarge")]
    public void EachDownloadErrorHasItsOwnMessageKey(string errorName, string expectedKey)
    {
        Assert.Equal(expectedKey, AreaDataAcquisitionViewModel.GetDownloadErrorKey(Enum.Parse<AreaDataDownloadError>(errorName)));
    }

    private sealed class Fixture
    {
        public Fixture(IReadOnlyList<string> missing)
        {
            Detector = new FakeDetector { Missing = missing };
            Downloader = new FakeDownloader();
            ViewModel = new AreaDataAcquisitionViewModel(
                Detector,
                Downloader,
                (names, _) =>
                {
                    Prompts.Add(names.ToList());
                    return Task.FromResult(Consent);
                },
                (message, severity) => Notifications.Add((message, severity)),
                report => new SynchronousProgress(report));
        }

        public FakeDetector Detector { get; }

        public FakeDownloader Downloader { get; }

        public AreaDataAcquisitionViewModel ViewModel { get; }

        public AreaDataConsent Consent { get; set; } = AreaDataConsent.Decline;

        public List<List<string>> Prompts { get; } = [];

        public List<(string Message, InfoBarSeverity Severity)> Notifications { get; } = [];
    }

    private sealed class FakeDetector : IAreaDataNeedDetector
    {
        public IReadOnlyList<string> Missing { get; set; } = [];

        public Exception? Failure { get; set; }

        public int CallCount { get; private set; }

        public Task<IReadOnlyList<string>> FindMissingAsync(double latitude, double longitude, CancellationToken cancellationToken)
        {
            CallCount++;
            return Failure is not null ? Task.FromException<IReadOnlyList<string>>(Failure) : Task.FromResult(Missing);
        }
    }

    private sealed class FakeDownloader : IAreaDataDownloader
    {
        public List<string> Requested { get; } = [];

        public Exception? Failure { get; set; }

        public Task? Gate { get; set; }

        public bool WaitForCancellation { get; set; }

        public long ProgressToReport { get; set; }

        public TaskCompletionSource Started { get; } = new();

        public async Task<AreaDataPackInfo> DownloadAsync(string prefectureCode, IProgress<long>? progress, CancellationToken cancellationToken)
        {
            Requested.Add(prefectureCode);
            if (ProgressToReport > 0)
            {
                progress?.Report(ProgressToReport);
            }

            Started.TrySetResult();
            if (WaitForCancellation)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }

            if (Gate is not null)
            {
                await Gate.ConfigureAwait(false);
            }

            if (Failure is not null)
            {
                throw Failure;
            }

            PrefectureCatalog.TryGetName(prefectureCode, out var name);
            return new AreaDataPackInfo(prefectureCode, name, 2020, 1);
        }
    }

    private sealed class SynchronousProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }
}
