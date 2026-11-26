using System;
using System.IO;
using Xunit;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// プライバシーポリシーは、公開 URL の違い（<c>/privacy-policy</c> と <c>/privacy-policy/</c>）で別のファイルが
/// 配信されるため、2 つのファイルを同じ内容に保つ。
/// </summary>
public sealed class PublishedPrivacyPolicySyncTests
{
    [Fact]
    public void BothPublishedPrivacyPoliciesHaveTheSameContent()
    {
        var docs = Path.Combine(RepositoryPaths.FindRoot(), "docs");

        Assert.Equal(
            Read(Path.Combine(docs, "privacy-policy.html")),
            Read(Path.Combine(docs, "privacy-policy", "index.html")));
    }

    private static string Read(string path)
        => File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
}
