using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Tests;

[TestClass]
public sealed class MainViewModelIntegrationTests
{
    [TestMethod]
    public async Task LoadFolderAsyncLoadsItemsAndBreadcrumbs()
    {
        var root = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Folder"));
            await File.WriteAllTextAsync(Path.Combine(root, "note.txt"), "note").ConfigureAwait(true);

            var viewModel = new MainViewModel(new FileSystemService())
            {
                ShowImagesOnly = false
            };

            await viewModel.LoadFolderAsync(root).ConfigureAwait(true);

            Assert.AreEqual(root, viewModel.CurrentFolderPath);
            Assert.AreEqual(2, viewModel.Items.Count);
            Assert.IsTrue(viewModel.Items[0].IsFolder);
            Assert.AreEqual("Folder", viewModel.Items[0].FileName);
            Assert.IsFalse(viewModel.Items[1].IsFolder);
            Assert.AreEqual("note.txt", viewModel.Items[1].FileName);
            Assert.IsTrue(viewModel.BreadcrumbItems.Count > 0);
            Assert.AreEqual(root, viewModel.BreadcrumbItems.Last().FullPath);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task ToggleSortBySizeReordersFiles()
    {
        var root = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Folder"));
            await File.WriteAllTextAsync(Path.Combine(root, "small.txt"), "a").ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(root, "large.txt"), new string('b', 200)).ConfigureAwait(true);

            var viewModel = new MainViewModel(new FileSystemService())
            {
                ShowImagesOnly = false
            };

            await viewModel.LoadFolderAsync(root).ConfigureAwait(true);
            viewModel.ToggleSort(FileSortColumn.Size);

            Assert.AreEqual("Folder", viewModel.Items[0].FileName);
            Assert.AreEqual("small.txt", viewModel.Items[1].FileName);
            Assert.AreEqual("large.txt", viewModel.Items[2].FileName);
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
