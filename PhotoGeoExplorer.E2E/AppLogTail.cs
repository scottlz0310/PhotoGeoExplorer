using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PhotoGeoExplorer.E2E;

/// <summary>
/// ローテーションを伴うアプリログ（<c>app.log</c> および <c>app.1.log</c> 以降の世代ファイル）から、
/// テスト開始時点以降に追記されたログ文字列を追跡・取得する。
/// </summary>
internal sealed class AppLogTail
{
    private readonly string _logPath;
    private readonly long _anchorPrefixLength;
    private readonly string _anchorPrefixHash;

    private AppLogTail(string logPath, long anchorPrefixLength, string anchorPrefixHash)
    {
        _logPath = logPath;
        _anchorPrefixLength = anchorPrefixLength;
        _anchorPrefixHash = anchorPrefixHash;
    }

    public static string DefaultLogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PhotoGeoExplorer",
        "Logs",
        "app.log");

    public static AppLogTail CaptureDefault() => Capture(DefaultLogPath);

    public static AppLogTail Capture(string logPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logPath);

        // app.log が空または不在の場合は既に存在していた最新世代をアンカーにし、過去のログ混入を防ぐ。
        foreach (var candidatePath in EnumerateLogFilesNewestFirst(logPath))
        {
            var (length, hash) = TryComputeFileHash(candidatePath);
            if (length > 0)
            {
                return new AppLogTail(logPath, length, hash);
            }
        }

        return new AppLogTail(logPath, 0, string.Empty);
    }

    public string ReadAppended()
    {
        var filesNewestFirst = EnumerateLogFilesNewestFirst(_logPath);
        if (filesNewestFirst.Count == 0)
        {
            return string.Empty;
        }

        if (_anchorPrefixLength <= 0)
        {
            return ReadJoinedOldestFirst(filesNewestFirst, filesNewestFirst.Count - 1, 0);
        }

        for (var i = 0; i < filesNewestFirst.Count; i++)
        {
            if (MatchesPrefixHash(filesNewestFirst[i], _anchorPrefixLength, _anchorPrefixHash))
            {
                return ReadJoinedOldestFirst(filesNewestFirst, i, _anchorPrefixLength);
            }
        }

        // 保持世代数を超えて開始時のファイルが押し出された場合は、残っている全世代を古い順に結合する。
        return ReadJoinedOldestFirst(filesNewestFirst, filesNewestFirst.Count - 1, 0);
    }

    private static List<string> EnumerateLogFilesNewestFirst(string logPath)
    {
        var result = new List<string>();
        var directory = Path.GetDirectoryName(logPath);
        if (string.IsNullOrEmpty(directory))
        {
            directory = ".";
        }

        if (!Directory.Exists(directory))
        {
            return result;
        }

        if (File.Exists(logPath))
        {
            result.Add(logPath);
        }

        var stem = Path.GetFileNameWithoutExtension(logPath);
        var extension = Path.GetExtension(logPath);

        var stagingPath = Path.Combine(directory, $"{stem}.rotating{extension}");
        if (File.Exists(stagingPath))
        {
            result.Add(stagingPath);
        }

        var prefix = stem + ".";
        var generations = new List<(int Generation, string Path)>();
        foreach (var filePath in Directory.EnumerateFiles(directory, $"{stem}.*{extension}"))
        {
            var fileName = Path.GetFileName(filePath);
            if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || !fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var middleLength = fileName.Length - prefix.Length - extension.Length;
            if (middleLength <= 0)
            {
                continue;
            }

            var middle = fileName.AsSpan(prefix.Length, middleLength);
            if (int.TryParse(middle, NumberStyles.None, CultureInfo.InvariantCulture, out var generation)
                && generation >= 1)
            {
                generations.Add((generation, filePath));
            }
        }

        generations.Sort(static (a, b) => a.Generation.CompareTo(b.Generation));
        foreach (var (_, path) in generations)
        {
            result.Add(path);
        }

        return result;
    }

    private static (long Length, string Hash) TryComputeFileHash(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[8192];
            long totalRead = 0;
            while (true)
            {
                var bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead <= 0)
                {
                    break;
                }

                hasher.AppendData(buffer.AsSpan(0, bytesRead));
                totalRead += bytesRead;
            }

            return totalRead == 0
                ? (0, string.Empty)
                : (totalRead, Convert.ToHexString(hasher.GetHashAndReset()));
        }
        catch (FileNotFoundException)
        {
            return (0, string.Empty);
        }
        catch (DirectoryNotFoundException)
        {
            return (0, string.Empty);
        }
    }

    private static bool MatchesPrefixHash(string path, long prefixLength, string expectedHash)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length < prefixLength)
            {
                return false;
            }

            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[8192];
            var remaining = prefixLength;
            while (remaining > 0)
            {
                var toRead = (int)Math.Min(buffer.Length, remaining);
                var bytesRead = stream.Read(buffer, 0, toRead);
                if (bytesRead <= 0)
                {
                    return false;
                }

                hasher.AppendData(buffer.AsSpan(0, bytesRead));
                remaining -= bytesRead;
            }

            var actualHash = Convert.ToHexString(hasher.GetHashAndReset());
            return string.Equals(actualHash, expectedHash, StringComparison.Ordinal);
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static string ReadJoinedOldestFirst(
        List<string> filesNewestFirst,
        int anchorIndex,
        long anchorOffset)
    {
        using var combined = new MemoryStream();
        for (var i = anchorIndex; i >= 0; i--)
        {
            var offset = i == anchorIndex ? anchorOffset : 0;
            AppendFileSlice(filesNewestFirst[i], offset, combined);
        }

        combined.Position = 0;
        using var reader = new StreamReader(combined, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static void AppendFileSlice(string path, long offset, Stream destination)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (offset > stream.Length)
            {
                return;
            }

            if (offset > 0)
            {
                _ = stream.Seek(offset, SeekOrigin.Begin);
            }

            stream.CopyTo(destination);
        }
        catch (FileNotFoundException)
        {
            // 読み取り中に世代が押し出された場合はスキップする。
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
