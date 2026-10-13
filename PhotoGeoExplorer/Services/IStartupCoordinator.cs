using System;
using System.Threading.Tasks;

namespace PhotoGeoExplorer.Services;

internal interface IStartupCoordinator : IDisposable
{
    string? StartupFilePath { get; }

    void SetStartupFilePath(string? filePath);

    Task ApplyStartupAsync();

    /// <summary>
    /// 起動時の初期化が完了したことを通知します。<see cref="NavigateToFileAsync"/> の待機が解除されます。
    /// </summary>
    void NotifyInitializationCompleted();

    /// <summary>
    /// 別プロセスからリダイレクトされたファイル起動を処理します。
    /// 起動時の初期化完了後に、受信順に 1 件ずつ直列で実行します。
    /// </summary>
    Task NavigateToFileAsync(string filePath);
}
