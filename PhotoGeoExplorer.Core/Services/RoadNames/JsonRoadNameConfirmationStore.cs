using System.Globalization;
using System.Text.Json;
using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Services.RoadNames;

/// <summary>
/// 確定した道路名を、<c>%LocalAppData%\PhotoGeoExplorer\road-name-confirmations.json</c> に保存する。
/// 写真は、フルパスで引き、サイズと撮影日時が確定したときと同じときだけ確定値を返す
/// </summary>
/// <remarks>
/// 読み書きは 1 つのロックの中で同期的に行い（件数が少なく、ミリ秒で終わる）、スレッドプールで走らせて呼び出し元を止めない。
/// 書き込みは一時ファイルへ書いてから置き換え、書けなかったときは記憶の内容も変えない（保存されていない確定を、見せ続けない）
/// </remarks>
internal sealed class JsonRoadNameConfirmationStore : IRoadNameConfirmationStore
{
    private const int FormatVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;
    private readonly Lock _gate = new();
    private Dictionary<string, Entry>? _entries;

    public JsonRoadNameConfirmationStore()
        : this(DefaultPath)
    {
    }

    internal JsonRoadNameConfirmationStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PhotoGeoExplorer",
        "road-name-confirmations.json");

    public Task<ConfirmedRoadName?> FindAsync(PhotoIdentity photo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photo);
        return Task.Run(
            () =>
            {
                lock (_gate)
                {
                    var entries = EnsureLoaded();
                    return entries.TryGetValue(photo.FilePath, out var entry) && entry.Matches(photo)
                        ? new ConfirmedRoadName(entry.Name, entry.Ref)
                        : null;
                }
            },
            cancellationToken);
    }

    public Task SaveAsync(PhotoIdentity photo, ConfirmedRoadName road, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(road);
        if (road.Name is null && road.Ref is null)
        {
            throw new ArgumentException("A road name or a route number is required.", nameof(road));
        }

        return Task.Run(
            () =>
            {
                lock (_gate)
                {
                    var next = new Dictionary<string, Entry>(EnsureLoaded(), StringComparer.OrdinalIgnoreCase)
                    {
                        [photo.FilePath] = new Entry(photo.FilePath, photo.FileSize, photo.TakenAt, road.Name, road.Ref)
                    };
                    Write(next);
                    _entries = next;
                }
            },
            cancellationToken);
    }

    public Task RemoveAsync(string filePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return Task.Run(
            () =>
            {
                lock (_gate)
                {
                    var current = EnsureLoaded();
                    if (!current.ContainsKey(filePath))
                    {
                        return;
                    }

                    var next = new Dictionary<string, Entry>(current, StringComparer.OrdinalIgnoreCase);
                    next.Remove(filePath);
                    Write(next);
                    _entries = next;
                }
            },
            cancellationToken);
    }

    // 呼び出し元が _gate を持っている
    private Dictionary<string, Entry> EnsureLoaded() => _entries ??= Load();

    private Dictionary<string, Entry> Load()
    {
        var entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(_path))
        {
            return entries;
        }

        StoreFile? file;
        try
        {
            file = JsonSerializer.Deserialize<StoreFile>(File.ReadAllBytes(_path), SerializerOptions);
        }
        catch (JsonException ex)
        {
            MoveAsideBrokenFile(ex);
            return entries;
        }

        if (file is not { Version: FormatVersion })
        {
            MoveAsideBrokenFile(new JsonException($"Unsupported format version: {file?.Version}."));
            return entries;
        }

        // null の要素・パスのない要素・名称も路線番号もない要素は、このアプリが書いた内容ではない。読めない内容と同じに扱う
        var items = file.Items ?? [];
        if (items.Any(entry => entry is not { Path: { } path } || string.IsNullOrWhiteSpace(path) || (entry.Name is null && entry.Ref is null)))
        {
            MoveAsideBrokenFile(new JsonException("The items contain an invalid entry."));
            return entries;
        }

        foreach (var entry in items)
        {
            entries[entry.Path] = entry;
        }

        return entries;
    }

    // 読めない内容を、次の保存で上書きして失わないよう、別名に退避してから空で始める
    private void MoveAsideBrokenFile(JsonException reason)
    {
        var brokenPath = $"{_path}.broken-{DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}";
        File.Move(_path, brokenPath, overwrite: true);
        AppLog.Error($"The road name confirmations could not be read. The file was moved to {brokenPath}.", reason);
    }

    private void Write(Dictionary<string, Entry> entries)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var file = new StoreFile(FormatVersion, [.. entries.Values.OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)]);
            File.WriteAllBytes(tempPath, JsonSerializer.SerializeToUtf8Bytes(file, SerializerOptions));
            File.Move(tempPath, _path, overwrite: true);
        }
        catch
        {
            File.Delete(tempPath);
            throw;
        }
    }

    private sealed record StoreFile(int Version, Entry[]? Items);

    private sealed record Entry(string Path, long Size, DateTimeOffset? TakenAt, string? Name, string? Ref)
    {
        public bool Matches(PhotoIdentity photo) => Size == photo.FileSize && TakenAt == photo.TakenAt;
    }
}
