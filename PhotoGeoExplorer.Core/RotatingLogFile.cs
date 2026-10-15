using System;
using System.Globalization;
using System.IO;

namespace PhotoGeoExplorer;

/// <summary>
/// サイズ基準で世代管理する追記専用ログファイル。<c>app.log</c> が上限に達したら
/// <c>app.1.log</c> → <c>app.2.log</c> … へ繰り下げ、最大 <c>maxFiles</c> ファイルを保持する。
/// 書き込みと繰り下げは <see cref="ILogLock"/> で直列化する。
/// </summary>
internal sealed class RotatingLogFile
{
    private const int RetryDivisor = 8;

    private readonly ILogFileSystem _fileSystem;
    private readonly ILogLock _lock;
    private readonly string _path;
    private readonly string _stagingPath;
    private readonly long _maxBytes;
    private readonly int _maxFiles;
    private long _retryAfterLength;

    public RotatingLogFile(ILogFileSystem fileSystem, ILogLock logLock, string path, long maxBytes, int maxFiles)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logLock);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxBytes, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFiles, 2);

        _fileSystem = fileSystem;
        _lock = logLock;
        _path = path;
        _stagingPath = GetSiblingPath("rotating");
        _maxBytes = maxBytes;
        _maxFiles = maxFiles;
    }

    /// <summary>
    /// 必要なら繰り下げてから追記する。繰り下げの失敗はログ出力を妨げず、次回の書き込みで再試行する。
    /// 追記の失敗は呼び出し元へ伝える（フォールバック先の判断は呼び出し元が行う）。
    /// </summary>
    public void Append(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            _fileSystem.CreateDirectory(directory);
        }

        using (_lock.Acquire())
        {
            TryRotate();
            _fileSystem.AppendAllText(_path, payload);
        }
    }

    internal string GetGenerationPath(int generation)
    {
        return GetSiblingPath(generation.ToString(CultureInfo.InvariantCulture));
    }

    // 繰り下げ中に現行ログを退避する固定名。1 つしか存在し得ないため蓄積せず、app*.log の回収対象にも含まれる。
    internal string StagingPath => _stagingPath;

    private string GetSiblingPath(string suffix)
    {
        var directory = Path.GetDirectoryName(_path);
        var name = $"{Path.GetFileNameWithoutExtension(_path)}.{suffix}{Path.GetExtension(_path)}";
        return string.IsNullOrEmpty(directory) ? name : Path.Combine(directory, name);
    }

    private void TryRotate()
    {
        try
        {
            var length = _fileSystem.GetLength(_path);
            // 前回の繰り下げが退避ログを配置できなかった場合は、上限に達していなくても先に回収する。
            var recovering = _fileSystem.GetLength(_stagingPath) > 0;
            if ((!recovering && length < _maxBytes) || length < _retryAfterLength)
            {
                return;
            }

            if (!recovering)
            {
                // 現行ログを先に退避する。他プロセスが書き込み中で退避できない場合は何も変更せず、次回に持ち越す。
                _fileSystem.Move(_path, _stagingPath, overwrite: false);
                length = 0;
            }

            if (TryPlaceStagedLog())
            {
                _retryAfterLength = 0;
                return;
            }

            // 配置できなかった退避ログは固定名のまま残し、次回以降に回収する（ファイルは増えない）。
            // 失敗が続く間に毎回の書き込みで繰り下げを試みないよう、一定量増えるまで間隔を空ける。
            _retryAfterLength = length + Math.Max(_maxBytes / RetryDivisor, 1);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // 最古の世代を捨てて古い順に 1 つずつ繰り下げ、空いた app.1.log へ退避ログを置く。
    // 世代の移動に失敗した場合は、そこで中断する（退避ログは失わない。繰り下げ済みの世代も失われない）。
    // 失敗が続くと、試行のたびに最古の世代が 1 つ捨てられる。診断用ログなので許容する。
    private bool TryPlaceStagedLog()
    {
        TryDelete(GetGenerationPath(_maxFiles - 1));
        for (var generation = _maxFiles - 2; generation >= 1; generation--)
        {
            try
            {
                _fileSystem.Move(GetGenerationPath(generation), GetGenerationPath(generation + 1), overwrite: true);
            }
            catch (FileNotFoundException)
            {
                // その世代がまだ無いだけ（空きスロット）。
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        try
        {
            _fileSystem.Move(_stagingPath, GetGenerationPath(1), overwrite: false);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            _fileSystem.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
