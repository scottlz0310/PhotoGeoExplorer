using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Panes.Settings;

/// <summary>
/// 設定Paneの ViewModel
/// 実際の設定機能を実装
/// </summary>
internal sealed class SettingsPaneViewModel : PaneViewModelBase
{
    private readonly SettingsPaneService _service;
    private string? _language;
    private ThemePreference _theme = ThemePreference.System;
    private int _mapDefaultZoomLevel = 14;
    private MapTileSourceType _mapTileSource = MapTileSourceType.OpenStreetMap;
    private bool _autoCheckUpdates = true;
    private bool _showImagesOnly = true;
    private bool _showQuickStartOnStartup;
    private string? _lastFolderPath;
    private bool _isDirty;

    public SettingsPaneViewModel()
        : this(new SettingsPaneService())
    {
    }

    internal SettingsPaneViewModel(SettingsPaneService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        Title = "Settings";
        SaveCommand = new RelayCommand(async () => await SaveAsync().ConfigureAwait(false), () => IsDirty);
        ResetCommand = new RelayCommand(async () => await ResetAsync().ConfigureAwait(false));
        ExportCommand = new RelayCommand<string>(async (filePath) => await ExportAsync(filePath).ConfigureAwait(false));
        ImportCommand = new RelayCommand<string>(async (filePath) => await ImportAsync(filePath).ConfigureAwait(false));
    }

    /// <summary>
    /// 言語設定（null = System Default）
    /// </summary>
    public string? Language
    {
        get => _language;
        set
        {
            if (SetProperty(ref _language, value))
            {
                MarkDirty();
            }
        }
    }

    /// <summary>
    /// テーマ設定
    /// </summary>
    public ThemePreference Theme
    {
        get => _theme;
        set
        {
            if (SetProperty(ref _theme, value))
            {
                MarkDirty();
            }
        }
    }

    /// <summary>
    /// 地図のデフォルトズームレベル
    /// </summary>
    public int MapDefaultZoomLevel
    {
        get => _mapDefaultZoomLevel;
        set
        {
            if (SetProperty(ref _mapDefaultZoomLevel, value))
            {
                MarkDirty();
            }
        }
    }

    /// <summary>
    /// 地図のタイルソース
    /// </summary>
    public MapTileSourceType MapTileSource
    {
        get => _mapTileSource;
        set
        {
            if (SetProperty(ref _mapTileSource, value))
            {
                MarkDirty();
            }
        }
    }

    /// <summary>
    /// 自動更新チェック
    /// </summary>
    public bool AutoCheckUpdates
    {
        get => _autoCheckUpdates;
        set
        {
            if (SetProperty(ref _autoCheckUpdates, value))
            {
                MarkDirty();
            }
        }
    }

    /// <summary>
    /// 画像のみ表示
    /// </summary>
    public bool ShowImagesOnly
    {
        get => _showImagesOnly;
        set
        {
            if (SetProperty(ref _showImagesOnly, value))
            {
                MarkDirty();
            }
        }
    }

    /// <summary>
    /// 起動時にクイックスタートを表示
    /// </summary>
    public bool ShowQuickStartOnStartup
    {
        get => _showQuickStartOnStartup;
        set
        {
            if (SetProperty(ref _showQuickStartOnStartup, value))
            {
                MarkDirty();
            }
        }
    }

    /// <summary>
    /// 最後に開いたフォルダパス
    /// </summary>
    public string? LastFolderPath
    {
        get => _lastFolderPath;
        set
        {
            if (SetProperty(ref _lastFolderPath, value))
            {
                MarkDirty();
            }
        }
    }

    /// <summary>
    /// 設定が変更されているかどうか
    /// </summary>
    public bool IsDirty
    {
        get => _isDirty;
        private set => SetProperty(ref _isDirty, value);
    }

    /// <summary>
    /// 変更状態の表示用 Visibility
    /// </summary>
    public Visibility IsDirtyVisibility => IsDirty ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// 保存コマンド
    /// </summary>
    public ICommand SaveCommand { get; }

    /// <summary>
    /// リセットコマンド
    /// </summary>
    public ICommand ResetCommand { get; }

    /// <summary>
    /// エクスポートコマンド
    /// </summary>
    public ICommand ExportCommand { get; }

    /// <summary>
    /// インポートコマンド
    /// </summary>
    public ICommand ImportCommand { get; }

