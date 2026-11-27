using System.IO.Pipes;
using System.Text;
using PhotoGeoExplorer.Services.MapRendering;

namespace PhotoGeoExplorer.Services.E2E;

/// <summary>
/// E2E 検証時だけ明示的に起動するローカル専用の名前付きパイプサーバー（#363 案 B）。
/// <see cref="PipeOptions.CurrentUserOnly"/> で現在のログオンユーザー以外の接続を拒否する。
/// </summary>
internal sealed class E2EMapControlServer : IDisposable
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _pipeName;
    private readonly E2EMapControlProtocol _protocol;
    private readonly Action<string> _logInfo;
    private readonly Action<string> _logError;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoopTask;
    private bool _disposed;

    internal E2EMapControlServer(
        IMapViewController controller,
        string? pipeName = null,
        Action<string>? logInfo = null,
        Action<string>? logError = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        _pipeName = string.IsNullOrWhiteSpace(pipeName) ? BuildPipeName(Environment.ProcessId) : pipeName;
        _logInfo = logInfo ?? (_ => { });
        _logError = logError ?? (_ => { });
        _protocol = new E2EMapControlProtocol(controller, _logInfo, _logError);
        _logInfo($@"[E2E][Control] Starting named pipe server: \\.\pipe\{_pipeName}");
        // 戻った時点で接続できるように、最初のパイプはここで作る。受け付けのループの開始を待つ作りでは、
        // スレッドプールが混み合うと、接続する側が時間切れになる（#397）。
#pragma warning disable CA2000 // 受け付けのループへ所有権を渡し、そこで破棄する。
        var firstPipe = CreatePipe();
#pragma warning restore CA2000
        var cancellationToken = _cts.Token;
        _acceptLoopTask = Task.Run(() => RunAcceptLoopAsync(firstPipe, cancellationToken));
    }

    internal string PipeName => _pipeName;

    internal static string BuildPipeName(int processId) => $"PhotoGeoExplorer.E2E.{processId}";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();
        try
        {
            _acceptLoopTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // キャンセルに伴う終了は無視する
        }
        finally
        {
            _cts.Dispose();
            _logInfo($@"[E2E][Control] Stopped named pipe server: \\.\pipe\{_pipeName}");
        }
    }

    private NamedPipeServerStream CreatePipe() => new(
        _pipeName,
        PipeDirection.InOut,
        maxNumberOfServerInstances: 1,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task RunAcceptLoopAsync(NamedPipeServerStream firstPipe, CancellationToken cancellationToken)
    {
        NamedPipeServerStream? pendingPipe = firstPipe;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var server = pendingPipe ?? CreatePipe();
                pendingPipe = null;

                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                await HandleConnectionAsync(server, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException ex)
            {
                _logError($"[E2E][Control] Pipe connection I/O error: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                _logError($"[E2E][Control] Failed to open named pipe '{_pipeName}': {ex.Message}");
                break;
            }
        }

        // ループが始まる前に停止した場合は、最初のパイプが残っている。
        if (pendingPipe is not null)
        {
            await pendingPipe.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(server, Utf8NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        using var writer = new StreamWriter(server, Utf8NoBom, bufferSize: 1024, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };

        while (!cancellationToken.IsCancellationRequested && server.IsConnected)
        {
            var (line, exceededLimit) = await ReadBoundedLineAsync(reader, E2EMapControlProtocol.MaxRequestLineLength, cancellationToken).ConfigureAwait(false);
            if (exceededLimit)
            {
                var errorResponse = await _protocol.HandleLineAsync(
                    new string('x', E2EMapControlProtocol.MaxRequestLineLength + 1),
                    cancellationToken).ConfigureAwait(false);
                await writer.WriteLineAsync(errorResponse.AsMemory(), cancellationToken).ConfigureAwait(false);
                // 超過行の残りを読み飛ばさず接続を閉じて安全にリセットする
                return;
            }

            if (line is null)
            {
                return;
            }

            var response = await _protocol.HandleLineAsync(line, cancellationToken).ConfigureAwait(false);
            await writer.WriteLineAsync(response.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<(string? Line, bool ExceededLimit)> ReadBoundedLineAsync(
        StreamReader reader,
        int maxLength,
        CancellationToken cancellationToken)
    {
        var buffer = new char[1];
        var builder = new StringBuilder();
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return builder.Length == 0 ? (null, false) : (builder.ToString(), false);
            }

            var ch = buffer[0];
            if (ch == '\n')
            {
                if (builder.Length > 0 && builder[^1] == '\r')
                {
                    builder.Length -= 1;
                }

                return (builder.ToString(), false);
            }

            builder.Append(ch);
            if (builder.Length > maxLength + 1)
            {
                return (null, true);
            }
        }
    }
}
