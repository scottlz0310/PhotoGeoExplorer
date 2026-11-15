using System;
using System.Collections.Generic;
using PhotoGeoExplorer.Services;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class E2EOptionsTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("1", true)]
    [InlineData("true", true)]
    public void IsAreaDataDownloadDisabledFollowsTheEnvironmentVariable(string? value, bool expected)
    {
        var variables = new Dictionary<string, string?> { [E2EOptions.DisableAreaDataDownloadVariable] = value };

        Assert.Equal(expected, E2EOptions.IsAreaDataDownloadDisabled(name => variables.GetValueOrDefault(name)));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("1", true)]
    public void IsAreaDataDownloadDisabledReadsTheProcessEnvironmentByDefault(string? value, bool expected)
    {
        // 実アプリが使う、引数を省略した経路。このクラスだけが対象の環境変数を変更するため、並列実行でも干渉しない
        var original = Environment.GetEnvironmentVariable(E2EOptions.DisableAreaDataDownloadVariable);
        try
        {
            Environment.SetEnvironmentVariable(E2EOptions.DisableAreaDataDownloadVariable, value);

            Assert.Equal(expected, E2EOptions.IsAreaDataDownloadDisabled());
        }
        finally
        {
            Environment.SetEnvironmentVariable(E2EOptions.DisableAreaDataDownloadVariable, original);
        }
    }
}
