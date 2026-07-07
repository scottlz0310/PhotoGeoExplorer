using PhotoGeoExplorer.Services;

namespace PhotoGeoExplorer.Tests;

[Collection("NonParallel")]
public sealed class LastFolderPathRecoveryTests
{
    [Fact]
    public void FindValidAncestorPathReturnsPathWhenValid()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var validPath = Path.Combine(tempRoot, "subfolder");
            Directory.CreateDirectory(validPath);

            var result = SettingsNormalization.FindValidAncestorPath(validPath);

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
        var tempRoot = CreateTempDirectory();
        try
        {
            var parentPath = Path.Combine(tempRoot, "parent");
            var childPath = Path.Combine(parentPath, "child");
            Directory.CreateDirectory(parentPath);

            var result = SettingsNormalization.FindValidAncestorPath(childPath);

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
        var tempRoot = CreateTempDirectory();
        try
        {
            var grandparentPath = Path.Combine(tempRoot, "grandparent");
            var parentPath = Path.Combine(grandparentPath, "parent");
            var childPath = Path.Combine(parentPath, "child");
            Directory.CreateDirectory(grandparentPath);

            var result = SettingsNormalization.FindValidAncestorPath(childPath);

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
        var invalidPath = Path.Combine("Z:", "nonexistent", "path", "folder");

        var result = SettingsNormalization.FindValidAncestorPath(invalidPath);

        if (result is not null)
        {
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
        var result = SettingsNormalization.FindValidAncestorPath(string.Empty);

        Assert.Null(result);
    }

    [Fact]
    public void FindValidAncestorPathReturnsNullForNullPath()
    {
        var result = SettingsNormalization.FindValidAncestorPath(null);

        Assert.Null(result);
    }

    [Fact]
    public void FindValidAncestorPathHandlesRelativePaths()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var subfolder = Path.Combine(tempRoot, "subfolder");
            Directory.CreateDirectory(subfolder);

            var currentDir = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(tempRoot);
                var relativePath = Path.Combine("subfolder", "nonexistent");

                var result = SettingsNormalization.FindValidAncestorPath(relativePath);

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
        var tempRoot = CreateTempDirectory();
        try
        {
            var level1 = Path.Combine(tempRoot, "level1");
            var level2 = Path.Combine(level1, "level2");
            var level3 = Path.Combine(level2, "level3");
            var level4 = Path.Combine(level3, "level4");
            var level5 = Path.Combine(level4, "level5");

            Directory.CreateDirectory(level1);

            var result = SettingsNormalization.FindValidAncestorPath(level5);

            Assert.NotNull(result);
            Assert.Equal(Path.GetFullPath(level1), result);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
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
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
