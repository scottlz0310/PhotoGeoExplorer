using System.IO;

namespace PhotoGeoExplorer;

internal interface ILogFileSystem
{
    long GetLength(string path);

    void CreateDirectory(string path);

    void AppendAllText(string path, string contents);

    void Move(string sourcePath, string destinationPath, bool overwrite);

    void Delete(string path);
}

internal sealed class PhysicalLogFileSystem : ILogFileSystem
{
    public long GetLength(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? info.Length : 0;
    }

    public void CreateDirectory(string path)
    {
        Directory.CreateDirectory(path);
    }

    public void AppendAllText(string path, string contents)
    {
        File.AppendAllText(path, contents);
    }

    public void Move(string sourcePath, string destinationPath, bool overwrite)
    {
        File.Move(sourcePath, destinationPath, overwrite);
    }

    public void Delete(string path)
    {
        File.Delete(path);
    }
}
