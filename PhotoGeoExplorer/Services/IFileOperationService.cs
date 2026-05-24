using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Services;

internal interface IFileOperationService
{
    // パス検証・変換
    bool ContainsInvalidFileNameChars(string name);
    string NormalizeName(PhotoListItem item, string newName);
    bool IsDescendantPath(string root, string candidate);
    bool IsSamePath(string left, string right);
    string? GetParentPath(string path);
    bool ItemExistsAtPath(string path);
    bool FolderExistsAtPath(string path);
    bool IsJpegFile(string filePath);

    // ファイル操作（単発）
    FileOperationResult CreateFolder(string parentFolder, string folderName);
    FileOperationResult RenameItem(PhotoListItem item, string normalizedName);

    // ファイル操作（複数件）
    FileOperationSummary MoveItems(IReadOnlyList<PhotoListItem> items, string destinationFolder);
    Task<FileOperationSummary> MoveItemsAsync(
        IReadOnlyList<PhotoListItem> items,
        string destinationFolder,
        Func<string, bool, Task<ConflictResolution>> resolveConflictAsync,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);
    FileOperationSummary CopyItems(IReadOnlyList<PhotoListItem> items, string destinationFolder);
    Task<FileOperationSummary> DeleteItemsAsync(IReadOnlyList<PhotoListItem> items);
}
