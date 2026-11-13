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
    NoRegions
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

    public AreaDataFormatError Error { get; }
}
