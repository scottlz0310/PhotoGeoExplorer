using System;
using System.Collections.Generic;
using System.IO;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// FileOperationService のテスト
/// </summary>
public sealed class FileOperationServiceTests
{
    private readonly FileOperationService _service = new();

    // =========================================================
    // ContainsInvalidFileNameChars
    // =========================================================

    [Theory]
    [InlineData("validname")]
    [InlineData("file.jpg")]
    [InlineData("フォルダ名")]
    public void ContainsInvalidFileNameChars_ValidName_ReturnsFalse(string name)
        => Assert.False(_service.ContainsInvalidFileNameChars(name));

    [Theory]
    [InlineData("bad:name")]
    [InlineData("bad/name")]
    [InlineData("bad*name")]
    [InlineData("bad?name")]
    [InlineData("bad|name")]
    [InlineData("bad<name")]
    [InlineData("bad>name")]
    public void ContainsInvalidFileNameChars_InvalidName_ReturnsTrue(string name)
        => Assert.True(_service.ContainsInvalidFileNameChars(name));

    // =========================================================
    // NormalizeName
    // =========================================================

    [Fact]
    public void NormalizeName_FileRenameWithoutExtension_AppendsOriginalExtension()
    {
        var item = CreateFileItem(@"C:\photos\image.jpg");
        var result = _service.NormalizeName(item, "newname");
        Assert.Equal("newname.jpg", result);
    }

    [Fact]
    public void NormalizeName_FileRenameWithExtension_KeepsNewExtension()
    {
        var item = CreateFileItem(@"C:\photos\image.jpg");
        var result = _service.NormalizeName(item, "newname.png");
        Assert.Equal("newname.png", result);
    }

    [Fact]
    public void NormalizeName_FolderItem_ReturnsTrimmed()
    {
        var item = CreateFolderItem(@"C:\photos\myfolder");
        var result = _service.NormalizeName(item, "  newname  ");
        Assert.Equal("newname", result);
    }

    [Fact]
    public void NormalizeName_FileWithNoOriginalExtension_ReturnsTrimmed()
    {
        var item = CreateFileItem(@"C:\photos\noext");
        var result = _service.NormalizeName(item, "  renamed  ");
        Assert.Equal("renamed", result);
    }

    // =========================================================
    // IsSamePath
    // =========================================================

    [Fact]
    public void IsSamePath_IdenticalPaths_ReturnsTrue()
    {
        var dir = Path.GetTempPath();
        Assert.True(_service.IsSamePath(dir, dir));
    }

    [Fact]
    public void IsSamePath_DifferentCase_ReturnsTrue()
    {
        Assert.True(_service.IsSamePath(@"C:\Photos", @"C:\photos"));
    }

    [Fact]
    public void IsSamePath_DifferentPaths_ReturnsFalse()
    {
        Assert.False(_service.IsSamePath(@"C:\Photos", @"C:\Videos"));
    }

    // =========================================================
    // IsDescendantPath
    // =========================================================

    [Fact]
    public void IsDescendantPath_ChildFolder_ReturnsTrue()
    {
        Assert.True(_service.IsDescendantPath(@"C:\Photos", @"C:\Photos\2024\Japan"));
    }

    [Fact]
    public void IsDescendantPath_SamePath_ReturnsFalse()
    {
        Assert.False(_service.IsDescendantPath(@"C:\Photos", @"C:\Photos"));
    }

    [Fact]
    public void IsDescendantPath_ParentFolder_ReturnsFalse()
    {
        Assert.False(_service.IsDescendantPath(@"C:\Photos\2024", @"C:\Photos"));
    }

    // =========================================================
    // GetParentPath
    // =========================================================

    [Fact]
    public void GetParentPath_NormalPath_ReturnsParent()
    {
        var result = _service.GetParentPath(@"C:\Photos\image.jpg");
        Assert.Equal(@"C:\Photos", result);
    }

