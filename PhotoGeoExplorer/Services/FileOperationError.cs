namespace PhotoGeoExplorer.Services;

internal enum FileOperationError
{
    None,
    InvalidName,
    AlreadyExists,
    DescendantPath,
    NoParent,
    IoError,
    Unauthorized,
}