    protected override async Task OnInitializeAsync()
    {
        try
        {
            var settings = await _service.LoadSettingsAsync().ConfigureAwait(false);
            
            // UI スレッドで設定を適用
            var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            if (dispatcherQueue is null)
            {
                // テスト環境またはバックグラウンドスレッドの場合
                ApplySettings(settings);
                IsDirty = false;
                return;
            }

            var tcs = new TaskCompletionSource<bool>();
            dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    ApplySettings(settings);
                    IsDirty = false;
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            await tcs.Task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to load settings in SettingsPaneViewModel.", ex);
        }
    }

    protected override void OnCleanup()
    {
        // クリーンアップ処理
    }

    protected override void OnActiveChanged()
    {
        // Paneがアクティブになったときの処理
        if (IsActive)
        {
            // 必要に応じて設定を再読み込み
        }
    }

    private void ApplySettings(AppSettings settings)
    {
        Language = settings.Language;
        Theme = settings.Theme;
        MapDefaultZoomLevel = settings.MapDefaultZoomLevel;
        MapTileSource = settings.MapTileSource;
        AutoCheckUpdates = settings.AutoCheckUpdates;
        ShowImagesOnly = settings.ShowImagesOnly;
        ShowQuickStartOnStartup = settings.ShowQuickStartOnStartup;
        LastFolderPath = settings.LastFolderPath;
    }

    private AppSettings BuildSettings()
    {
        return new AppSettings
        {
            Language = Language,
            Theme = Theme,
            MapDefaultZoomLevel = MapDefaultZoomLevel,
            MapTileSource = MapTileSource,
            AutoCheckUpdates = AutoCheckUpdates,
            ShowImagesOnly = ShowImagesOnly,
            ShowQuickStartOnStartup = ShowQuickStartOnStartup,
            LastFolderPath = LastFolderPath
        };
    }

    private void MarkDirty()
    {
        IsDirty = true;
        OnPropertyChanged(nameof(IsDirtyVisibility));
        (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private async Task SaveAsync()
    {
        try
        {
            var settings = BuildSettings();
            await _service.SaveSettingsAsync(settings).ConfigureAwait(false);
            
            // UI スレッドで状態を更新
            var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            if (dispatcherQueue is null)
            {
                // テスト環境またはバックグラウンドスレッドの場合
                IsDirty = false;
                OnPropertyChanged(nameof(IsDirtyVisibility));
                (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
            else
            {
                var tcs = new TaskCompletionSource<bool>();
                dispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        IsDirty = false;
                        OnPropertyChanged(nameof(IsDirtyVisibility));
                        (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
                        tcs.SetResult(true);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                });
                await tcs.Task.ConfigureAwait(false);
            }
            
            AppLog.Info("Settings saved successfully.");
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to save settings.", ex);
        }
    }

    private async Task ResetAsync()
    {
        try
        {
            var defaultSettings = _service.CreateDefaultSettings();
            
            // UI スレッドで設定を適用
            var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            if (dispatcherQueue is null)
            {
                // テスト環境またはバックグラウンドスレッドの場合
                ApplySettings(defaultSettings);
                IsDirty = true;
                OnPropertyChanged(nameof(IsDirtyVisibility));
                (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
            else
            {
                var tcs = new TaskCompletionSource<bool>();
                dispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        ApplySettings(defaultSettings);
                        IsDirty = true;
                        OnPropertyChanged(nameof(IsDirtyVisibility));
                        (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
                        tcs.SetResult(true);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                });
                await tcs.Task.ConfigureAwait(false);
            }
            
            AppLog.Info("Settings reset to defaults.");
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to reset settings.", ex);
        }
    }

    private async Task ExportAsync(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        try
        {
            var settings = BuildSettings();
            await _service.ExportSettingsAsync(settings, filePath).ConfigureAwait(false);
            AppLog.Info($"Settings exported to {filePath}");
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to export settings to {filePath}", ex);
        }
    }

    private async Task ImportAsync(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        try
        {
            var settings = await _service.ImportSettingsAsync(filePath).ConfigureAwait(false);
            if (settings is not null)
            {
                // UI スレッドで設定を適用
                var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
                if (dispatcherQueue is null)
                {
                    // テスト環境またはバックグラウンドスレッドの場合
                    ApplySettings(settings);
                    MarkDirty();
                }
                else
                {
                    var tcs = new TaskCompletionSource<bool>();
                    dispatcherQueue.TryEnqueue(() =>
                    {
                        try
                        {
                            ApplySettings(settings);
                            MarkDirty();
                            tcs.SetResult(true);
                        }
                        catch (Exception ex)
                        {
                            tcs.SetException(ex);
                        }
                    });
                    await tcs.Task.ConfigureAwait(false);
                }
                
                AppLog.Info($"Settings imported from {filePath}");
            }
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to import settings from {filePath}", ex);
        }
    }
}
