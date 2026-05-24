using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.ViewModels;
using Windows.Storage;

namespace PhotoGeoExplorer.Services;

internal sealed class FileOperationService : IFileOperationService
{
    public bool ContainsInvalidFileNameChars(string name)
        => name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0;

    public string NormalizeName(PhotoListItem item, string newName)
    {
        var trimmed = newName.Trim();
        if (item.IsFolder)
        {
            return trimmed;
        }

        var originalExtension = Path.GetExtension(item.FileName);
        if (string.IsNullOrWhiteSpace(originalExtension))
        {
            return trimmed;
        }

        var newExtension = Path.GetExtension(trimmed);
        if (string.IsNullOrWhiteSpace(newExtension))
        {
            return $"{trimmed}{originalExtension}";
        }

        return trimmed;
    }

    public bool IsDescendantPath(string root, string candidate)
    {
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return normalizedCandidate.Length > normalizedRoot.Length
            && normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    public bool IsSamePath(string left, string right)
    {
        var normalizedLeft = Path.GetFullPath(left)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRight = Path.GetFullPath(right)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    public string? GetParentPath(string path)
        => Directory.GetParent(path)?.FullName;

    public bool ItemExistsAtPath(string path)
        => Directory.Exists(path) || File.Exists(path);

    public bool FolderExistsAtPath(string path)
        => Directory.Exists(path);

    public bool IsJpegFile(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    public FileOperationResult CreateFolder(string parentFolder, string folderName)
    {
        var targetPath = Path.Combine(parentFolder, folderName);
        if (ItemExistsAtPath(targetPath))
        {
            return FileOperationResult.Failure(FileOperationError.AlreadyExists);
        }

        try
        {
            Directory.CreateDirectory(targetPath);
            return FileOperationResult.Success(targetPath);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException)
        {
            AppLog.Error($"Failed to create folder: {targetPath}", ex);
            return FileOperationResult.Failure(FileOperationError.Unauthorized);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or PathTooLongException)
        {
            AppLog.Error($"Failed to create folder: {targetPath}", ex);
            return FileOperationResult.Failure(FileOperationError.IoError);
        }
    }

    public FileOperationResult RenameItem(PhotoListItem item, string normalizedName)
    {
        var parent = Path.GetDirectoryName(item.FilePath);
        if (string.IsNullOrWhiteSpace(parent))
        {
            return FileOperationResult.Failure(FileOperationError.NoParent);
        }

        var targetPath = Path.Combine(parent, normalizedName);
        if (ItemExistsAtPath(targetPath))
        {
            return FileOperationResult.Failure(FileOperationError.AlreadyExists);
        }

        try
        {
            if (item.IsFolder)
            {
                Directory.Move(item.FilePath, targetPath);
            }
            else
            {
                File.Move(item.FilePath, targetPath);
            }

            return FileOperationResult.Success(targetPath);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException)
        {
            AppLog.Error($"Failed to rename item: {item.FilePath}", ex);
            return FileOperationResult.Failure(FileOperationError.Unauthorized);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or PathTooLongException)
        {
            AppLog.Error($"Failed to rename item: {item.FilePath}", ex);
            return FileOperationResult.Failure(FileOperationError.IoError);
        }
    }

    public FileOperationSummary CopyItems(IReadOnlyList<PhotoListItem> items, string destinationFolder)
    {
        var successCount = 0;
        var failures = new List<FileOperationFailure>();

        foreach (var item in items)
        {
            var sourcePath = item.FilePath;
            var targetPath = Path.Combine(destinationFolder, item.FileName);

            if (item.IsFolder && IsDescendantPath(sourcePath, destinationFolder))
            {
                failures.Add(new FileOperationFailure(sourcePath, item.FileName, FileOperationError.DescendantPath));
                break;
            }

            if (ItemExistsAtPath(targetPath))
            {
                failures.Add(new FileOperationFailure(sourcePath, item.FileName, FileOperationError.AlreadyExists));
                break;
            }

            try
            {
                if (item.IsFolder)
                {
                    CopyDirectory(sourcePath, targetPath);
                }
                else
                {
                    File.Copy(sourcePath, targetPath, overwrite: false);
                }

                successCount++;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException)
            {
                AppLog.Error($"Failed to copy item: {sourcePath}", ex);
                failures.Add(new FileOperationFailure(sourcePath, item.FileName, FileOperationError.Unauthorized));
                break;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or PathTooLongException)
            {
                AppLog.Error($"Failed to copy item: {sourcePath}", ex);
                failures.Add(new FileOperationFailure(sourcePath, item.FileName, FileOperationError.IoError));
                break;
            }
        }

        return new FileOperationSummary(successCount, 0, failures);
    }

    private static void CopyDirectory(string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(targetPath);
        foreach (var file in Directory.GetFiles(sourcePath))
        {
            File.Copy(file, Path.Combine(targetPath, Path.GetFileName(file)), overwrite: false);
        }

        foreach (var dir in Directory.GetDirectories(sourcePath))
        {
            CopyDirectory(dir, Path.Combine(targetPath, Path.GetFileName(dir)));
        }
    }

    public async Task<FileOperationSummary> CopyItemsAsync(
        IReadOnlyList<PhotoListItem> items,
        string destinationFolder,
        Func<string, bool, Task<ConflictResolution>> resolveConflictAsync,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var successCount = 0;
        var skipCount = 0;
        var failures = new List<FileOperationFailure>();
        var overwriteAll = false;
        var skipAll = false;

        for (var i = 0; i < items.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                failures.Add(new FileOperationFailure(items[i].FilePath, items[i].FileName, FileOperationError.Cancelled));
                break;
            }

            var item = items[i];
            var sourcePath = item.FilePath;
            var targetPath = Path.Combine(destinationFolder, item.FileName);

            if (item.IsFolder && IsDescendantPath(sourcePath, destinationFolder))
            {
                failures.Add(new FileOperationFailure(sourcePath, item.FileName, FileOperationError.DescendantPath));
                progress?.Report(i + 1);
                continue;
            }

            // 同一パスへのコピーは上書きするとソースを失うため無条件スキップ
            if (IsSamePath(sourcePath, targetPath))
            {
                skipCount++;
                progress?.Report(i + 1);
                continue;
            }

            if (ItemExistsAtPath(targetPath))
            {
                ConflictResolution resolution;
                if (overwriteAll)
                {
                    resolution = ConflictResolution.Overwrite;
                }
                else if (skipAll)
                {
                    resolution = ConflictResolution.Skip;
                }
                else
                {
                    resolution = await resolveConflictAsync(item.FileName, item.IsFolder).ConfigureAwait(false);
                    if (resolution == ConflictResolution.OverwriteAll)
                    {
                        overwriteAll = true;
                        resolution = ConflictResolution.Overwrite;
                    }
                    else if (resolution == ConflictResolution.SkipAll)
                    {
                        skipAll = true;
                        resolution = ConflictResolution.Skip;
                    }
                }

                if (resolution == ConflictResolution.Cancel)
                {
                    failures.Add(new FileOperationFailure(sourcePath, item.FileName, FileOperationError.Cancelled));
                    break;
                }

                if (resolution == ConflictResolution.Skip)
                {
                    skipCount++;
                    progress?.Report(i + 1);
                    continue;
                }

                // Overwrite: 既存を削除してからコピー
                try
                {
                    if (item.IsFolder)
                    {
                        Directory.Delete(targetPath, recursive: true);
                    }
                    else
                    {
                        File.Delete(targetPath);
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException)
                {
                    AppLog.Error($"Failed to delete existing item for overwrite: {targetPath}", ex);
                    failures.Add(new FileOperationFailure(sourcePath, item.FileName, FileOperationError.Unauthorized));
                    progress?.Report(i + 1);
                    continue;
                }
                catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or PathTooLongException)
                {
                    AppLog.Error($"Failed to delete existing item for overwrite: {targetPath}", ex);
                    failures.Add(new FileOperationFailure(sourcePath, item.FileName, FileOperationError.IoError));
                    progress?.Report(i + 1);
                    continue;
                }
            }

            try
            {
                if (item.IsFolder)
                {
                    CopyDirectory(sourcePath, targetPath);
                }
                else
                {
                    File.Copy(sourcePath, targetPath, overwrite: false);
                }

                successCount++;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException)
            {
                AppLog.Error($"Failed to copy item: {sourcePath}", ex);
                failures.Add(new FileOperationFailure(sourcePath, item.FileName, FileOperationError.Unauthorized));
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or PathTooLongException)
            {
                AppLog.Error($"Failed to copy item: {sourcePath}", ex);
                failures.Add(new FileOperationFailure(sourcePath, item.FileName, FileOperationError.IoError));
            }

            progress?.Report(i + 1);
        }

        AppLog.Info($"CopyItemsAsync: success={successCount}, skip={skipCount}, failure={failures.Count}");
        return new FileOperationSummary(successCount, skipCount, failures);
    }

    public FileOperationSummary MoveItems(IReadOnlyList<PhotoListItem> items, string destinationFolder)
    {
        var successCount = 0;
        var failures = new List<FileOperationFailure>();

        foreach (var item in items)
        {
            var sourcePath = item.FilePath;
            var parent = Path.GetDirectoryName(sourcePath);
            if (string.IsNullOrWhiteSpace(parent))
            {
                failures.Add(new FileOperationFailure(sourcePath, item.FileName, FileOperationError.NoParent));
                break;
            }

            if (IsSamePath(parent, destinationFolder))
            {
                // 同一フォルダへの移動はスキップ（エラーではない）
                continue;
            }

            if (item.IsFolder && IsDescendantPath(sourcePath, destinationFolder))
            {
                failures.Add(new FileOperationFailure(sourcePath, item.FileName, FileOperationError.DescendantPath));
                break;
            }

            var targetPath = Path.Combine(destinationFolder, item.FileName);
            if (ItemExistsAtPath(targetPath))
            {
                failures.Add(new FileOperationFailure(sourcePath, item.FileName, FileOperationError.AlreadyExists));
                break;
            }

            try
            {
                if (item.IsFolder)
                {
                    Directory.Move(sourcePath, targetPath);
                }
                else
                {
                    File.Move(sourcePath, targetPath);
                }

                successCount++;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException)
            {
                AppLog.Error($"Failed to move item: {sourcePath}", ex);
                failures.Add(new FileOperationFailure(sourcePath, item.FileName, FileOperationError.Unauthorized));
                break;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or PathTooLongException)
            {
                AppLog.Error($"Failed to move item: {sourcePath}", ex);
                failures.Add(new FileOperationFailure(sourcePath, item.FileName, FileOperationError.IoError));
                break;
            }
        }

        return new FileOperationSummary(successCount, 0, failures);
    }

    public async Task<FileOperationSummary> MoveItemsAsync(
        IReadOnlyList<PhotoListItem> items,
        string destinationFolder,
        Func<string, bool, Task<ConflictResolution>> resolveConflictAsync,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var successCount = 0;
        var skipCount = 0;
        var failures = new List<FileOperationFailure>();
        var overwriteAll = false;
        var skipAll = false;

        for (var i = 0; i < items.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                failures.Add(new FileOperationFailure(items[i].FilePath, items[i].FileName, FileOperationError.Cancelled));
                break;
            }

            var item = items[i];
            var sourcePath = item.FilePath;
            var parent = Path.GetDirectoryName(sourcePath);
            if (string.IsNullOrWhiteSpace(parent))
            {
                failures.Add(new FileOperationFailure(sourcePath, item.FileName, FileOperationError.NoParent));
                continue;
            }

            if (IsSamePath(parent, destinationFolder))
            {
                progress?.Report(i + 1);
                continue;
            }

            if (item.IsFolder && IsDescendantPath(sourcePath, destinationFolder))
            {
                failures.Add(new FileOperationFailure(sourcePath, item.FileName, FileOperationError.DescendantPath));
                continue;
            }

            var targetPath = Path.Combine(destinationFolder, item.FileName);
            if (ItemExistsAtPath(targetPath))
            {
                ConflictResolution resolution;
                if (overwriteAll)
                {
                    resolution = ConflictResolution.Overwrite;
                }
                else if (skipAll)
                {
                    resolution = ConflictResolution.Skip;
                }
                else
                {
                    resolution = await resolveConflictAsync(item.FileName, item.IsFolder).ConfigureAwait(false);
                    if (resolution == ConflictResolution.OverwriteAll)
                    {
                        overwriteAll = true;
                        resolution = ConflictResolution.Overwrite;
                    }
                    else if (resolution == ConflictResolution.SkipAll)
                    {
                        skipAll = true;
                        resolution = ConflictResolution.Skip;
                    }
                }

                if (resolution == ConflictResolution.Cancel)
                {
                    failures.Add(new FileOperationFailure(sourcePath, item.FileName, FileOperationError.Cancelled));
                    break;
                }

                if (resolution == ConflictResolution.Skip)
                {
                    skipCount++;
                    progress?.Report(i + 1);
                    continue;
                }

                // Overwrite: 上書き移動
                try
                {
                    if (item.IsFolder)
                    {
                        // フォルダ上書き: 既存を削除してから移動
                        Directory.Delete(targetPath, recursive: true);
                    }
                    else
                    {
                        File.Delete(targetPath);
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException)
                {
                    AppLog.Error($"Failed to delete existing item for overwrite: {targetPath}", ex);
                    failures.Add(new FileOperationFailure(sourcePath, item.FileName, FileOperationError.Unauthorized));
                    progress?.Report(i + 1);
                    continue;
                }
                catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or PathTooLongException)
                {
                    AppLog.Error($"Failed to delete existing item for overwrite: {targetPath}", ex);
                    failures.Add(new FileOperationFailure(sourcePath, item.FileName, FileOperationError.IoError));
                    progress?.Report(i + 1);
                    continue;
                }
            }

            try
            {
                if (item.IsFolder)
                {
                    Directory.Move(sourcePath, targetPath);
                }
                else
                {
                    File.Move(sourcePath, targetPath);
                }

                successCount++;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException)
            {
                AppLog.Error($"Failed to move item: {sourcePath}", ex);
                failures.Add(new FileOperationFailure(sourcePath, item.FileName, FileOperationError.Unauthorized));
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or PathTooLongException)
            {
                AppLog.Error($"Failed to move item: {sourcePath}", ex);
                failures.Add(new FileOperationFailure(sourcePath, item.FileName, FileOperationError.IoError));
            }

            progress?.Report(i + 1);
        }

        AppLog.Info($"MoveItemsAsync: success={successCount}, skip={skipCount}, failure={failures.Count}");
        return new FileOperationSummary(successCount, skipCount, failures);
    }

    public async Task<FileOperationSummary> DeleteItemsAsync(IReadOnlyList<PhotoListItem> items)
    {
        var successCount = 0;
        var failures = new List<FileOperationFailure>();

        foreach (var item in items)
        {
            if (item.IsFolder && Directory.GetParent(item.FilePath) is null)
            {
                failures.Add(new FileOperationFailure(item.FilePath, item.FileName, FileOperationError.NoParent));
                break;
            }

            try
            {
                if (item.IsFolder)
                {
                    var folder = await StorageFolder.GetFolderFromPathAsync(item.FilePath);
                    await folder.DeleteAsync(StorageDeleteOption.Default);
                }
                else
                {
                    var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
                    await file.DeleteAsync(StorageDeleteOption.Default);
                }

                successCount++;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException)
            {
                AppLog.Error($"Failed to delete item: {item.FilePath}", ex);
                failures.Add(new FileOperationFailure(item.FilePath, item.FileName, FileOperationError.Unauthorized));
                break;
            }
            catch (FileNotFoundException)
            {
                // 別操作や同期ツールで既に消えている場合は次のアイテムへ継続
                AppLog.Info($"Delete skipped: item already missing: {item.FilePath}");
                failures.Add(new FileOperationFailure(item.FilePath, item.FileName, FileOperationError.IoError));
                continue;
            }
            catch (Exception ex) when (ex is COMException)
            {
                AppLog.Error($"Failed to delete item: {item.FilePath}", ex);
                failures.Add(new FileOperationFailure(item.FilePath, item.FileName, FileOperationError.IoError));
                break;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or PathTooLongException)
            {
                AppLog.Error($"Failed to delete item: {item.FilePath}", ex);
                failures.Add(new FileOperationFailure(item.FilePath, item.FileName, FileOperationError.IoError));
                break;
            }
        }

        return new FileOperationSummary(successCount, 0, failures);
    }
}
