using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace PhotoGeoExplorer.Services;

internal static class TileCacheService
{
    private static readonly string CacheRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PhotoGeoExplorer",
        "Cache",
        "Tiles");

    public static bool TryGetTilePath(Uri uri, out string tilePath)
    {
        tilePath = string.Empty;
        if (uri is null || !uri.IsAbsoluteUri)
        {
            return false;
        }

        if (!uri.Host.EndsWith(".tile.openstreetmap.org", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Host, "tile.openstreetmap.org", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3)
        {
            return false;
        }

        if (!int.TryParse(segments[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var zoom))
        {
            return false;
        }

        if (!int.TryParse(segments[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x))
        {
            return false;
        }

        var yPart = segments[2];
        if (!yPart.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var yText = Path.GetFileNameWithoutExtension(yPart);
        if (!int.TryParse(yText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
        {
            return false;
        }

        tilePath = Path.Combine(
            CacheRoot,
            zoom.ToString(CultureInfo.InvariantCulture),
            x.ToString(CultureInfo.InvariantCulture),
            $"{y}.png");
        return true;
    }

    public static bool TryOpenTile(string tilePath, out FileStream? stream)
    {
        stream = null;
        if (string.IsNullOrWhiteSpace(tilePath) || !File.Exists(tilePath))
        {
            return false;
        }

        try
        {
            stream = new FileStream(tilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
        catch (NotSupportedException)
        {
        }

        return false;
    }

    public static async Task SaveTileAsync(string tilePath, Stream content)
    {
        if (string.IsNullOrWhiteSpace(tilePath))
        {
            return;
        }

        if (File.Exists(tilePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(tilePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var tempPath = $"{tilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await content.CopyToAsync(fileStream).ConfigureAwait(true);
            fileStream.Close();
            File.Move(tempPath, tilePath, overwrite: true);
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.Error($"Failed to cache tile: {tilePath}", ex);
        }
        catch (IOException ex)
        {
            AppLog.Error($"Failed to cache tile: {tilePath}", ex);
        }
        catch (NotSupportedException ex)
        {
            AppLog.Error($"Failed to cache tile: {tilePath}", ex);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
        catch (NotSupportedException)
        {
        }
    }
}
