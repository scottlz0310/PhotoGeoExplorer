using System.Threading.Tasks;
using PhotoGeoExplorer.Panes.Settings;
using PhotoGeoExplorer.Services;

namespace PhotoGeoExplorer.Tests;

public sealed class SettingsPaneMapboxSectionViewModelTests
{
    private const string PublicToken = "pk.public-token";
    private readonly InMemorySecretStore _secretStore = new();
    private readonly FakeMapboxConnectionTester _connectionTester = new();
    private int _tokenChangedCount;

    [Fact]
    public void SaveTokenStoresTrimmedTokenAndNotifies()
    {
        var viewModel = CreateViewModel();
        viewModel.TokenInput = "  " + PublicToken + "  ";

        viewModel.SaveToken();

        Assert.Equal(PublicToken, _secretStore.Get(SecretKeys.MapboxAccessToken));
        Assert.True(viewModel.HasToken);
        Assert.Equal(string.Empty, viewModel.TokenInput);
        Assert.Equal("Mapbox.TokenSaved", viewModel.StatusMessage);
        Assert.Equal(1, _tokenChangedCount);
    }

    [Theory]
    [InlineData("sk.secret-token", "Mapbox.TokenSecretRejected")]
    [InlineData("not-a-token", "Mapbox.TokenInvalidFormat")]
    [InlineData("", "Mapbox.TokenEmpty")]
    [InlineData("   ", "Mapbox.TokenEmpty")]
    public void SaveTokenRejectsInvalidInput(string input, string expectedStatus)
    {
        var viewModel = CreateViewModel();
        viewModel.TokenInput = input;

        viewModel.SaveToken();

        Assert.Null(_secretStore.Get(SecretKeys.MapboxAccessToken));
        Assert.False(viewModel.HasToken);
        Assert.Equal(expectedStatus, viewModel.StatusMessage);
        Assert.Equal(0, _tokenChangedCount);
    }

    [Fact]
    public void RejectedInputKeepsPreviouslySavedToken()
    {
        _secretStore.Set(SecretKeys.MapboxAccessToken, PublicToken);
        var viewModel = CreateViewModel();
        viewModel.Refresh();
        viewModel.TokenInput = "sk.secret-token";

        viewModel.SaveToken();

        Assert.Equal(PublicToken, _secretStore.Get(SecretKeys.MapboxAccessToken));
        Assert.True(viewModel.HasToken);
    }

    [Fact]
    public void RemoveTokenClearsStoreAndNotifies()
    {
        _secretStore.Set(SecretKeys.MapboxAccessToken, PublicToken);
        var viewModel = CreateViewModel();
        viewModel.Refresh();
        Assert.True(viewModel.HasToken);

        viewModel.RemoveToken();

        Assert.Null(_secretStore.Get(SecretKeys.MapboxAccessToken));
        Assert.False(viewModel.HasToken);
        Assert.Equal("Mapbox.TokenRemoved", viewModel.StatusMessage);
        Assert.Equal(1, _tokenChangedCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StoreFailureIsReportedWithoutChangingState(bool save)
    {
        var viewModel = CreateViewModel();
        viewModel.TokenInput = PublicToken;
        _secretStore.FailWithVaultError();

        if (save)
        {
            viewModel.SaveToken();
        }
        else
        {
            viewModel.RemoveToken();
        }

        Assert.False(viewModel.HasToken);
        Assert.Equal("Mapbox.TokenStoreFailed", viewModel.StatusMessage);
        Assert.Equal(0, _tokenChangedCount);
    }

    [Theory]
    [InlineData(MapboxConnectionResult.Success, "Mapbox.TestSucceeded")]
    [InlineData(MapboxConnectionResult.AuthenticationFailed, "Mapbox.TestAuthFailed")]
    [InlineData(MapboxConnectionResult.NetworkError, "Mapbox.TestNetworkError")]
    internal async Task TestConnectionReportsResult(MapboxConnectionResult result, string expectedStatus)
    {
        _connectionTester.Result = result;
        var viewModel = CreateViewModel();
        viewModel.TokenInput = PublicToken;

        await viewModel.TestConnectionAsync().ConfigureAwait(true);

        Assert.Equal([PublicToken], _connectionTester.TestedTokens);
        Assert.Equal(expectedStatus, viewModel.StatusMessage);
        Assert.False(viewModel.IsTesting);
        // 接続テストは保存しない
        Assert.Null(_secretStore.Get(SecretKeys.MapboxAccessToken));
    }

    [Fact]
    public async Task TestConnectionUsesStoredTokenWhenInputIsEmpty()
    {
        _secretStore.Set(SecretKeys.MapboxAccessToken, PublicToken);
        var viewModel = CreateViewModel();

        await viewModel.TestConnectionAsync().ConfigureAwait(true);

        Assert.Equal([PublicToken], _connectionTester.TestedTokens);
        Assert.Equal("Mapbox.TestSucceeded", viewModel.StatusMessage);
    }

    [Theory]
    [InlineData("", "Mapbox.TokenEmpty")]
    [InlineData("sk.secret-token", "Mapbox.TokenSecretRejected")]
    public async Task TestConnectionDoesNotSendInvalidOrMissingToken(string input, string expectedStatus)
    {
        var viewModel = CreateViewModel();
        viewModel.TokenInput = input;

        await viewModel.TestConnectionAsync().ConfigureAwait(true);

        Assert.Empty(_connectionTester.TestedTokens);
        Assert.Equal(expectedStatus, viewModel.StatusMessage);
    }

    private SettingsPaneMapboxSectionViewModel CreateViewModel()
        => new(_secretStore, _connectionTester, () => _tokenChangedCount++);
}
