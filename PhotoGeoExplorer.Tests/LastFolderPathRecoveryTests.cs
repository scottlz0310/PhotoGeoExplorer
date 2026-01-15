using System.Reflection;

namespace PhotoGeoExplorer.Tests;

[Collection("NonParallel")]
public sealed class LastFolderPathRecoveryTests
{
    [Fact]
    public void FindValidAncestorPathReturnsPathWhenValid()
    {
        // Arrange
        var tempRoot = CreateTempDirectory();
        try
        {
            var validPath = Path.Combine(tempRoot, "subfolder");
            Directory.CreateDirectory(validPath);

            // Act
            var result = InvokeFindValidAncestorPath(validPath);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(Path.GetFullPath(validPath), result);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void FindValidAncestorPathReturnsParentWhenChildNotExists()
    {
        // Arrange
        var tempRoot = CreateTempDirectory();
        try
        {
            var parentPath = Path.Combine(tempRoot, "parent");
            var childPath = Path.Combine(parentPath, "child");
            Directory.CreateDirectory(parentPath);
            // childPath は作成しない

            // Act
            var result = InvokeFindValidAncestorPath(childPath);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(Path.GetFullPath(parentPath), result);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void FindValidAncestorPathReturnsGrandparentWhenParentAndChildNotExist()
    {
        // Arrange
        var tempRoot = CreateTempDirectory();
        try
        {
            var grandparentPath = Path.Combine(tempRoot, "grandparent");
            var parentPath = Path.Combine(grandparentPath, "parent");
            var childPath = Path.Combine(parentPath, "child");
            Directory.CreateDirectory(grandparentPath);
            // parentPath と childPath は作成しない

            // Act
            var result = InvokeFindValidAncestorPath(childPath);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(Path.GetFullPath(grandparentPath), result);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void FindValidAncestorPathReturnsNullWhenNoValidAncestor()
    {
        // Arrange
        // 存在しないドライブやルートパスを使用
        var invalidPath = Path.Combine("Z:", "nonexistent", "path", "folder");

        // Act
        var result = InvokeFindValidAncestorPath(invalidPath);

        // Assert
        // Z: ドライブが存在する場合は Z: が返る可能性があるため、
        // 結果が null または無効なパスであることを確認
        if (result is not null)
        {
            // 返されたパスが元のパスの祖先であることを確認
            var normalizedResult = Path.GetFullPath(result);
            var normalizedInvalid = Path.GetFullPath(invalidPath);
            Assert.True(
                normalizedInvalid.StartsWith(normalizedResult, StringComparison.OrdinalIgnoreCase),
                $"Expected '{normalizedInvalid}' to start with '{normalizedResult}'");
        }
    }

    [Fact]
    public void FindValidAncestorPathReturnsNullForEmptyPath()
    {
        // Act
        var result = InvokeFindValidAncestorPath(string.Empty);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void FindValidAncestorPathReturnsNullForNullPath()
    {
        // Act
        var result = InvokeFindValidAncestorPath(null);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void FindValidAncestorPathHandlesRelativePaths()
    {
        // Arrange
        var tempRoot = CreateTempDirectory();
        try
        {
            var subfolder = Path.Combine(tempRoot, "subfolder");
            Directory.CreateDirectory(subfolder);

            // 相対パスから絶対パスに変換
            var currentDir = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(tempRoot);
                var relativePath = Path.Combine("subfolder", "nonexistent");

                // Act
                var result = InvokeFindValidAncestorPath(relativePath);

                // Assert
                Assert.NotNull(result);
                Assert.Equal(Path.GetFullPath(subfolder), result);
            }
            finally
            {
                Directory.SetCurrentDirectory(currentDir);
            }
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void FindValidAncestorPathHandlesDeepNesting()
    {
        // Arrange
        var tempRoot = CreateTempDirectory();
        try
        {
            var level1 = Path.Combine(tempRoot, "level1");
            var level2 = Path.Combine(level1, "level2");
            var level3 = Path.Combine(level2, "level3");
            var level4 = Path.Combine(level3, "level4");
            var level5 = Path.Combine(level4, "level5");

            Directory.CreateDirectory(level1);
            // level2 以降は作成しない

            // Act
            var result = InvokeFindValidAncestorPath(level5);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(Path.GetFullPath(level1), result);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static string? InvokeFindValidAncestorPath(string? path)
    {
        // MainWindow クラスの FindValidAncestorPath メソッドをリフレクションで呼び出す
        var mainWindowType = Type.GetType("PhotoGeoExplorer.MainWindow, PhotoGeoExplorer");
        Assert.NotNull(mainWindowType);

        var method = mainWindowType.GetMethod(
            "FindValidAncestorPath",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var result = method.Invoke(null, new object?[] { path });
        return result as string;
    }

    private static string CreateTempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "PhotoGeoExplorerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Intentionally empty - cleanup should not fail tests
        }
        catch (UnauthorizedAccessException)
        {
            // Intentionally empty - cleanup should not fail tests
        }
    }
}
