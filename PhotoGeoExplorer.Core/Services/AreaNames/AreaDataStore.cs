using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace PhotoGeoExplorer.Services.AreaNames;

/// <summary>
/// 取り込んだ地域データを、都道府県ごとのフォルダー（<c>&lt;ルート&gt;\&lt;県コード&gt;\r2ka31.shp など</c>）に置いて管理する。
/// 調査年はファイル名（<c>r2ka31</c> の <c>r2</c> = 令和 2 年）から判別するため、取り込み時の名前のまま保存する
/// </summary>
internal sealed partial class AreaDataStore : IAreaDataStore, IAreaDataSource
{
    /// <summary>展開後の合計サイズの上限（北海道の全体で約 32MB）</summary>
    internal const long MaxExtractedBytes = 512L * 1024 * 1024;

    private const string StagingPrefix = ".import-";
    private const string ReplacedPrefix = ".replaced-";

    private static readonly string[] ShapefileExtensions = [".shp", ".shx", ".dbf", ".prj"];

    private readonly string _rootDirectory;
    private readonly long _maxExtractedBytes;

    public AreaDataStore(string rootDirectory, long maxExtractedBytes = MaxExtractedBytes)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootDirectory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExtractedBytes);
        _rootDirectory = rootDirectory;
        _maxExtractedBytes = maxExtractedBytes;
    }

    public static string DefaultRootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PhotoGeoExplorer",
        "AreaData");

    public Task<IReadOnlyList<AreaDataPackInfo>> ListAsync(CancellationToken cancellationToken)
        => Task.Run<IReadOnlyList<AreaDataPackInfo>>(
            () => ReadInstalledPacks().Select(pack => pack.Info).ToList(),
            cancellationToken);

    public event EventHandler? DataChanged;

    public Task<AreaDataPackInfo> ImportAsync(string archivePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(archivePath);
        return ImportCoreAsync(archivePath, cancellationToken);
    }

    public Task<bool> DeleteAsync(string prefectureCode, CancellationToken cancellationToken)
    {
        if (!PrefectureCatalog.IsValidCode(prefectureCode))
        {
            throw new ArgumentException($"Not a prefecture code: {prefectureCode}", nameof(prefectureCode));
        }

        return DeleteCoreAsync(prefectureCode, cancellationToken);
    }

    public Task<AreaDataset?> LoadAsync(string prefectureCode, CancellationToken cancellationToken)
        => Task.Run(
            () =>
            {
                var pack = ReadInstalledPacks().FirstOrDefault(
                    candidate => string.Equals(candidate.Info.PrefectureCode, prefectureCode, StringComparison.Ordinal));
                return pack is null
                    ? null
                    : AreaShapefileReader.Read(pack.ShapefilePath, prefectureCode, pack.Info.CensusYear);
            },
            cancellationToken);

    private static int? ParseCensusYear(string baseName)
    {
        var match = CensusFileNamePattern().Match(baseName);
        if (!match.Success)
        {
            return null;
        }

        var number = int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture);

        // 令和元年 = 2019、平成元年 = 1989
        return char.ToLowerInvariant(match.Groups["era"].Value[0]) == 'r' ? 2018 + number : 1988 + number;
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 一時フォルダーの片付けに失敗しても、取り込みの結果には影響しない。次の取り込みの開始時に再度片付ける
            AppLog.Error($"Failed to delete a temporary area data directory: {directory}", ex);
        }
    }

    // 通知は呼び出し元の同期コンテキスト（UI スレッド）へ戻ってから行う。購読側が UI を更新するため
    private async Task<AreaDataPackInfo> ImportCoreAsync(string archivePath, CancellationToken cancellationToken)
    {
        var info = await Task.Run(() => Import(archivePath, cancellationToken), cancellationToken).ConfigureAwait(true);
        DataChanged?.Invoke(this, EventArgs.Empty);
        return info;
    }

    private async Task<bool> DeleteCoreAsync(string prefectureCode, CancellationToken cancellationToken)
    {
        var deleted = await Task.Run(
            () =>
            {
                var directory = Path.Combine(_rootDirectory, prefectureCode);
                if (!Directory.Exists(directory))
                {
                    return false;
                }

                Directory.Delete(directory, recursive: true);
                return true;
            },
            cancellationToken).ConfigureAwait(true);
        if (deleted)
        {
            DataChanged?.Invoke(this, EventArgs.Empty);
        }

        return deleted;
    }

    private AreaDataPackInfo Import(string archivePath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_rootDirectory);
        foreach (var leftover in Directory.EnumerateDirectories(_rootDirectory)
            .Where(directory => Path.GetFileName(directory).StartsWith(StagingPrefix, StringComparison.Ordinal)
                || Path.GetFileName(directory).StartsWith(ReplacedPrefix, StringComparison.Ordinal)))
        {
            TryDeleteDirectory(leftover);
        }

        var staging = Path.Combine(_rootDirectory, StagingPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var (shapefilePath, censusYear) = Extract(archivePath, staging, cancellationToken);

            // 読めること（座標系・必須属性・ポリゴン・単一の都道府県）を、置く前に確かめる
            var dataset = AreaShapefileReader.Read(shapefilePath, censusYear);
            cancellationToken.ThrowIfCancellationRequested();

            Install(staging, dataset.PrefectureCode);
            return ReadInstalledPacks()
                .First(pack => string.Equals(pack.Info.PrefectureCode, dataset.PrefectureCode, StringComparison.Ordinal))
                .Info;
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    private (string ShapefilePath, int CensusYear) Extract(string archivePath, string stagingDirectory, CancellationToken cancellationToken)
    {
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            var entries = archive.Entries
                .Where(entry => entry.Name.Length > 0
                    && ShapefileExtensions.Contains(Path.GetExtension(entry.Name), StringComparer.OrdinalIgnoreCase))
                .ToList();

            var shapefiles = entries.Where(entry => HasExtension(entry.Name, ".shp")).ToList();
            if (shapefiles.Count != 1)
            {
                throw new AreaDataFormatException(
                    AreaDataFormatError.InvalidArchive,
                    $"The archive must contain exactly one .shp file, but contains {shapefiles.Count}: {archivePath}");
            }

            var baseName = Path.GetFileNameWithoutExtension(shapefiles[0].Name);
            var censusYear = ParseCensusYear(baseName)
                ?? throw new AreaDataFormatException(
                    AreaDataFormatError.UnsupportedFileName,
                    $"The census year cannot be determined from the file name (expected names like r2ka31.shp): {shapefiles[0].Name}");

            var parts = new List<(ZipArchiveEntry Entry, string FileName)>();
            foreach (var extension in ShapefileExtensions)
            {
                var matches = entries
                    .Where(entry => string.Equals(entry.Name, baseName + extension, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (matches.Count != 1)
                {
                    throw new AreaDataFormatException(
                        AreaDataFormatError.InvalidArchive,
                        $"The archive must contain exactly one {baseName}{extension}, but contains {matches.Count}: {archivePath}");
                }

                parts.Add((matches[0], baseName + extension));
            }

            if (parts.Sum(part => part.Entry.Length) > _maxExtractedBytes)
            {
                throw new AreaDataFormatException(
                    AreaDataFormatError.TooLarge,
                    $"The archive is larger than the limit ({_maxExtractedBytes} bytes) once extracted: {archivePath}");
            }

            // ファイル名は baseName（検証済みの英数字だけ）と固定の拡張子から作り、ZIP 内のパスは使わない
            var extracted = 0L;
            foreach (var (entry, fileName) in parts)
            {
                using var input = entry.Open();
                using var output = File.Create(Path.Combine(stagingDirectory, fileName));
                var buffer = new byte[81920];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    extracted += read;
                    if (extracted > _maxExtractedBytes)
                    {
                        throw new AreaDataFormatException(
                            AreaDataFormatError.TooLarge,
                            $"The archive is larger than the limit ({_maxExtractedBytes} bytes) once extracted: {archivePath}");
                    }

                    output.Write(buffer, 0, read);
                }
            }

            return (Path.Combine(stagingDirectory, baseName + ".shp"), censusYear);
        }
        catch (InvalidDataException ex)
        {
            throw new AreaDataFormatException(
                AreaDataFormatError.InvalidArchive,
                $"The file is not a readable ZIP archive: {archivePath}",
                ex);
        }
    }

    // 置き換えは「既存を退避 → 新しいフォルダーを移動 → 退避を削除」の順で行い、移動に失敗したら既存を戻す
    private void Install(string stagingDirectory, string prefectureCode)
    {
        var target = Path.Combine(_rootDirectory, prefectureCode);
        string? replaced = null;
        if (Directory.Exists(target))
        {
            replaced = Path.Combine(_rootDirectory, ReplacedPrefix + Guid.NewGuid().ToString("N"));
            Directory.Move(target, replaced);
        }

        var installed = false;
        try
        {
            Directory.Move(stagingDirectory, target);
            installed = true;
        }
        finally
        {
            if (!installed && replaced is not null)
            {
                Directory.Move(replaced, target);
            }
        }

        if (replaced is not null)
        {
            TryDeleteDirectory(replaced);
        }
    }

    private List<InstalledPack> ReadInstalledPacks()
    {
        var packs = new List<InstalledPack>();
        if (!Directory.Exists(_rootDirectory))
        {
            return packs;
        }

        foreach (var directory in Directory.EnumerateDirectories(_rootDirectory))
        {
            var code = Path.GetFileName(directory);
            if (!PrefectureCatalog.TryGetName(code, out var name))
            {
                continue;
            }

            var pack = TryReadPack(directory, code, name);
            if (pack is null)
            {
                AppLog.Error($"Ignoring an area data directory that does not contain a complete shapefile: {directory}");
                continue;
            }

            packs.Add(pack);
        }

        return packs.OrderBy(pack => pack.Info.PrefectureCode, StringComparer.Ordinal).ToList();
    }

    private static InstalledPack? TryReadPack(string directory, string code, string name)
    {
        var shapefiles = Directory.GetFiles(directory, "*.shp");
        if (shapefiles.Length != 1)
        {
            return null;
        }

        var baseName = Path.GetFileNameWithoutExtension(shapefiles[0]);
        if (ParseCensusYear(baseName) is not { } censusYear
            || ShapefileExtensions.Any(extension => !File.Exists(Path.Combine(directory, baseName + extension))))
        {
            return null;
        }

        var size = ShapefileExtensions.Sum(extension => new FileInfo(Path.Combine(directory, baseName + extension)).Length);
        return new InstalledPack(new AreaDataPackInfo(code, name, censusYear, size), shapefiles[0]);
    }

    private static bool HasExtension(string fileName, string extension)
        => string.Equals(Path.GetExtension(fileName), extension, StringComparison.OrdinalIgnoreCase);

    // e-Stat の境界データのファイル名: r2ka31（令和 2 年・31 = 鳥取県）、h27ka31（平成 27 年）
    [GeneratedRegex(@"^(?<era>[hr])(?<year>[1-9]\d?)ka\d{2}$", RegexOptions.IgnoreCase)]
    private static partial Regex CensusFileNamePattern();

    private sealed record InstalledPack(AreaDataPackInfo Info, string ShapefilePath);
}
