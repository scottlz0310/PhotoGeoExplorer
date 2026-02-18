using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.ViewModels;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace PhotoGeoExplorer.Tests;

public sealed class ExifEditorServiceTests
{
    [Fact]
    public async Task ValidateExifEditableAsyncMultipleSelectionReturnsInvalid()
    {
        var service = CreateService(
            metadataService: new FakeExifMetadataService(),
            locationPicker: new FakeExifLocationPicker(),
            presenter: new FakeDialogPresenter(),
            notifications: out _);

        var result = await service.ValidateExifEditableAsync(
            new List<PhotoListItem>
            {
                CreatePhotoListItem("a.jpg"),
                CreatePhotoListItem("b.jpg")
            },
            CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.IsValid);
        Assert.Null(result.TargetItem);
        Assert.Equal("Message.ExifEditorMultipleFiles", result.ErrorMessageKey);
    }

    [Fact]
    public async Task ValidateExifEditableAsyncFolderSelectionReturnsInvalid()
    {
        var service = CreateService(
            metadataService: new FakeExifMetadataService(),
            locationPicker: new FakeExifLocationPicker(),
            presenter: new FakeDialogPresenter(),
            notifications: out _);

        var result = await service.ValidateExifEditableAsync(
            new List<PhotoListItem> { CreateFolderListItem("folder") },
            CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.IsValid);
        Assert.Null(result.TargetItem);
        Assert.Equal("Message.ExifEditorFolderSelected", result.ErrorMessageKey);
    }

    [Fact]
    public async Task EditExifAsyncCancelReturnsFalseAndSkipsUpdate()
    {
        var metadataService = new FakeExifMetadataService();
        var presenter = new FakeDialogPresenter();
        presenter.Enqueue(ExifDialogAction.Cancel);
        var service = CreateService(
            metadataService,
            new FakeExifLocationPicker(),
            presenter,
            out _);

        var success = await service.EditExifAsync(CreatePhotoListItem("test.jpg"), CancellationToken.None).ConfigureAwait(true);

        Assert.False(success);
        Assert.Equal(0, metadataService.UpdateCallCount);
    }

    [Fact]
    public async Task EditExifAsyncPickLocationThenCancelReflectsPickedLocation()
    {
        var metadataService = new FakeExifMetadataService();
        var picker = new FakeExifLocationPicker
        {
            CanPickExifLocation = true,
            NextLocation = (35.1234567, 139.9876543)
        };
        var presenter = new FakeDialogPresenter();
        presenter.Enqueue(ExifDialogAction.PickLocation);
        presenter.Enqueue(ExifDialogAction.Cancel);

        var service = CreateService(metadataService, picker, presenter, out var notifications);

        var success = await service.EditExifAsync(CreatePhotoListItem("test.jpg"), CancellationToken.None).ConfigureAwait(true);

        Assert.False(success);
        Assert.Equal(0, metadataService.UpdateCallCount);
        Assert.Equal(2, presenter.ObservedStates.Count);
        Assert.Equal("35.123457", presenter.ObservedStates[1].LatitudeText);
        Assert.Equal("139.987654", presenter.ObservedStates[1].LongitudeText);
        Assert.Collection(
            notifications,
            item =>
            {
                Assert.Equal(LocalizationService.GetString("Message.ExifPickLocationInstruction"), item.Message);
                Assert.Equal(InfoBarSeverity.Informational, item.Severity);
            },
            item =>
            {
                Assert.Equal(string.Empty, item.Message);
                Assert.Equal(InfoBarSeverity.Informational, item.Severity);
            });
    }

    [Fact]
    public async Task EditExifAsyncSaveCallsUpdateAndShowsSuccessNotification()
    {
        var metadataService = new FakeExifMetadataService
        {
            UpdateResult = true
        };
        var presenter = new FakeDialogPresenter();
        presenter.Enqueue(ExifDialogAction.Save, state =>
        {
            state.UpdateDate = true;
            state.TakenAtDate = new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero);
            state.TakenAtTime = new TimeSpan(12, 34, 56);
            state.LatitudeText = "35.1234";
            state.LongitudeText = "139.5678";
            state.UpdateFileDate = true;
        });

        var service = CreateService(
            metadataService,
            new FakeExifLocationPicker(),
            presenter,
            out var notifications);

        var success = await service.EditExifAsync(CreatePhotoListItem("test.jpg"), CancellationToken.None).ConfigureAwait(true);

