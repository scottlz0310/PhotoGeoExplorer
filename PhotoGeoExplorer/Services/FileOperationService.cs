using System;
using System.Collections.Generic;
using System.IO;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.ViewModels;

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

        return new FileOperationSummary(successCount, failures);
    }

    public FileOperationSummary DeleteItems(IReadOnlyList<PhotoListItem> items)
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
                    Directory.Delete(item.FilePath, recursive: true);
                }
                else
                {
                    File.Delete(item.FilePath);
                }

                successCount++;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException)
            {
                AppLog.Error($"Failed to delete item: {item.FilePath}", ex);
                failures.Add(new FileOperationFailure(item.FilePath, item.FileName, FileOperationError.Unauthorized));
                break;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or PathTooLongException)
            {
                AppLog.Error($"Failed to delete item: {item.FilePath}", ex);
                failures.Add(new FileOperationFailure(item.FilePath, item.FileName, FileOperationError.IoError));
                break;
            }
        }

        return new FileOperationSummary(successCount, failures);
    }
}
