namespace PhotoGeoExplorer.Services.AreaNames;

internal enum AreaDataFormatError
{
    Unspecified = 0,

    /// <summary>.prj がなく、座標系を確認できない</summary>
    MissingProjection,

    /// <summary>緯度経度の座標系ではない（平面直角座標系など）</summary>
    UnsupportedProjection,

    /// <summary>必須の属性がない</summary>
    MissingAttribute,

    /// <summary>小地域のポリゴンが 1 件もない</summary>
    NoRegions,

    /// <summary>ファイルが壊れている、または Shapefile として読めない</summary>
    CorruptData,

    /// <summary>KEY_CODE から都道府県を判別できない</summary>
    InvalidKeyCode,

    /// <summary>1 つのデータに複数の都道府県が含まれる</summary>
    MixedPrefectures,

    /// <summary>ZIP として読めない、または Shapefile の構成ファイルがそろっていない</summary>
    InvalidArchive,

    /// <summary>ファイル名から調査年を判別できない</summary>
    UnsupportedFileName,

    /// <summary>展開後のサイズが上限を超える</summary>
    TooLarge
}

/// <summary>
/// 地域データが、小地域の境界データとして読める形式ではない
/// </summary>
internal sealed class AreaDataFormatException : IOException
{
    public AreaDataFormatException()
    {
    }

    public AreaDataFormatException(string message)
        : base(message)
    {
    }

    public AreaDataFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public AreaDataFormatException(AreaDataFormatError error, string message)
        : base(message)
    {
        Error = error;
    }

    public AreaDataFormatException(AreaDataFormatError error, string message, Exception innerException)
        : base(message, innerException)
    {
        Error = error;
    }

    public AreaDataFormatError Error { get; }
}
