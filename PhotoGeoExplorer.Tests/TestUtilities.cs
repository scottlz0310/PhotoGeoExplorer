using System.Globalization;

using PhotoGeoExplorer.Services;

namespace PhotoGeoExplorer.Tests;

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
