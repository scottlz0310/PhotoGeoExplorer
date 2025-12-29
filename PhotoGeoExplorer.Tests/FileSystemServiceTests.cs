using PhotoGeoExplorer.Services;

namespace PhotoGeoExplorer.Tests;

[TestClass]
public sealed class FileSystemServiceTests
{
    [TestMethod]
    public async Task GetPhotoItemsAsyncReturnsDirectoriesBeforeFiles()
    {
        var root = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "BFolder"));
            Directory.CreateDirectory(Path.Combine(root, "AFolder"));
            await File.WriteAllTextAsync(Path.Combine(root, "b.txt"), "b").ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "a").ConfigureAwait(true);

            var service = new FileSystemService();
            var items = await service.GetPhotoItemsAsync(root, imagesOnly: false, searchText: null).ConfigureAwait(true);

            Assert.AreEqual(4, items.Count);
            Assert.IsTrue(items[0].IsFolder);
            Assert.AreEqual("AFolder", items[0].FileName);
            Assert.IsTrue(items[1].IsFolder);
            Assert.AreEqual("BFolder", items[1].FileName);
            Assert.IsFalse(items[2].IsFolder);
            Assert.AreEqual("a.txt", items[2].FileName);
            Assert.IsFalse(items[3].IsFolder);
            Assert.AreEqual("b.txt", items[3].FileName);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetPhotoItemsAsyncImagesOnlyFiltersNonImages()
    {
        var root = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Images"));
            await File.WriteAllTextAsync(Path.Combine(root, "note.txt"), "note").ConfigureAwait(true);

            var service = new FileSystemService();
            var items = await service.GetPhotoItemsAsync(root, imagesOnly: true, searchText: null).ConfigureAwait(true);

            Assert.AreEqual(1, items.Count);
            Assert.IsTrue(items[0].IsFolder);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [TestMethod]
    public void GetChildDirectoriesReturnsSortedNames()
    {
        var root = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Zoo"));
            Directory.CreateDirectory(Path.Combine(root, "Alpha"));

            var children = FileSystemService.GetChildDirectories(root);

            Assert.AreEqual(2, children.Count);
            Assert.AreEqual("Alpha", children[0].Name);
            Assert.AreEqual("Zoo", children[1].Name);
        }
        finally
        {
            TryDeleteDirectory(root);
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
