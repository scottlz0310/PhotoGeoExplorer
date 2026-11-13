using System.Globalization;

using PhotoGeoExplorer.Services;
using Xunit;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// テストで「一定時間内に完了すること」を確かめる。<c>Task.WhenAny(task, Task.Delay(...))</c> は完了後も Delay のタイマーが
/// 残る（CA2027）ため、タイマーを残さない <see cref="Task.WaitAsync(TimeSpan)"/> を使う
/// </summary>
internal static class TestTasks
{
    public static async Task AssertCompletesWithinAsync(Task task, TimeSpan timeout)
    {
        try
        {
            await task.WaitAsync(timeout).ConfigureAwait(true);
        }
        catch (TimeoutException) when (!task.IsCompleted)
        {
            Assert.Fail($"The task did not complete within {timeout}.");
        }
    }
}

internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _originalCulture;
    private readonly CultureInfo _originalUiCulture;

    public CultureScope(CultureInfo culture)
    {
        _originalCulture = CultureInfo.CurrentCulture;
        _originalUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUiCulture;
    }
}

/// <summary>
/// 単体テストで実 <see cref="FolderWatcherService"/> による実 FileSystemWatcher / Timer 起動を防ぐ no-op フェイク。
/// <c>LoadFolderAsync</c> が temp ディレクトリを監視し、ディレクトリ削除と debounce タイマー発火が競合して
/// テストホストが非決定的にクラッシュする問題（#174）への対処。<see cref="FolderChanged"/> は発火しない。
/// ステートレス（Watch/Stop/Dispose と購読が全て no-op）のため、テストアセンブリ全体で <see cref="Shared"/>
/// を安全に共有でき、VM 生成ごとの実インスタンス生成を避けられる。
/// </summary>
internal sealed class NoOpFolderWatcherService : IFolderWatcherService
{
    public static readonly NoOpFolderWatcherService Shared = new();

    public event EventHandler? FolderChanged { add { } remove { } }

    public void Watch(string folderPath) { }

    public void Stop() { }

    public void Dispose() { }
}
