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
}
