using System;

namespace PhotoGeoExplorer.Security;

internal enum MapboxAccessTokenError
{
    None = 0,
    Empty = 1,
    SecretToken = 2,
    InvalidFormat = 3
}

/// <summary>
/// Mapbox アクセストークンの形式検証。
/// クライアントに置いてよい公開トークン（pk.）だけを受け付ける。
/// </summary>
internal static class MapboxAccessToken
{
    private const string PublicPrefix = "pk.";
    private const string SecretPrefix = "sk.";

    internal static bool TryParse(string? input, out string token, out MapboxAccessTokenError error)
    {
        token = string.Empty;
        var trimmed = input?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            error = MapboxAccessTokenError.Empty;
            return false;
        }

        if (trimmed.StartsWith(SecretPrefix, StringComparison.Ordinal))
        {
            error = MapboxAccessTokenError.SecretToken;
            return false;
        }

        if (!trimmed.StartsWith(PublicPrefix, StringComparison.Ordinal)
            || trimmed.Length == PublicPrefix.Length
            || !IsUrlSafe(trimmed))
        {
            error = MapboxAccessTokenError.InvalidFormat;
            return false;
        }

        token = trimmed;
        error = MapboxAccessTokenError.None;
        return true;
    }

    // トークンは URL のクエリへそのまま載せるため、区切り文字や空白を含むものは受け付けない。
    private static bool IsUrlSafe(string value)
    {
        foreach (var c in value)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'))
            {
                return false;
            }
        }

        return true;
    }
}
