using System.Net;

namespace PhotoGeoExplorer.Services.AreaNames;

/// <summary>
/// 地域データのダウンロード失敗の種類。表示する文言の選択に使う
/// </summary>
internal enum AreaDataDownloadError
{
    /// <summary>接続できない・途中で切れた・時間切れ</summary>
    Network,

    /// <summary>サーバーが成功以外の状態コードを返した（404・403・429・5xx など）</summary>
    HttpStatus,

    /// <summary>ZIP ではない応答（ログイン画面への遷移など、認証が要る場合を含む）</summary>
    NotArchive,

    /// <summary>受信量が上限を超えた</summary>
    TooLarge
}

/// <summary>
/// 地域データをダウンロードできなかったことを表す。取り込み（検証）の失敗は <see cref="AreaDataFormatException"/> で報告する
/// </summary>
internal sealed class AreaDataDownloadException : IOException
{
    public AreaDataDownloadException()
    {
    }

    public AreaDataDownloadException(string message)
        : base(message)
    {
    }

    public AreaDataDownloadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public AreaDataDownloadException(AreaDataDownloadError error, string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
        StatusCode = statusCode;
    }

    public AreaDataDownloadError Error { get; }

    public HttpStatusCode? StatusCode { get; }
}
