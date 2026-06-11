using System;
using System.Threading.Tasks;

namespace PhotoGeoExplorer.Services;

/// <summary>
/// UI スレッドへのディスパッチを抽象化するサービス。
/// DispatcherQueue が取得できない環境（単体テスト等）では同期実行にフォールバックする。
/// </summary>
internal interface IUiDispatcher
{
    /// <summary>
    /// UI スレッドディスパッチが利用可能かどうか。
    /// false の場合、各メソッドはフォールバック動作（同期実行・no-op）になる。
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// アクションを UI スレッドで実行し、完了を待機する。
    /// 利用不可の場合は呼び出し元スレッドで同期実行する。
    /// </summary>
    Task RunAsync(Action action);

    /// <summary>
    /// 非同期関数を UI スレッドで開始し、その完了結果を返す。
    /// 利用不可の場合は呼び出し元スレッドでそのまま実行する。
    /// </summary>
    Task<T> EnqueueAsync<T>(Func<Task<T>> asyncFunc);

    /// <summary>
    /// アクションを UI スレッドへエンキューする（完了は待機しない）。
    /// 利用不可の場合は何もせず false を返す。
    /// </summary>
    bool TryEnqueue(Action action);

    /// <summary>
    /// UI スレッド上で Tick する繰り返しタイマーを作成する。
    /// 利用不可の場合は null を返す。
    /// </summary>
    IUiDispatcherTimer? CreateTimer();
}

/// <summary>
/// UI スレッド上で Tick する繰り返しタイマーの抽象。
/// </summary>
internal interface IUiDispatcherTimer
{
    TimeSpan Interval { get; set; }

    event EventHandler? Tick;

    void Start();

    void Stop();
}
