namespace PhotoGeoExplorer.Services;

/// <summary>
/// 機密値（アクセストークン等）の保存先。settings.json とは分離する。
/// </summary>
internal interface ISecretStore
{
    string? Get(string key);

    void Set(string key, string value);

    void Remove(string key);
}

internal static class SecretKeys
{
    internal const string MapboxAccessToken = "MapboxAccessToken";
}