    [Fact]
    public void GetParentPath_DriveRoot_ReturnsNull()
    {
        var result = _service.GetParentPath(@"C:\");
        Assert.Null(result);
    }

    // =========================================================
    // ItemExistsAtPath
    // =========================================================

    [Fact]
    public void ItemExistsAtPath_ExistingFile_ReturnsTrue()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            Assert.True(_service.ItemExistsAtPath(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ItemExistsAtPath_ExistingDirectory_ReturnsTrue()
    {
        Assert.True(_service.ItemExistsAtPath(Path.GetTempPath()));
    }

    [Fact]
    public void ItemExistsAtPath_NonExistentPath_ReturnsFalse()
    {
        Assert.False(_service.ItemExistsAtPath(Path.Combine(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid()}")));
    }

    // =========================================================
    // FolderExistsAtPath
    // =========================================================

    [Fact]
    public void FolderExistsAtPath_ExistingDirectory_ReturnsTrue()
    {
        Assert.True(_service.FolderExistsAtPath(Path.GetTempPath()));
    }

    [Fact]
    public void FolderExistsAtPath_ExistingFile_ReturnsFalse()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            Assert.False(_service.FolderExistsAtPath(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void FolderExistsAtPath_NonExistentPath_ReturnsFalse()
    {
        Assert.False(_service.FolderExistsAtPath(Path.Combine(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid()}")));
    }

    // =========================================================
    // IsJpegFile
    // =========================================================

    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("photo.JPG")]
    [InlineData("photo.jpeg")]
    [InlineData("photo.JPEG")]
    public void IsJpegFile_JpegExtension_ReturnsTrue(string fileName)
        => Assert.True(_service.IsJpegFile(Path.Combine(@"C:\photos", fileName)));

    [Theory]
    [InlineData("photo.png")]
    [InlineData("photo.heic")]
    [InlineData("photo.tiff")]
    [InlineData("noextension")]
    [InlineData("")]
    public void IsJpegFile_NonJpegExtension_ReturnsFalse(string fileName)
        => Assert.False(_service.IsJpegFile(fileName));

    // =========================================================
    // CreateFolder
    // =========================================================

    [Fact]
    public void CreateFolder_NewFolder_ReturnsSuccessWithResultPath()
    {
        var tempDir = CreateTempTestDirectory();
        try
        {
            var result = _service.CreateFolder(tempDir, "NewFolder");

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.ResultPath);
            Assert.True(Directory.Exists(result.ResultPath));
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public void CreateFolder_ExistingFolderName_ReturnsAlreadyExists()
    {
        var tempDir = CreateTempTestDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(tempDir, "Existing"));

            var result = _service.CreateFolder(tempDir, "Existing");

            Assert.False(result.IsSuccess);
            Assert.Equal(FileOperationError.AlreadyExists, result.Error);
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    // =========================================================
    // RenameItem（ファイル）
    // =========================================================

    [Fact]
    public void RenameItem_File_ReturnsSuccessAndFileExists()
    {
        var tempDir = CreateTempTestDirectory();
        try
        {
            var srcPath = Path.Combine(tempDir, "original.jpg");
            File.WriteAllText(srcPath, "content");
            var item = CreateFileItem(srcPath);

            var result = _service.RenameItem(item, "renamed.jpg");

            Assert.True(result.IsSuccess);
            Assert.Equal(Path.Combine(tempDir, "renamed.jpg"), result.ResultPath);
            Assert.True(File.Exists(result.ResultPath));
            Assert.False(File.Exists(srcPath));
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public void RenameItem_Directory_ReturnsSuccessAndDirectoryExists()
    {
        var tempDir = CreateTempTestDirectory();
        try
        {
            var srcPath = Path.Combine(tempDir, "OldName");
            Directory.CreateDirectory(srcPath);
            var item = CreateFolderItem(srcPath);

            var result = _service.RenameItem(item, "NewName");

            Assert.True(result.IsSuccess);
            Assert.True(Directory.Exists(result.ResultPath));
            Assert.False(Directory.Exists(srcPath));
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public void RenameItem_TargetNameAlreadyExists_ReturnsAlreadyExists()
    {
        var tempDir = CreateTempTestDirectory();
        try
        {
            var srcPath = Path.Combine(tempDir, "original.jpg");
            var existingPath = Path.Combine(tempDir, "existing.jpg");
            File.WriteAllText(srcPath, "content");
            File.WriteAllText(existingPath, "other");
            var item = CreateFileItem(srcPath);

            var result = _service.RenameItem(item, "existing.jpg");

            Assert.False(result.IsSuccess);
            Assert.Equal(FileOperationError.AlreadyExists, result.Error);
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    // =========================================================
    // MoveItems
    // =========================================================

    [Fact]
    public void MoveItems_SingleFile_ReturnsSuccessCount1()
    {
        var tempDir = CreateTempTestDirectory();
        var destDir = CreateTempTestDirectory();
        try
        {
            var srcPath = Path.Combine(tempDir, "photo.jpg");
            File.WriteAllText(srcPath, "content");
            var items = new List<PhotoListItem> { CreateFileItem(srcPath) };

            var summary = _service.MoveItems(items, destDir);

            Assert.Equal(1, summary.SuccessCount);
            Assert.True(summary.IsAllSuccess);
            Assert.True(File.Exists(Path.Combine(destDir, "photo.jpg")));
        }
        finally
        {
            CleanupTempDirectory(tempDir);
            CleanupTempDirectory(destDir);
        }
    }

    [Fact]
    public void MoveItems_SameDirectory_SkipsWithoutError()
    {
        var tempDir = CreateTempTestDirectory();
        try
        {
            var srcPath = Path.Combine(tempDir, "photo.jpg");
            File.WriteAllText(srcPath, "content");
            var items = new List<PhotoListItem> { CreateFileItem(srcPath) };

            var summary = _service.MoveItems(items, tempDir);

            Assert.Equal(0, summary.SuccessCount);
            Assert.True(summary.IsAllSuccess);
            Assert.True(File.Exists(srcPath));
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public void MoveItems_FolderIntoDescendant_ReturnsDescendantPathError()
    {
        var tempDir = CreateTempTestDirectory();
        try
        {
            var srcFolder = Path.Combine(tempDir, "Parent");
            var destFolder = Path.Combine(srcFolder, "Child");
            Directory.CreateDirectory(srcFolder);
            Directory.CreateDirectory(destFolder);
            var items = new List<PhotoListItem> { CreateFolderItem(srcFolder) };

            var summary = _service.MoveItems(items, destFolder);

            Assert.False(summary.IsAllSuccess);
            Assert.True(summary.HasFailures);
            Assert.Equal(1, summary.FailureCount);
            Assert.Equal(FileOperationError.DescendantPath, summary.Failures[0].Error);
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public void MoveItems_TargetAlreadyExists_ReturnsAlreadyExistsError()
    {
        var tempDir = CreateTempTestDirectory();
        var destDir = CreateTempTestDirectory();
        try
        {
            var srcPath = Path.Combine(tempDir, "photo.jpg");
            var conflictPath = Path.Combine(destDir, "photo.jpg");
            File.WriteAllText(srcPath, "original");
            File.WriteAllText(conflictPath, "conflict");
            var items = new List<PhotoListItem> { CreateFileItem(srcPath) };

            var summary = _service.MoveItems(items, destDir);

            Assert.False(summary.IsAllSuccess);
            Assert.True(summary.HasFailures);
            Assert.Equal(1, summary.FailureCount);
            Assert.Equal(FileOperationError.AlreadyExists, summary.Failures[0].Error);
        }
        finally
        {
            CleanupTempDirectory(tempDir);
            CleanupTempDirectory(destDir);
        }
    }

    [Fact]
    public void MoveItems_FileLockedByAnotherProcess_ReturnsIoError()
    {
        var tempDir = CreateTempTestDirectory();
        var destDir = CreateTempTestDirectory();
        try
        {
            var srcPath = Path.Combine(tempDir, "locked.txt");
            File.WriteAllText(srcPath, "content");

            using var fs = File.Open(srcPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var items = new List<PhotoListItem> { CreateFileItem(srcPath) };

            var summary = _service.MoveItems(items, destDir);

            Assert.False(summary.IsAllSuccess);
            Assert.True(summary.HasFailures);
            Assert.Equal(1, summary.FailureCount);
            Assert.Equal(FileOperationError.IoError, summary.Failures[0].Error);
        }
        finally
        {
            CleanupTempDirectory(tempDir);
            CleanupTempDirectory(destDir);
        }
    }

    // =========================================================
    // CopyItems
    // =========================================================

    [Fact]
    public void CopyItems_SingleFile_ReturnsSuccessCount1AndSourceRemains()
    {
        var tempDir = CreateTempTestDirectory();
        var destDir = CreateTempTestDirectory();
        try
        {
            var srcPath = Path.Combine(tempDir, "photo.jpg");
            File.WriteAllText(srcPath, "content");
            var items = new List<PhotoListItem> { CreateFileItem(srcPath) };

            var summary = _service.CopyItems(items, destDir);

            Assert.Equal(1, summary.SuccessCount);
            Assert.True(summary.IsAllSuccess);
            Assert.True(File.Exists(Path.Combine(destDir, "photo.jpg")));
            Assert.True(File.Exists(srcPath), "コピー元は残っている");
        }
        finally
        {
            CleanupTempDirectory(tempDir);
            CleanupTempDirectory(destDir);
        }
    }

    [Fact]
    public void CopyItems_TargetAlreadyExists_ReturnsAlreadyExistsError()
    {
        var tempDir = CreateTempTestDirectory();
        var destDir = CreateTempTestDirectory();
        try
        {
            var srcPath = Path.Combine(tempDir, "photo.jpg");
            var conflictPath = Path.Combine(destDir, "photo.jpg");
            File.WriteAllText(srcPath, "original");
            File.WriteAllText(conflictPath, "conflict");
            var items = new List<PhotoListItem> { CreateFileItem(srcPath) };

            var summary = _service.CopyItems(items, destDir);

            Assert.False(summary.IsAllSuccess);
            Assert.Equal(1, summary.FailureCount);
            Assert.Equal(FileOperationError.AlreadyExists, summary.Failures[0].Error);
            Assert.Equal("conflict", File.ReadAllText(conflictPath), "コピー先は上書きされていない");
        }
        finally
        {
            CleanupTempDirectory(tempDir);
            CleanupTempDirectory(destDir);
        }
    }

    [Fact]
    public void CopyItems_MultipleFiles_ReturnsSuccessCountForAll()
    {
        var tempDir = CreateTempTestDirectory();
        var destDir = CreateTempTestDirectory();
        try
        {
            var src1 = Path.Combine(tempDir, "a.jpg");
            var src2 = Path.Combine(tempDir, "b.jpg");
            File.WriteAllText(src1, "a");
            File.WriteAllText(src2, "b");
            var items = new List<PhotoListItem>
            {
                CreateFileItem(src1),
                CreateFileItem(src2)
            };

            var summary = _service.CopyItems(items, destDir);

            Assert.Equal(2, summary.SuccessCount);
            Assert.True(summary.IsAllSuccess);
            Assert.True(File.Exists(Path.Combine(destDir, "a.jpg")));
            Assert.True(File.Exists(Path.Combine(destDir, "b.jpg")));
        }
        finally
        {
            CleanupTempDirectory(tempDir);
            CleanupTempDirectory(destDir);
        }
    }

    // =========================================================
    // DeleteItems
    // =========================================================

    [Fact]
    public void DeleteItems_SingleFile_ReturnsSuccessCount1()
    {
        var tempDir = CreateTempTestDirectory();
        try
        {
            var srcPath = Path.Combine(tempDir, "photo.jpg");
            File.WriteAllText(srcPath, "content");
            var items = new List<PhotoListItem> { CreateFileItem(srcPath) };

            var summary = _service.DeleteItems(items);

            Assert.Equal(1, summary.SuccessCount);
            Assert.True(summary.IsAllSuccess);
            Assert.False(File.Exists(srcPath));
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public void DeleteItems_Directory_ReturnsSuccessCount1()
    {
        var tempDir = CreateTempTestDirectory();
        try
        {
            var srcDir = Path.Combine(tempDir, "SubFolder");
            Directory.CreateDirectory(srcDir);
            File.WriteAllText(Path.Combine(srcDir, "file.txt"), "content");
            var items = new List<PhotoListItem> { CreateFolderItem(srcDir) };

            var summary = _service.DeleteItems(items);

            Assert.Equal(1, summary.SuccessCount);
            Assert.True(summary.IsAllSuccess);
            Assert.False(Directory.Exists(srcDir));
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public void DeleteItems_FileLockedByAnotherProcess_ReturnsIoError()
    {
        var tempDir = CreateTempTestDirectory();
        try
        {
            var srcPath = Path.Combine(tempDir, "locked.txt");
            File.WriteAllText(srcPath, "content");

            using var fs = File.Open(srcPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var items = new List<PhotoListItem> { CreateFileItem(srcPath) };

            var summary = _service.DeleteItems(items);

            Assert.False(summary.IsAllSuccess);
            Assert.True(summary.HasFailures);
            Assert.Equal(1, summary.FailureCount);
            Assert.Equal(FileOperationError.IoError, summary.Failures[0].Error);
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    // =========================================================
    // ヘルパー
    // =========================================================

    private static PhotoListItem CreateFileItem(string filePath)
    {
        var item = new PhotoItem(filePath, sizeBytes: 0, modifiedAt: DateTimeOffset.UtcNow, isFolder: false);
        return new PhotoListItem(item, thumbnail: null);
    }

    private static PhotoListItem CreateFolderItem(string folderPath)
    {
        var item = new PhotoItem(folderPath, sizeBytes: 0, modifiedAt: DateTimeOffset.UtcNow, isFolder: true);
        return new PhotoListItem(item, thumbnail: null);
    }

    private static string CreateTempTestDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"test-fileop-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    private static void CleanupTempDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
