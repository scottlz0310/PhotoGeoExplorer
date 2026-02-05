using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Panes.FileBrowser;

/// <summary>
/// ファイルブラウザPane専用のサービス
/// ファイルシステム操作、ナビゲーション履歴、ソート処理を分離
/// </summary>
internal sealed class FileBrowserPaneService : IFileBrowserPaneService
{
    private const int MaxNavigationHistorySize = 100;
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".gif",
        ".bmp",
        ".tif",
        ".tiff",
        ".heic",
        ".webp"
    };

    private readonly FileSystemService _fileSystemService;
    private readonly Stack<string> _navigationBackStack = new();
    private readonly Stack<string> _navigationForwardStack = new();

    public FileBrowserPaneService()
        : this(new FileSystemService())
    {
    }

    internal FileBrowserPaneService(FileSystemService fileSystemService)
    {
        ArgumentNullException.ThrowIfNull(fileSystemService);
        _fileSystemService = fileSystemService;
    }

    public bool CanNavigateBack => _navigationBackStack.Count > 0;
    public bool CanNavigateForward => _navigationForwardStack.Count > 0;

    public async Task<List<PhotoListItem>> LoadFolderAsync(string folderPath, bool showImagesOnly, string? searchText)
    {
        ArgumentNullException.ThrowIfNull(folderPath);

        var items = await _fileSystemService
            .GetPhotoItemsAsync(folderPath, showImagesOnly, searchText)
            .ConfigureAwait(false);

        return items.Select(CreateListItem).ToList();
    }

    public ObservableCollection<BreadcrumbSegment> GetBreadcrumbs(string folderPath)
    {
        ArgumentNullException.ThrowIfNull(folderPath);

        var segments = new ObservableCollection<BreadcrumbSegment>();
        var currentPath = folderPath;

        while (!string.IsNullOrWhiteSpace(currentPath))
        {
            var directoryInfo = new DirectoryInfo(currentPath);
            var displayName = directoryInfo.Name;

            // ルートドライブの場合はフルパスを表示
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = directoryInfo.FullName;
            }

            var children = _fileSystemService.GetChildDirectories(directoryInfo.FullName);
            segments.Insert(0, new BreadcrumbSegment(displayName, directoryInfo.FullName, children));

            // 親ディレクトリへ
            var parent = directoryInfo.Parent;
            if (parent is null)
            {
                break;
            }

            currentPath = parent.FullName;
        }

        return segments;
    }

    public List<PhotoListItem> ApplySort(IEnumerable<PhotoListItem> items, FileSortColumn column, SortDirection direction)
    {
        ArgumentNullException.ThrowIfNull(items);

        var ordered = items.OrderByDescending(item => item.IsFolder);

        ordered = column switch
        {
            FileSortColumn.Name => direction == SortDirection.Ascending
                ? ordered.ThenBy(item => item.FileName, NaturalSortComparer.Instance)
                : ordered.ThenByDescending(item => item.FileName, NaturalSortComparer.Instance),
            FileSortColumn.ModifiedAt => direction == SortDirection.Ascending
                ? ordered.ThenBy(item => item.Item.ModifiedAt)
                : ordered.ThenByDescending(item => item.Item.ModifiedAt),
            FileSortColumn.Resolution => direction == SortDirection.Ascending
                ? ordered.ThenBy(item => GetResolutionSortKey(item, ascending: true))
                : ordered.ThenByDescending(item => GetResolutionSortKey(item, ascending: false)),
            FileSortColumn.Size => direction == SortDirection.Ascending
                ? ordered.ThenBy(item => item.Item.SizeBytes)
                : ordered.ThenByDescending(item => item.Item.SizeBytes),
            _ => ordered
        };

        if (column != FileSortColumn.Name)
        {
            ordered = ordered.ThenBy(item => item.FileName, NaturalSortComparer.Instance);
        }

        return ordered.ToList();
    }

    public string? NavigateBack(string currentPath)
    {
        if (_navigationBackStack.Count == 0)
        {
            return null;
        }

        var previousPath = _navigationBackStack.Pop();
        PushToForwardStack(currentPath);
        return previousPath;
    }

    public string? NavigateForward(string currentPath)
    {
        if (_navigationForwardStack.Count == 0)
        {
            return null;
        }

        var nextPath = _navigationForwardStack.Pop();
        PushToBackStack(currentPath);
        return nextPath;
    }

    public void PushToBackStack(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var normalizedPath = NormalizePath(path);

        // 履歴サイズの上限チェック
        if (_navigationBackStack.Count >= MaxNavigationHistorySize)
        {
            // スタックを一時的にリストに変換して古いものを削除
            var items = _navigationBackStack.ToList();
            items.RemoveAt(items.Count - 1); // 最も古い項目を削除
            _navigationBackStack.Clear();
            for (var i = items.Count - 1; i >= 0; i--)
            {
                _navigationBackStack.Push(items[i]);
            }
        }

        _navigationBackStack.Push(normalizedPath);
    }

    public void PushToForwardStack(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var normalizedPath = NormalizePath(path);

        // 履歴サイズの上限チェック
        if (_navigationForwardStack.Count >= MaxNavigationHistorySize)
        {
            // スタックを一時的にリストに変換して古いものを削除
            var items = _navigationForwardStack.ToList();
            items.RemoveAt(items.Count - 1); // 最も古い項目を削除
            _navigationForwardStack.Clear();
            for (var i = items.Count - 1; i >= 0; i--)
            {
                _navigationForwardStack.Push(items[i]);
            }
        }

        _navigationForwardStack.Push(normalizedPath);
    }

    public void ClearForwardStack()
    {
        _navigationForwardStack.Clear();
    }

    private static PhotoListItem CreateListItem(PhotoItem item)
    {
        var thumbnail = CanInitializeBitmapImage() ? CreateThumbnailImage(item.ThumbnailPath) : null;
        var toolTipText = GenerateToolTipText(item);

        // サムネイルキーを生成（画像ファイルのみ）
        string? thumbnailKey = null;
        if (!item.IsFolder && IsImageFile(item.FilePath))
        {
            var fileInfo = new FileInfo(item.FilePath);
            if (fileInfo.Exists)
            {
                thumbnailKey = ThumbnailService.GetThumbnailCacheKey(item.FilePath, fileInfo.LastWriteTimeUtc);
            }
        }

        return new PhotoListItem(item, thumbnail, toolTipText, thumbnailKey);
    }

    private static bool IsImageFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return ImageExtensions.Contains(extension);
    }

    private static string GenerateToolTipText(PhotoItem item)
    {
        var lines = new List<string>();

        // ファイル名
        lines.Add($"{LocalizationService.GetString("ToolTip.FileName")}: {item.FileName}");

        // フォルダの場合はファイル名と更新日時のみ
        if (item.IsFolder)
        {
            lines.Add($"{LocalizationService.GetString("ToolTip.ModifiedAt")}: {item.ModifiedAtText}");
            return string.Join("\n", lines);
        }

        // ファイルサイズ
        if (!string.IsNullOrWhiteSpace(item.SizeText))
        {
            lines.Add($"{LocalizationService.GetString("ToolTip.Size")}: {item.SizeText}");
        }

        // 解像度
        if (!string.IsNullOrWhiteSpace(item.ResolutionText))
        {
            lines.Add($"{LocalizationService.GetString("ToolTip.Resolution")}: {item.ResolutionText}");
        }

        // 更新日時
        lines.Add($"{LocalizationService.GetString("ToolTip.ModifiedAt")}: {item.ModifiedAtText}");

        // フルパス
        lines.Add($"{LocalizationService.GetString("ToolTip.FullPath")}: {item.FilePath}");

        return string.Join("\n", lines);
    }

    private static BitmapImage? CreateThumbnailImage(string? thumbnailPath)
    {
        if (string.IsNullOrWhiteSpace(thumbnailPath))
        {
            return null;
        }

        try
        {
            return new BitmapImage(new Uri(thumbnailPath));
        }
        catch (ArgumentException ex)
        {
            AppLog.Error($"Failed to load thumbnail image. Path: '{thumbnailPath}'", ex);
            return null;
        }
        catch (IOException ex)
        {
            AppLog.Error($"Failed to load thumbnail image. Path: '{thumbnailPath}'", ex);
            return null;
        }
        catch (UriFormatException ex)
        {
            AppLog.Error($"Failed to load thumbnail image. Path: '{thumbnailPath}'", ex);
            return null;
        }
    }

    private static bool CanInitializeBitmapImage()
    {
        // テスト環境または CI 環境では BitmapImage を作成しない
        var ci = Environment.GetEnvironmentVariable("CI");
        var githubActions = Environment.GetEnvironmentVariable("GITHUB_ACTIONS");
        if (!string.IsNullOrEmpty(ci) || !string.IsNullOrEmpty(githubActions))
        {
            return false;
        }

        // AppDomain 名による検出（フォールバック）
        var name = AppDomain.CurrentDomain.FriendlyName;
        if (!string.IsNullOrWhiteSpace(name)
            && (name.Contains("testhost", StringComparison.OrdinalIgnoreCase)
                || name.Contains("vstest", StringComparison.OrdinalIgnoreCase)
                || name.Contains("xunit", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return DispatcherQueue.GetForCurrentThread() is not null;
    }

    private static long GetResolutionSortKey(PhotoListItem item, bool ascending)
    {
        if (item.IsFolder)
        {
            return ascending ? long.MaxValue : long.MinValue;
        }

        if (item.PixelWidth is null || item.PixelHeight is null)
        {
            return ascending ? long.MaxValue : long.MinValue;
        }

        return (long)item.PixelWidth.Value * item.PixelHeight.Value;
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
