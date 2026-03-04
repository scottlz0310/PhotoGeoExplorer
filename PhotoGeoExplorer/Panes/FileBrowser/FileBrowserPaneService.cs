using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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

            var children = FileSystemService.GetChildDirectories(directoryInfo.FullName);
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
            FileSortColumn.TakenAt => direction == SortDirection.Ascending
                ? ordered.ThenBy(item => GetTakenAtSortKey(item, ascending: true))
                : ordered.ThenByDescending(item => GetTakenAtSortKey(item, ascending: false)),
            FileSortColumn.Resolution => direction == SortDirection.Ascending
                ? ordered.ThenBy(item => GetResolutionSortKey(item, ascending: true))
                : ordered.ThenByDescending(item => GetResolutionSortKey(item, ascending: false)),
            FileSortColumn.Size => direction == SortDirection.Ascending
                ? ordered.ThenBy(item => item.Item.SizeBytes)
                : ordered.ThenByDescending(item => item.Item.SizeBytes),
            FileSortColumn.Location => direction == SortDirection.Ascending
                ? ordered.ThenBy(item => GetLocationSortKey(item, ascending: true))
                : ordered.ThenByDescending(item => GetLocationSortKey(item, ascending: false)),
            _ => ordered
        };

        if (column != FileSortColumn.Name)
        {
            ordered = ordered.ThenBy(item => item.FileName, NaturalSortComparer.Instance);
        }

        return ordered.ToList();
    }

    public PhotoListItem? FindItemByFilePath(IEnumerable<PhotoListItem> items, string filePath)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!TryNormalizePath(filePath, out var normalizedFilePath))
        {
            return null;
        }

        foreach (var item in items)
        {
            if (!TryNormalizePath(item.FilePath, out var itemPath))
            {
                continue;
            }

            if (string.Equals(itemPath, normalizedFilePath, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return null;
    }

    public IReadOnlyList<PhotoListItem> ResolveItemsByFilePaths(IEnumerable<PhotoListItem> items, IReadOnlyList<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(filePaths);

        if (filePaths.Count == 0)
        {
            return Array.Empty<PhotoListItem>();
        }

        var itemLookup = new Dictionary<string, PhotoListItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (!TryNormalizePath(item.FilePath, out var itemPath))
            {
                continue;
            }

            itemLookup.TryAdd(itemPath, item);
        }

        var resolvedItems = new List<PhotoListItem>(filePaths.Count);
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var filePath in filePaths)
        {
            if (!TryNormalizePath(filePath, out var normalizedPath))
            {
                continue;
            }

            if (!seenPaths.Add(normalizedPath))
            {
                continue;
            }

            if (itemLookup.TryGetValue(normalizedPath, out var matchedItem))
            {
                resolvedItems.Add(matchedItem);
            }
        }

        return resolvedItems;
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

        return new PhotoListItem(item, thumbnail: null, toolTipText, thumbnailKey);
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

    private static DateTimeOffset GetTakenAtSortKey(PhotoListItem item, bool ascending)
    {
        if (item.IsFolder || item.TakenAt is null)
        {
            return ascending ? DateTimeOffset.MaxValue : DateTimeOffset.MinValue;
        }

        return item.TakenAt.Value;
    }

    private static int GetLocationSortKey(PhotoListItem item, bool ascending)
    {
        if (item.IsFolder || item.HasLocation is null)
        {
            return ascending ? int.MaxValue : int.MinValue;
        }

        return item.HasLocation.Value ? 1 : 0;
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool TryNormalizePath(string? path, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            normalizedPath = NormalizePath(path);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException
            or NotSupportedException
            or PathTooLongException
            or IOException)
        {
            AppLog.Error($"Failed to normalize path. Path: '{path}'", ex);
            return false;
        }
    }
}
