using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using PhotoGeoExplorer.Security;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Panes.Settings;

/// <summary>
/// 設定 Pane の Mapbox アクセストークン管理セクション。
/// トークンは <see cref="ISecretStore"/> にだけ保存し、設定ファイルには書かない。
/// </summary>
internal sealed class SettingsPaneMapboxSectionViewModel : BindableBase
{
    private readonly ISecretStore _secretStore;
    private readonly IMapboxConnectionTester _connectionTester;
    private readonly Action _tokenChanged;
    private string _tokenInput = string.Empty;
    private bool _hasToken;
    private bool _isTesting;
    private string _statusMessage = string.Empty;

    internal SettingsPaneMapboxSectionViewModel(
        ISecretStore secretStore,
        IMapboxConnectionTester connectionTester,
        Action tokenChanged)
    {
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        _connectionTester = connectionTester ?? throw new ArgumentNullException(nameof(connectionTester));
        _tokenChanged = tokenChanged ?? throw new ArgumentNullException(nameof(tokenChanged));

        SaveTokenCommand = new RelayCommand(() =>
        {
            SaveToken();
            return Task.CompletedTask;
        });
        RemoveTokenCommand = new RelayCommand(() =>
        {
            RemoveToken();
            return Task.CompletedTask;
        });
        TestConnectionCommand = new RelayCommand(() => TestConnectionAsync());
    }

    /// <summary>
    /// 入力欄の内容。保存に成功したら空に戻す（保存済みのトークンは画面へ戻さない）
    /// </summary>
    public string TokenInput
    {
        get => _tokenInput;
        set => SetProperty(ref _tokenInput, value ?? string.Empty);
    }

    public bool HasToken
    {
        get => _hasToken;
        private set
        {
            if (SetProperty(ref _hasToken, value))
            {
                OnPropertyChanged(nameof(TokenStateText));
            }
        }
    }

    public string TokenStateText => LocalizationService.GetString(
        HasToken ? "Mapbox.TokenStateSaved" : "Mapbox.TokenStateNone");

    public bool IsTesting
    {
        get => _isTesting;
        private set => SetProperty(ref _isTesting, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public ICommand SaveTokenCommand { get; }

    public ICommand RemoveTokenCommand { get; }

    public ICommand TestConnectionCommand { get; }

    internal void Refresh()
    {
        try
        {
            HasToken = !string.IsNullOrWhiteSpace(_secretStore.Get(SecretKeys.MapboxAccessToken));
        }
        catch (COMException ex)
        {
            AppLog.Error("Failed to read the Mapbox access token from the credential locker.", ex);
            HasToken = false;
            StatusMessage = LocalizationService.GetString("Mapbox.TokenStoreFailed");
        }
    }

    internal void SaveToken()
    {
        if (!MapboxAccessToken.TryParse(TokenInput, out var token, out var error))
        {
            StatusMessage = LocalizationService.GetString(GetErrorKey(error));
            return;
        }

        try
        {
            _secretStore.Set(SecretKeys.MapboxAccessToken, token);
        }
        catch (COMException ex)
        {
            AppLog.Error("Failed to save the Mapbox access token to the credential locker.", ex);
            StatusMessage = LocalizationService.GetString("Mapbox.TokenStoreFailed");
            return;
        }

        TokenInput = string.Empty;
        HasToken = true;
        StatusMessage = LocalizationService.GetString("Mapbox.TokenSaved");
        _tokenChanged();
    }

    internal void RemoveToken()
    {
        try
        {
            _secretStore.Remove(SecretKeys.MapboxAccessToken);
        }
        catch (COMException ex)
        {
            AppLog.Error("Failed to remove the Mapbox access token from the credential locker.", ex);
            StatusMessage = LocalizationService.GetString("Mapbox.TokenStoreFailed");
            return;
        }

        HasToken = false;
        StatusMessage = LocalizationService.GetString("Mapbox.TokenRemoved");
        _tokenChanged();
    }

    /// <summary>
    /// 入力中のトークンがあればそれを、なければ保存済みのトークンを試す
    /// </summary>
    internal async Task TestConnectionAsync()
    {
        if (IsTesting)
        {
            return;
        }

        string? token;
        if (string.IsNullOrWhiteSpace(TokenInput))
        {
            token = ReadStoredToken();
            if (token is null)
            {
                StatusMessage = LocalizationService.GetString("Mapbox.TokenEmpty");
                return;
            }
        }
        else if (!MapboxAccessToken.TryParse(TokenInput, out token, out var error))
        {
            StatusMessage = LocalizationService.GetString(GetErrorKey(error));
            return;
        }

        IsTesting = true;
        StatusMessage = LocalizationService.GetString("Mapbox.TestRunning");
        try
        {
            var result = await _connectionTester.TestAsync(token, CancellationToken.None).ConfigureAwait(true);
            StatusMessage = LocalizationService.GetString(result switch
            {
                MapboxConnectionResult.Success => "Mapbox.TestSucceeded",
                MapboxConnectionResult.AuthenticationFailed => "Mapbox.TestAuthFailed",
                _ => "Mapbox.TestNetworkError"
            });
        }
        finally
        {
            IsTesting = false;
        }
    }

    private string? ReadStoredToken()
    {
        try
        {
            var token = _secretStore.Get(SecretKeys.MapboxAccessToken);
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }
        catch (COMException ex)
        {
            AppLog.Error("Failed to read the Mapbox access token from the credential locker.", ex);
            return null;
        }
    }

    private static string GetErrorKey(MapboxAccessTokenError error)
    {
        return error switch
        {
            MapboxAccessTokenError.SecretToken => "Mapbox.TokenSecretRejected",
            MapboxAccessTokenError.InvalidFormat => "Mapbox.TokenInvalidFormat",
            _ => "Mapbox.TokenEmpty"
        };
    }
}
