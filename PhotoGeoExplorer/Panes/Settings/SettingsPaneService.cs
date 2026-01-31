using System;
using System.Threading.Tasks;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;

namespace PhotoGeoExplorer.Panes.Settings;

/// <summary>
/// 設定Pane専用のサービス
/// I/O処理とビジネスロジックを分離
/// </summary>
internal sealed class SettingsPaneService : ISettingsPaneService
{
    private readonly SettingsService _settingsService;

    public SettingsPaneService()
        : this(new SettingsService())
    {
    }

    internal SettingsPaneService(SettingsService settingsService)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        _settingsService = settingsService;
    }

    /// <summary>
    /// 設定を読み込む
    /// </summary>
    public Task<AppSettings> LoadSettingsAsync()
    {
        return _settingsService.LoadAsync();
    }

    /// <summary>
    /// 設定を保存する
    /// </summary>
    public Task SaveSettingsAsync(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return _settingsService.SaveAsync(settings);
    }

    /// <summary>
    /// 設定をエクスポートする
    /// </summary>
    public Task ExportSettingsAsync(AppSettings settings, string filePath)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path is required.", nameof(filePath));
        }

        return SettingsService.ExportAsync(settings, filePath);
    }

    /// <summary>
    /// 設定をインポートする
    /// </summary>
    public Task<AppSettings?> ImportSettingsAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path is required.", nameof(filePath));
        }

        return SettingsService.ImportAsync(filePath);
    }

    /// <summary>
    /// デフォルト設定を作成する
    /// </summary>
    public AppSettings CreateDefaultSettings()
    {
        return new AppSettings();
    }
}
