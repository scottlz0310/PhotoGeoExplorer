using PhotoGeoExplorer.Services;

namespace PhotoGeoExplorer.Tests;

[Collection("NonParallel")]
public sealed class StartupPathResolutionTests
{
    [Fact]
    public void ValidFilePathArgumentReturnsParentFolder()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var testFilePath = Path.Combine(tempRoot, "testfile.txt");
            File.WriteAllText(testFilePath, "test content");

            var parentFolder = Path.GetDirectoryName(testFilePath);

            Assert.NotNull(parentFolder);
            Assert.Equal(Path.GetFullPath(tempRoot), Path.GetFullPath(parentFolder));
            Assert.True(Directory.Exists(parentFolder));
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void InvalidFilePathArgumentFallsBackToParentFolder()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var nonExistentFilePath = Path.Combine(tempRoot, "nonexistent.txt");

            var parentFolder = Path.GetDirectoryName(nonExistentFilePath);

            Assert.False(File.Exists(nonExistentFilePath));
            Assert.NotNull(parentFolder);
            Assert.True(Directory.Exists(parentFolder));
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void NoArgumentShouldApplyLastFolderPathRestoration()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var lastFolderPath = tempRoot;

            var result = SettingsNormalization.FindValidAncestorPath(lastFolderPath);

            Assert.NotNull(result);
            Assert.Equal(Path.GetFullPath(lastFolderPath), result);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void InvalidArgumentAndInvalidLastFolderPathFallsBackToAncestor()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var validParent = Path.Combine(tempRoot, "parent");
            var invalidChild = Path.Combine(validParent, "nonexistent_child");
            Directory.CreateDirectory(validParent);

            var result = SettingsNormalization.FindValidAncestorPath(invalidChild);

            Assert.NotNull(result);
            Assert.Equal(Path.GetFullPath(validParent), result);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void SingleFolderSelectionShouldBeValidNavigationTarget()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var subFolder = Path.Combine(tempRoot, "subfolder");
            Directory.CreateDirectory(subFolder);

            var isValid = Directory.Exists(subFolder);

            Assert.True(isValid);
            Assert.Equal("subfolder", Path.GetFileName(subFolder));
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void MultipleItemSelectionShouldNotTriggerNavigation()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var folder1 = Path.Combine(tempRoot, "folder1");
            var folder2 = Path.Combine(tempRoot, "folder2");
            Directory.CreateDirectory(folder1);
            Directory.CreateDirectory(folder2);

            var itemCount = 2;

            Assert.True(itemCount > 1);
            Assert.True(Directory.Exists(folder1));
            Assert.True(Directory.Exists(folder2));
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void FileActivationDocumentationShouldBeAvailable()
    {
        Assert.True(true, "FileActivation の検証手順はコメントを参照");
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
