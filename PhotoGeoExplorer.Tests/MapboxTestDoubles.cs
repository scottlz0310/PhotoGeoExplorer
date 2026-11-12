using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using PhotoGeoExplorer.Services;

namespace PhotoGeoExplorer.Tests;

internal sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    private Exception? _failWith;

    // PasswordVault の失敗（COMException）を再現する。HRESULT は ERROR_INTERNAL_ERROR。
    public void FailWithVaultError()
        => _failWith = Marshal.GetExceptionForHR(unchecked((int)0x8007054F));

    public string? Get(string key)
    {
        ThrowIfConfigured();
        return _values.TryGetValue(key, out var value) ? value : null;
    }

    public void Set(string key, string value)
    {
        ThrowIfConfigured();
        _values[key] = value;
    }

    public void Remove(string key)
    {
        ThrowIfConfigured();
        _values.Remove(key);
    }

    private void ThrowIfConfigured()
    {
        if (_failWith is not null)
        {
            throw _failWith;
        }
    }
}

internal sealed class FakeMapboxConnectionTester : IMapboxConnectionTester
{
    public MapboxConnectionResult Result { get; set; } = MapboxConnectionResult.Success;

    public List<string> TestedTokens { get; } = [];

    public Task<MapboxConnectionResult> TestAsync(string accessToken, CancellationToken cancellationToken)
    {
        TestedTokens.Add(accessToken);
        return Task.FromResult(Result);
    }
}

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public StubHttpMessageHandler(HttpStatusCode statusCode)
        : this(_ => new HttpResponseMessage(statusCode) { Content = new ByteArrayContent([1, 2, 3]) })
    {
    }

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _respond = respond;
    }

    public List<Uri?> RequestUris { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequestUris.Add(request.RequestUri);
        return Task.FromResult(_respond(request));
    }
}
