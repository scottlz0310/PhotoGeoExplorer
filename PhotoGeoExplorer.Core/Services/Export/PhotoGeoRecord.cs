using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services.AreaTree;

namespace PhotoGeoExplorer.Services.Export;

/// <summary>
/// 書き出しの対象にする 1 枚の写真の、撮影日時・撮影位置・字地名
/// </summary>
/// <param name="FilePath">写真のパス</param>
/// <param name="TakenAt">撮影日時。分からなければ null</param>
/// <param name="Coordinate">撮影位置。なければ null</param>
/// <param name="Kind">字地名の判定結果の種類</param>
/// <param name="Area"><see cref="PhotoAreaKind.Resolved"/> のときの字地名（水面を含む）。それ以外は null</param>
internal sealed record PhotoGeoRecord(
    string FilePath,
    DateTimeOffset? TakenAt,
    PhotoCoordinate? Coordinate,
    PhotoAreaKind Kind,
    AreaName? Area);

/// <summary>
/// 書き出し 1 件の計画（元のファイルと、出力先からの相対パス）
/// </summary>
internal sealed record ExportPlanItem(string SourcePath, string RelativePath);

/// <summary>
/// 字地名別フォルダーの、字地名がない写真を入れるフォルダー名（表示言語に合わせて呼び出し側が与える）
/// </summary>
/// <param name="Water">水面（海・湖など、住所のない場所）</param>
/// <param name="NoAreaData">位置情報はあるが、字地名を判定できない</param>
/// <param name="NoLocation">位置情報がない</param>
/// <param name="Unnamed">町丁・字の名称が空</param>
internal sealed record ExportFolderNames(string Water, string NoAreaData, string NoLocation, string Unnamed);

/// <summary>
/// リネーム出力のファイル名に使う、字地名がない写真の字名と、撮影日時がない写真の日付（表示言語に合わせて呼び出し側が与える）
/// </summary>
internal sealed record ExportRenameNames(string Water, string NoAreaData, string NoLocation, string Unnamed, string NoDate);
