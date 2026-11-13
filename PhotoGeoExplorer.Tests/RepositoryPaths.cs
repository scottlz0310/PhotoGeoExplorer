using System;
using System.IO;

namespace PhotoGeoExplorer.Tests;

internal static class RepositoryPaths
{
    /// <summary>
    /// <c>PhotoGeoExplorer.sln</c> のあるフォルダー（リポジトリのルート）を、テストの出力先からさかのぼって探す
    /// </summary>
    public static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PhotoGeoExplorer.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"PhotoGeoExplorer.sln が見つかりません（起点: {AppContext.BaseDirectory}）。");
    }
}
