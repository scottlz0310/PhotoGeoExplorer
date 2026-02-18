using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Services;

internal interface IExifEditorService
{
    Task<ExifEditValidationResult> ValidateExifEditableAsync(
        IReadOnlyList<PhotoListItem> selectedItems,
        CancellationToken cancellationToken = default);

    Task<bool> EditExifAsync(PhotoListItem item, CancellationToken cancellationToken = default);
}

internal sealed class ExifEditValidationResult
{
    private ExifEditValidationResult(bool isValid, PhotoListItem? targetItem, string? errorMessageKey)
    {
        IsValid = isValid;
        TargetItem = targetItem;
        ErrorMessageKey = errorMessageKey;
    }

    public bool IsValid { get; }

    public PhotoListItem? TargetItem { get; }

    public string? ErrorMessageKey { get; }

    public static ExifEditValidationResult Valid(PhotoListItem item)
    {
        return new ExifEditValidationResult(isValid: true, targetItem: item, errorMessageKey: null);
    }

    public static ExifEditValidationResult Invalid(string errorMessageKey)
    {
        return new ExifEditValidationResult(isValid: false, targetItem: null, errorMessageKey: errorMessageKey);
    }
}
