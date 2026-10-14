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

    public static MapImageSavePickerOptions Create(
        string? lastSavedFolderPath,
        string? imageSourceFolderPath,
        string suggestedFileName)
    {
        return Create(
            lastSavedFolderPath,
            imageSourceFolderPath,
            suggestedFileName,
            Directory.Exists,
            () => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
    }

    /// <param name="lastSavedFolderPath">アプリ実行中に前回保存したフォルダー。存在確認済みのものを渡す。</param>
    internal static MapImageSavePickerOptions Create(
        string? lastSavedFolderPath,
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

        var suggestedStartFolder = !string.IsNullOrWhiteSpace(lastSavedFolderPath)
            ? lastSavedFolderPath
            : !string.IsNullOrWhiteSpace(imageSourceFolderPath) && directoryExists(imageSourceFolderPath)
                ? imageSourceFolderPath
                : picturesFolderPathProvider();

        return new MapImageSavePickerOptions(suggestedStartFolder, suggestedFileName);
    }
}
