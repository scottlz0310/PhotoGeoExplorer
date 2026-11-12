using PhotoGeoExplorer.Security;

namespace PhotoGeoExplorer.Tests;

public sealed class MapboxAccessTokenTests
{
    [Theory]
    [InlineData("pk.eyJ1IjoiZXhhbXBsZSJ9.abc-DEF_123", "pk.eyJ1IjoiZXhhbXBsZSJ9.abc-DEF_123")]
    [InlineData("  pk.abc.def  ", "pk.abc.def")]
    [InlineData("\tpk.abc\r\n", "pk.abc")]
    public void TryParseAcceptsPublicToken(string input, string expected)
    {
        var parsed = MapboxAccessToken.TryParse(input, out var token, out var error);

        Assert.True(parsed);
        Assert.Equal(expected, token);
        Assert.Equal(MapboxAccessTokenError.None, error);
    }

    [Theory]
    [InlineData(null, MapboxAccessTokenError.Empty)]
    [InlineData("", MapboxAccessTokenError.Empty)]
    [InlineData("   ", MapboxAccessTokenError.Empty)]
    [InlineData("sk.eyJ1IjoiZXhhbXBsZSJ9.secret", MapboxAccessTokenError.SecretToken)]
    [InlineData("  sk.abc  ", MapboxAccessTokenError.SecretToken)]
    [InlineData("tk.eyJ1IjoiZXhhbXBsZSJ9.temporary", MapboxAccessTokenError.InvalidFormat)]
    [InlineData("pk.", MapboxAccessTokenError.InvalidFormat)]
    [InlineData("PK.abc", MapboxAccessTokenError.InvalidFormat)]
    [InlineData("abcdef", MapboxAccessTokenError.InvalidFormat)]
    [InlineData("pk.abc def", MapboxAccessTokenError.InvalidFormat)]
    [InlineData("pk.abc&style=x", MapboxAccessTokenError.InvalidFormat)]
    [InlineData("pk.abc/../x", MapboxAccessTokenError.InvalidFormat)]
    internal void TryParseRejectsInvalidToken(string? input, MapboxAccessTokenError expectedError)
    {
        var parsed = MapboxAccessToken.TryParse(input, out var token, out var error);

        Assert.False(parsed);
        Assert.Equal(string.Empty, token);
        Assert.Equal(expectedError, error);
    }
}
