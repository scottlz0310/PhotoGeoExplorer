using System;
using System.IO;

namespace PhotoGeoExplorer;

/// <summary>
/// サイズ基準で世代管理する追記専用ログファイル。<c>app.log</c> が上限に達したら
/// <c>app.1.log</c> → <c>app.2.log</c> … へ繰り下げ、最大 <c>maxFiles</c> ファイルを保持する。
/// 書き込みと繰り下げは <see cref="ILogLock"/> で直列化する。
/// </summary>
internal sealed class RotatingLogFile
{
    private readonly ILogFileSystem _fileSystem;
    private readonly ILogLock _lock;
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly int _maxFiles;

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
        var directory = Path.GetDirectoryName(_path);
        var name = $"{Path.GetFileNameWithoutExtension(_path)}.{generation}{Path.GetExtension(_path)}";
        return string.IsNullOrEmpty(directory) ? name : Path.Combine(directory, name);
    }

    private void TryRotate()
    {
        try
        {
            if (_fileSystem.GetLength(_path) < _maxBytes)
            {
                return;
            }

            // 現行ログを先に退避する。他プロセスが書き込み中で退避できない場合は何も変更せず、次回に持ち越す。
            var stagingPath = $"{_path}.{Guid.NewGuid():N}.rotating";
            _fileSystem.Move(_path, stagingPath, overwrite: false);

            ShiftGenerations();
            _fileSystem.Move(stagingPath, GetGenerationPath(1), overwrite: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void ShiftGenerations()
    {
        // 最古の世代を捨てて、古い順に 1 つずつ繰り下げる。退避済みの現行ログを失わないことを優先し、
        // 個々の世代の繰り下げに失敗しても続行する（その場合、古い世代 1 つが上書きされ得る。診断用ログなので許容する）。
        TryDelete(GetGenerationPath(_maxFiles - 1));
        for (var generation = _maxFiles - 2; generation >= 1; generation--)
        {
            try
            {
                _fileSystem.Move(GetGenerationPath(generation), GetGenerationPath(generation + 1), overwrite: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
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
