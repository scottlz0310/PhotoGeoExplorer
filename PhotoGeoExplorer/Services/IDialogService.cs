using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace PhotoGeoExplorer.Services;

internal interface IDialogService
{
    Task<ContentDialogResult?> ShowContentDialogAsync(ContentDialog dialog, CancellationToken cancellationToken = default);

    Task ShowMessageDialogAsync(string title, string message, CancellationToken cancellationToken = default);

    Task<StorageFile?> ShowFilePickerAsync(
        PickerLocationId startLocation,
        IReadOnlyList<string>? fileTypeFilter = null,
        CancellationToken cancellationToken = default);
}