        Assert.True(success);
        Assert.Equal(1, metadataService.UpdateCallCount);
        Assert.NotNull(metadataService.LastTakenAt);
        Assert.Equal(2024, metadataService.LastTakenAt!.Value.Year);
        Assert.Equal(1, metadataService.LastTakenAt.Value.Month);
        Assert.Equal(2, metadataService.LastTakenAt.Value.Day);
        Assert.Equal(12, metadataService.LastTakenAt.Value.Hour);
        Assert.Equal(34, metadataService.LastTakenAt.Value.Minute);
        Assert.Equal(56, metadataService.LastTakenAt.Value.Second);
        Assert.Equal(35.1234, metadataService.LastLatitude);
        Assert.Equal(139.5678, metadataService.LastLongitude);
        Assert.True(metadataService.LastUpdateFileModifiedDate);
        Assert.Single(notifications);
        Assert.Equal(LocalizationService.GetString("Message.ExifUpdateSuccess"), notifications[0].Message);
        Assert.Equal(InfoBarSeverity.Success, notifications[0].Severity);
    }

    private static ExifEditorService CreateService(
        FakeExifMetadataService metadataService,
        FakeExifLocationPicker locationPicker,
        FakeDialogPresenter presenter,
        out List<(string Message, InfoBarSeverity Severity)> notifications)
    {
        var notificationBuffer = new List<(string Message, InfoBarSeverity Severity)>();
        notifications = notificationBuffer;
        return new ExifEditorService(
            new FakeDialogService(),
            metadataService,
            locationPicker,
            (message, severity) => notificationBuffer.Add((message, severity)),
            presenter);
    }

    private static PhotoListItem CreatePhotoListItem(string fileName)
    {
        var photoItem = new PhotoItem(
            filePath: $"/test/{fileName}",
            sizeBytes: 1000,
            modifiedAt: DateTimeOffset.UtcNow,
            isFolder: false,
            thumbnailPath: null,
            pixelWidth: 100,
            pixelHeight: 100);

        return new PhotoListItem(photoItem, thumbnail: null, toolTipText: null, thumbnailKey: null);
    }

    private static PhotoListItem CreateFolderListItem(string folderName)
    {
        var photoItem = new PhotoItem(
            filePath: $"/test/{folderName}",
            sizeBytes: 0,
            modifiedAt: DateTimeOffset.UtcNow,
            isFolder: true,
            thumbnailPath: null,
            pixelWidth: null,
            pixelHeight: null);

        return new PhotoListItem(photoItem, thumbnail: null, toolTipText: null, thumbnailKey: null);
    }

    private sealed class FakeDialogService : IDialogService
    {
        public Task<ContentDialogResult?> ShowContentDialogAsync(ContentDialog dialog, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ContentDialogResult?>(ContentDialogResult.None);
        }

        public Task ShowMessageDialogAsync(string title, string message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<StorageFile?> ShowFilePickerAsync(
            PickerLocationId startLocation,
            IReadOnlyList<string>? fileTypeFilter = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<StorageFile?>(null);
        }
    }

    private sealed class FakeExifMetadataService : IExifMetadataService
    {
        public PhotoMetadata? MetadataToReturn { get; set; }
        public bool UpdateResult { get; set; } = true;
        public int UpdateCallCount { get; private set; }
        public DateTimeOffset? LastTakenAt { get; private set; }
        public double? LastLatitude { get; private set; }
        public double? LastLongitude { get; private set; }
        public bool LastUpdateFileModifiedDate { get; private set; }

        public Task<PhotoMetadata?> GetMetadataAsync(string filePath, CancellationToken cancellationToken)
        {
            return Task.FromResult(MetadataToReturn);
        }

        public Task<bool> UpdateMetadataAsync(
            string filePath,
            DateTimeOffset? takenAt,
            double? latitude,
            double? longitude,
            bool updateFileModifiedDate,
            CancellationToken cancellationToken)
        {
            UpdateCallCount++;
            LastTakenAt = takenAt;
            LastLatitude = latitude;
            LastLongitude = longitude;
            LastUpdateFileModifiedDate = updateFileModifiedDate;
            return Task.FromResult(UpdateResult);
        }
    }

    private sealed class FakeExifLocationPicker : IExifLocationPicker
    {
        public bool CanPickExifLocation { get; set; } = true;
        public (double Latitude, double Longitude)? NextLocation { get; set; }

        public Task<(double Latitude, double Longitude)?> PickExifLocationAsync()
        {
            return Task.FromResult(NextLocation);
        }
    }

    private sealed class FakeDialogPresenter : IExifEditDialogPresenter
    {
        private readonly Queue<(ExifDialogAction Action, Action<ExifEditState>? Mutator)> _steps = new();
        public List<ExifEditState> ObservedStates { get; } = new();

        public void Enqueue(ExifDialogAction action, Action<ExifEditState>? mutator = null)
        {
            _steps.Enqueue((action, mutator));
        }

        public Task<ExifEditDialogResult> ShowAsync(ExifEditState state, CancellationToken cancellationToken)
        {
            ObservedStates.Add(Clone(state));

            if (_steps.Count == 0)
            {
                return Task.FromResult(new ExifEditDialogResult(ExifDialogAction.Cancel, state));
            }

            var (action, mutator) = _steps.Dequeue();
            mutator?.Invoke(state);
            return Task.FromResult(new ExifEditDialogResult(action, state));
        }

        private static ExifEditState Clone(ExifEditState source)
        {
            return new ExifEditState
            {
                UpdateDate = source.UpdateDate,
                TakenAtDate = source.TakenAtDate,
                TakenAtTime = source.TakenAtTime,
                LatitudeText = source.LatitudeText,
                LongitudeText = source.LongitudeText,
                UpdateFileDate = source.UpdateFileDate
            };
        }
    }
}
