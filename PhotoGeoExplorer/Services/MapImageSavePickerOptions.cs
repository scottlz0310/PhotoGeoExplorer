using System;
using System.IO;

namespace PhotoGeoExplorer.Services;

internal sealed record MapImageSavePickerOptions
{
    internal const string DefaultFileExtension = ".png";
    internal const string FileTypeLabel = "PNG";
    internal const string SettingsIdentifier = "PhotoGeoExplorer.MapImageExport";

    private MapImageSavePickerOptions(string suggestedStartFolder, string suggestedFileName)
    {
        SuggestedStartFolder = suggestedStartFolder;
        SuggestedFileName = suggestedFileName;
    }

    public string SuggestedStartFolder { get; }

    public string SuggestedFileName { get; }

    public static MapImageSavePickerOptions Create(string? imageSourceFolderPath, string suggestedFileName)
    {
        return Create(
            imageSourceFolderPath,
            suggestedFileName,
            Directory.Exists,
            () => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
    }

    internal static MapImageSavePickerOptions Create(
        string? imageSourceFolderPath,
        string suggestedFileName,
        Func<string, bool> directoryExists,
        Func<string> picturesFolderPathProvider)
    {
        if (string.IsNullOrWhiteSpace(suggestedFileName))
        {
            throw new ArgumentException("Suggested file name is required.", nameof(suggestedFileName));
        }

        ArgumentNullException.ThrowIfNull(directoryExists);
        ArgumentNullException.ThrowIfNull(picturesFolderPathProvider);

        var suggestedStartFolder = !string.IsNullOrWhiteSpace(imageSourceFolderPath)
            && directoryExists(imageSourceFolderPath)
                ? imageSourceFolderPath
                : picturesFolderPathProvider();

        return new MapImageSavePickerOptions(suggestedStartFolder, suggestedFileName);
    }
}
