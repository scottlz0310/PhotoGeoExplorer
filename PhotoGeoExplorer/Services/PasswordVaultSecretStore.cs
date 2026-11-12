using System;
using System.Runtime.InteropServices;
using Windows.Security.Credentials;

namespace PhotoGeoExplorer.Services;

/// <summary>
/// Windows 資格情報ロッカー（PasswordVault）による <see cref="ISecretStore"/> 実装。
/// </summary>
internal sealed class PasswordVaultSecretStore : ISecretStore
{
    private const string ResourceName = "PhotoGeoExplorer";

    // PasswordVault.Retrieve は該当なしを例外（ERROR_NOT_FOUND）で返す。
    private const int ElementNotFoundHresult = unchecked((int)0x80070490);

    public string? Get(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        var credential = Find(new PasswordVault(), key);
        if (credential is null)
        {
            return null;
        }

        credential.RetrievePassword();
        return credential.Password;
    }

    public void Set(string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentException.ThrowIfNullOrEmpty(value);

        new PasswordVault().Add(new PasswordCredential(ResourceName, key, value));
    }

    public void Remove(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        var vault = new PasswordVault();
        var credential = Find(vault, key);
        if (credential is not null)
        {
            vault.Remove(credential);
        }
    }

    private static PasswordCredential? Find(PasswordVault vault, string key)
    {
        try
        {
            return vault.Retrieve(ResourceName, key);
        }
        catch (COMException ex) when (ex.HResult == ElementNotFoundHresult)
        {
            return null;
        }
    }
}
