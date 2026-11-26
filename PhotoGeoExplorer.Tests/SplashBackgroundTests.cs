using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public static class SplashBackgroundTests
{
    /// <summary>
    /// 起動画面の背景色は、パッケージのマニフェストと起動ウィンドウの 2 箇所に書く必要がある。
    /// 画像を差し替えて片方だけ直すと、画像の周りに違う色が見える
    /// </summary>
    [Fact]
    public static void SplashBackgroundIsTheSameInManifestAndSplashWindow()
    {
        var appDirectory = Path.Combine(RepositoryPaths.FindRoot(), "PhotoGeoExplorer");

        var manifestColor = XDocument.Load(Path.Combine(appDirectory, "Package.appxmanifest"))
            .Descendants()
            .Single(element => element.Name.LocalName == "SplashScreen")
            .Attribute("BackgroundColor")?.Value;
        var windowColor = XDocument.Load(Path.Combine(appDirectory, "SplashWindow.xaml"))
            .Descendants()
            .Single(element => element.Name.LocalName == "Grid")
            .Attribute("Background")?.Value;

        Assert.NotNull(manifestColor);
        Assert.Equal(manifestColor, windowColor);
    }
}
