namespace PhotoGeoExplorer.Services;

internal sealed record FileOperationResult
{
    public bool IsSuccess { get; init; }
    public FileOperationError Error { get; init; }
    public string? ResultPath { get; init; }

    private FileOperationResult() { }

    public static FileOperationResult Success(string? resultPath = null)
        => new() { IsSuccess = true, ResultPath = resultPath };

    public static FileOperationResult Failure(FileOperationError error)
        => new() { IsSuccess = false, Error = error };
}
