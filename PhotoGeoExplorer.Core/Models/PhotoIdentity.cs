namespace PhotoGeoExplorer.Models;

/// <summary>
/// 写真を同じものと見なす手がかり。利用者が写真ごとに確定した値を、後で同じ写真に結びつけるために使う
/// </summary>
/// <param name="FilePath">写真のフルパス</param>
/// <param name="FileSize">ファイルサイズ（バイト）</param>
/// <param name="TakenAt">撮影日時。分からなければ null</param>
/// <remarks>
/// パスが同じでも、サイズか撮影日時が違えば別の写真として扱う（同じ名前で撮り直した写真・編集した写真に、古い確定値を結びつけない）
/// </remarks>
internal sealed record PhotoIdentity(string FilePath, long FileSize, DateTimeOffset? TakenAt);
