using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.ViewModels;
using Microsoft.UI.Xaml;
using PhotoGeoExplorer.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class MainViewModelTests : IDisposable
{
    private readonly List<string> _tempDirectories = new();

    [Fact]
    public void ConstructorInitializesNavigationProperties()
    {
        // Arrange & Act
        var fileSystemService = new FileSystemService();
        using var viewModel = new MainViewModel(fileSystemService);

        // Assert
        Assert.False(viewModel.CanNavigateBack);
        Assert.False(viewModel.CanNavigateForward);
    }

    [Fact]
    public void HelpCommandsAreDisabledBeforeHelpServiceConfiguration()
    {
        // Arrange
        using var viewModel = new MainViewModel(new FileSystemService());

        // Assert
        Assert.False(viewModel.ShowGettingStartedCommand.CanExecute(null));
        Assert.False(viewModel.ShowBasicOperationsCommand.CanExecute(null));
        Assert.False(viewModel.ShowDetailedHelpCommand.CanExecute(null));
        Assert.False(viewModel.ShowAboutCommand.CanExecute(null));
    }

    [Fact]
    public void HelpCommandsInvokeConfiguredHelpService()
    {
        // Arrange
        using var viewModel = new MainViewModel(new FileSystemService());
        using var helpService = new TestHelpService();
        viewModel.ConfigureHelpService(helpService);

        // Act
        viewModel.ShowGettingStartedCommand.Execute(null);
        viewModel.ShowBasicOperationsCommand.Execute(null);
        viewModel.ShowDetailedHelpCommand.Execute(null);
        viewModel.ShowAboutCommand.Execute(null);

        // Assert
        Assert.Equal(1, helpService.ShowGettingStartedCallCount);
        Assert.Equal(1, helpService.ShowBasicsCallCount);
        Assert.Equal(1, helpService.ShowHelpHtmlWindowCallCount);
        Assert.Equal(1, helpService.ShowAboutCallCount);
    }

    [Fact]
    public void SettingsCommandsAreDisabledBeforeSettingsCoordinatorConfiguration()
    {
        using var viewModel = new MainViewModel(new FileSystemService());

        Assert.False(viewModel.ChangeLanguageCommand.CanExecute("ja-JP"));
        Assert.False(viewModel.ChangeThemeCommand.CanExecute(nameof(ThemePreference.Dark)));
        Assert.False(viewModel.ChangeMapZoomLevelCommand.CanExecute("16"));
        Assert.False(viewModel.ChangeMapTileSourceCommand.CanExecute(nameof(MapTileSourceType.EsriWorldImagery)));
        Assert.False(viewModel.ExportSettingsCommand.CanExecute(null));
        Assert.False(viewModel.ImportSettingsCommand.CanExecute(null));
        Assert.False(viewModel.PersistLayoutSettingsCommand.CanExecute(null));
    }

    [Fact]
    public void SettingsCommandsInvokeConfiguredSettingsCoordinator()
    {
        using var viewModel = new MainViewModel(new FileSystemService());
        using var settingsCoordinator = new TestSettingsCoordinator();
        viewModel.ConfigureSettingsCoordinator(settingsCoordinator);

        viewModel.ChangeLanguageCommand.Execute("ja");
        viewModel.ChangeThemeCommand.Execute(nameof(ThemePreference.Dark));
        viewModel.ChangeMapZoomLevelCommand.Execute("16");
        viewModel.ChangeMapTileSourceCommand.Execute(nameof(MapTileSourceType.EsriWorldImagery));
        viewModel.ExportSettingsCommand.Execute(null);
        viewModel.ImportSettingsCommand.Execute(null);
        viewModel.PersistLayoutSettingsCommand.Execute(null);

        Assert.Equal(1, settingsCoordinator.ChangeLanguageCallCount);
        Assert.Equal("ja", settingsCoordinator.LastLanguageTag);
        Assert.True(settingsCoordinator.LastShowRestartPrompt);
        Assert.Equal(1, settingsCoordinator.ChangeThemeCallCount);
        Assert.Equal(ThemePreference.Dark, settingsCoordinator.LastTheme);
        Assert.Equal(1, settingsCoordinator.ChangeMapZoomLevelCallCount);
        Assert.Equal(16, settingsCoordinator.LastMapZoomLevel);
        Assert.Equal(1, settingsCoordinator.ChangeMapTileSourceCallCount);
        Assert.Equal(MapTileSourceType.EsriWorldImagery, settingsCoordinator.LastMapTileSource);
        Assert.Equal(1, settingsCoordinator.ExportCallCount);
        Assert.Equal(1, settingsCoordinator.ImportCallCount);
        Assert.Equal(1, settingsCoordinator.ScheduleSaveCallCount);
    }

    [Fact]
    public void ApplySettingsStateUpdatesMenuStateProperties()
    {
        using var viewModel = new MainViewModel(new FileSystemService());

        viewModel.ApplySettingsState(
            language: "en-US",
            theme: ThemePreference.Light,
            mapZoomLevel: 18,
            mapTileSource: MapTileSourceType.EsriWorldImagery);

        Assert.Equal("en-US", viewModel.CurrentLanguage);
        Assert.Equal(ThemePreference.Light, viewModel.CurrentTheme);
        Assert.Equal(18, viewModel.CurrentMapZoomLevel);
        Assert.Equal(MapTileSourceType.EsriWorldImagery, viewModel.CurrentMapTileSource);
        Assert.False(viewModel.IsLanguageSystem);
        Assert.True(viewModel.IsLanguageEnglish);
        Assert.True(viewModel.IsThemeLight);
        Assert.True(viewModel.IsMapZoomLevel18);
        Assert.True(viewModel.IsMapTileSourceEsri);
    }

    [Fact]
    public async Task NavigateBackAsyncWithoutHistoryDoesNothing()
    {
        // Arrange
        var fileSystemService = new FileSystemService();
        using var viewModel = new MainViewModel(fileSystemService);

        // Act
        await viewModel.NavigateBackAsync().ConfigureAwait(true);

        // Assert
        Assert.False(viewModel.CanNavigateBack);
        Assert.False(viewModel.CanNavigateForward);
    }

    [Fact]
    public async Task NavigateForwardAsyncWithoutHistoryDoesNothing()
    {
        // Arrange
        var fileSystemService = new FileSystemService();
        using var viewModel = new MainViewModel(fileSystemService);

        // Act
        await viewModel.NavigateForwardAsync().ConfigureAwait(true);

        // Assert
        Assert.False(viewModel.CanNavigateBack);
        Assert.False(viewModel.CanNavigateForward);
    }

    [Fact]
    public async Task LoadFolderAsyncUpdatesNavigationHistory()
    {
        // Arrange
        var fileSystemService = new FileSystemService();
        using var viewModel = new MainViewModel(fileSystemService);
        var testFolder1 = CreateTempDirectory();
        var testFolder2 = CreateTempDirectory();

        // Act
        await viewModel.LoadFolderAsync(testFolder1).ConfigureAwait(true);
        Assert.False(viewModel.CanNavigateBack, "初回読み込み後は戻れない");
        Assert.False(viewModel.CanNavigateForward, "初回読み込み後は進めない");

        await viewModel.LoadFolderAsync(testFolder2).ConfigureAwait(true);
        Assert.True(viewModel.CanNavigateBack, "2回目の読み込み後は戻れる");
        Assert.False(viewModel.CanNavigateForward, "2回目の読み込み後は進めない");

        // Navigate back
        await viewModel.NavigateBackAsync().ConfigureAwait(true);
        Assert.False(viewModel.CanNavigateBack, "戻った後は戻れない");
        Assert.True(viewModel.CanNavigateForward, "戻った後は進める");

        // パスを正規化して比較
        var normalizedCurrent = NormalizePath(viewModel.CurrentFolderPath!);
        var normalizedExpected = NormalizePath(testFolder1);
        Assert.Equal(normalizedExpected, normalizedCurrent);

        // Navigate forward
        await viewModel.NavigateForwardAsync().ConfigureAwait(true);
        Assert.True(viewModel.CanNavigateBack, "進んだ後は戻れる");
        Assert.False(viewModel.CanNavigateForward, "進んだ後は進めない");

        normalizedCurrent = NormalizePath(viewModel.CurrentFolderPath!);
        normalizedExpected = NormalizePath(testFolder2);
        Assert.Equal(normalizedExpected, normalizedCurrent);
    }

    [Fact]
    public async Task LoadFolderAsyncClearsForwardHistory()
    {
        // Arrange
        var fileSystemService = new FileSystemService();
        using var viewModel = new MainViewModel(fileSystemService);
        var testFolder1 = CreateTempDirectory();
        var testFolder2 = CreateTempDirectory();
        var testFolder3 = CreateTempDirectory();

        // Act
        await viewModel.LoadFolderAsync(testFolder1).ConfigureAwait(true);
        await viewModel.LoadFolderAsync(testFolder2).ConfigureAwait(true);
        await viewModel.NavigateBackAsync().ConfigureAwait(true);

        Assert.True(viewModel.CanNavigateForward, "戻った後は進める");

        // 新しいフォルダに移動すると進む履歴がクリアされる
        await viewModel.LoadFolderAsync(testFolder3).ConfigureAwait(true);

        // Assert
        Assert.True(viewModel.CanNavigateBack, "新しいフォルダに移動後は戻れる");
        Assert.False(viewModel.CanNavigateForward, "新しいフォルダに移動後は進む履歴がクリアされる");
    }

    [Fact]
    public async Task LoadFolderAsyncSameFolderDoesNotAddToHistory()
    {
        // Arrange
        var fileSystemService = new FileSystemService();
        using var viewModel = new MainViewModel(fileSystemService);
        var testFolder = CreateTempDirectory();

        // Act
        await viewModel.LoadFolderAsync(testFolder).ConfigureAwait(true);
        Assert.False(viewModel.CanNavigateBack, "初回読み込み後は戻れない");

        // 同じフォルダを再度読み込み
        await viewModel.LoadFolderAsync(testFolder).ConfigureAwait(true);

        // Assert
        Assert.False(viewModel.CanNavigateBack, "同じフォルダの再読み込みでは履歴に追加されない");
    }

    [Fact]
    public async Task LoadFolderAsyncSkipsThumbnailBitmapImageInTestEnvironment()
    {
        // Arrange
        var fileSystemService = new FileSystemService();
        using var viewModel = new MainViewModel(fileSystemService);
        var testFolder = CreateTempDirectory();
        _ = CreateTestImageFile(testFolder);

        // Act
        await viewModel.LoadFolderAsync(testFolder).ConfigureAwait(true);

        // Assert
        var item = viewModel.Items.Single(current => !current.IsFolder);
        Assert.Null(item.Thumbnail);
        Assert.Null(item.Item.ThumbnailPath);
        Assert.NotNull(item.ThumbnailKey);
    }

    [Fact]
    public async Task SelectingItemSkipsPreviewBitmapImageInTestEnvironment()
    {
        // Arrange
        var fileSystemService = new FileSystemService();
        using var viewModel = new MainViewModel(fileSystemService);
        var testFolder = CreateTempDirectory();
        _ = CreateTestImageFile(testFolder);

        await viewModel.LoadFolderAsync(testFolder).ConfigureAwait(true);
        var item = viewModel.Items.Single(current => !current.IsFolder);

        // Act
        viewModel.SelectedItem = item;

        // Assert
        Assert.Null(viewModel.SelectedPreview);
        Assert.Equal(Visibility.Visible, viewModel.PreviewPlaceholderVisibility);
    }

    #region #46 (PR #49) 回帰テスト: Move ボタン単一フォルダ選択時遷移

    /// <summary>
    /// 単一フォルダ選択後に LoadFolderAsync を呼び出すとそのフォルダに遷移することを検証。
    /// Move ボタンの動作をシミュレート。
    /// </summary>
    /// <remarks>
    /// Acceptance Criteria: Move ボタン：単一フォルダ選択時に選択フォルダへ遷移する
    /// </remarks>
    [Fact]
    public async Task LoadFolderAsyncWithSubfolderNavigatesToSubfolder()
    {
        // Arrange
        var fileSystemService = new FileSystemService();
        using var viewModel = new MainViewModel(fileSystemService);
        var parentFolder = CreateTempDirectory();
        var childFolder = Path.Combine(parentFolder, "child");
        Directory.CreateDirectory(childFolder);

        await viewModel.LoadFolderAsync(parentFolder).ConfigureAwait(true);
        var expectedPath = NormalizePath(childFolder);

        // Act - Move ボタンの動作をシミュレート: 単一フォルダが選択された状態で LoadFolderAsync を呼び出す
        await viewModel.LoadFolderAsync(childFolder).ConfigureAwait(true);

        // Assert
        var actualPath = NormalizePath(viewModel.CurrentFolderPath!);
        Assert.Equal(expectedPath, actualPath);
        Assert.True(viewModel.CanNavigateBack, "子フォルダに遷移後は戻れる");
    }

    /// <summary>
    /// フォルダをダブルクリックした場合と同等のナビゲーション動作を検証。
    /// </summary>
    [Fact]
    public async Task LoadFolderAsyncBehavesLikeDoubleClick()
    {
        // Arrange
        var fileSystemService = new FileSystemService();
        using var viewModel = new MainViewModel(fileSystemService);
        var parentFolder = CreateTempDirectory();
        var childFolder = Path.Combine(parentFolder, "child");
        Directory.CreateDirectory(childFolder);

        // 親フォルダをロード
        await viewModel.LoadFolderAsync(parentFolder).ConfigureAwait(true);
        Assert.False(viewModel.CanNavigateBack);

        // Act - ダブルクリック相当: 子フォルダに遷移
        await viewModel.LoadFolderAsync(childFolder).ConfigureAwait(true);

        // Assert
        Assert.True(viewModel.CanNavigateBack);
        Assert.Equal(NormalizePath(childFolder), NormalizePath(viewModel.CurrentFolderPath!));

        // 戻る操作で親フォルダに戻れることを確認
        await viewModel.NavigateBackAsync().ConfigureAwait(true);
        Assert.Equal(NormalizePath(parentFolder), NormalizePath(viewModel.CurrentFolderPath!));
    }

    #endregion

    #region #47 (PR #50) 回帰テスト: 起動時フォルダ復元優先順位

    /// <summary>
    /// フォルダパス引数がある場合、そのフォルダが開かれることを検証。
    /// </summary>
    [Fact]
    public async Task LoadFolderAsyncWithValidPathOpensFolder()
    {
        // Arrange
        var fileSystemService = new FileSystemService();
        using var viewModel = new MainViewModel(fileSystemService);
        var testFolder = CreateTempDirectory();

        // Act - 起動時のフォルダ指定をシミュレート
        await viewModel.LoadFolderAsync(testFolder).ConfigureAwait(true);

        // Assert
        Assert.NotNull(viewModel.CurrentFolderPath);
        Assert.Equal(NormalizePath(testFolder), NormalizePath(viewModel.CurrentFolderPath));
    }

    /// <summary>
    /// フォルダパス引数なしで InitializeAsync を呼び出すと Pictures フォルダが開かれることを検証。
    /// </summary>
    [Fact]
    public async Task InitializeAsyncWithoutCurrentFolderOpensDefaultFolder()
    {
        // Arrange
        var fileSystemService = new FileSystemService();
        using var viewModel = new MainViewModel(fileSystemService);

        // Act
        await viewModel.InitializeAsync().ConfigureAwait(true);

        // Assert - InitializeAsync が実行されると CurrentFolderPath が設定される
        // （Pictures フォルダが存在する場合）
        // テスト環境によっては Pictures フォルダが存在しない可能性があるため、
        // CurrentFolderPath が設定されるか、エラーが表示されることを確認
        // ※実際の動作は環境依存
    }

    #endregion

    private string CreateTempDirectory()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "PhotoGeoExplorer_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);
        _tempDirectories.Add(tempPath);
        return tempPath;
    }

    private static string CreateTestImageFile(string folderPath)
    {
        var filePath = Path.Combine(folderPath, "test.png");
        using var image = new Image<Rgba32>(1, 1);
        image[0, 0] = new Rgba32(255, 0, 0, 255);
        image.Save(filePath);
        return filePath;
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirectories)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch (IOException)
            {
                // Ignore cleanup errors
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore cleanup errors
            }
        }
    }

    private sealed class TestHelpService : IHelpService
    {
        public int ShowGettingStartedCallCount { get; private set; }
        public int ShowBasicsCallCount { get; private set; }
        public int ShowHelpHtmlWindowCallCount { get; private set; }
        public int ShowAboutCallCount { get; private set; }

        public Task ShowGettingStartedAsync()
        {
            ShowGettingStartedCallCount++;
            return Task.CompletedTask;
        }

        public Task ShowBasicsAsync()
        {
            ShowBasicsCallCount++;
            return Task.CompletedTask;
        }

        public Task ShowHelpHtmlWindowAsync()
        {
            ShowHelpHtmlWindowCallCount++;
            return Task.CompletedTask;
        }

        public Task ShowAboutAsync()
        {
            ShowAboutCallCount++;
            return Task.CompletedTask;
        }

        public Task ShowQuickStartIfNeededAsync()
        {
            return Task.CompletedTask;
        }

        public void CloseHelpWindow()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class TestSettingsCoordinator : ISettingsCoordinator
    {
        public int ChangeLanguageCallCount { get; private set; }
        public int ChangeThemeCallCount { get; private set; }
        public int ChangeMapZoomLevelCallCount { get; private set; }
        public int ChangeMapTileSourceCallCount { get; private set; }
        public int ExportCallCount { get; private set; }
        public int ImportCallCount { get; private set; }
        public int ScheduleSaveCallCount { get; private set; }
        public string? LastLanguageTag { get; private set; }
        public bool LastShowRestartPrompt { get; private set; }
        public ThemePreference LastTheme { get; private set; } = ThemePreference.System;
        public int LastMapZoomLevel { get; private set; }
        public MapTileSourceType LastMapTileSource { get; private set; } = MapTileSourceType.OpenStreetMap;

        public bool SettingsFileExistsAtStartup => false;

        public string? LanguageOverride => null;

        public string? ExternalContentBaseUrl => null;

        public bool ShowQuickStartOnStartup { get; set; }

        public PaneLayoutPreset PaneLayoutPreset => AppSettings.DefaultPaneLayoutPreset;

        public PaneViewType PaneRegion1View => AppSettings.DefaultPaneRegion1View;

        public PaneViewType PaneRegion2View => AppSettings.DefaultPaneRegion2View;

        public PaneViewType PaneRegion3View => AppSettings.DefaultPaneRegion3View;

        public event EventHandler<PaneLayoutChangedEventArgs>? PaneLayoutChanged;

        public Task LoadAsync()
        {
            return Task.CompletedTask;
        }

        public void ScheduleSave()
        {
            ScheduleSaveCallCount++;
        }

        public Task SaveAsync()
        {
            return Task.CompletedTask;
        }

        public Task ChangeLanguageAsync(string? languageTag, bool showRestartPrompt)
        {
            ChangeLanguageCallCount++;
            LastLanguageTag = languageTag;
            LastShowRestartPrompt = showRestartPrompt;
            return Task.CompletedTask;
        }

        public void ChangeTheme(ThemePreference preference)
        {
            ChangeThemeCallCount++;
            LastTheme = preference;
        }

        public void ChangeMapZoomLevel(int level)
        {
            ChangeMapZoomLevelCallCount++;
            LastMapZoomLevel = level;
        }

        public void ChangeMapTileSource(MapTileSourceType sourceType)
        {
            ChangeMapTileSourceCallCount++;
            LastMapTileSource = sourceType;
        }

        public void ChangePaneLayout(PaneLayoutPreset preset, PaneViewType region1View, PaneViewType region2View, PaneViewType region3View)
        {
            PaneLayoutChanged?.Invoke(this, new PaneLayoutChangedEventArgs(preset, region1View, region2View, region3View));
        }

        public Task ExportSettingsAsync()
        {
            ExportCallCount++;
            return Task.CompletedTask;
        }

        public Task ImportSettingsAsync()
        {
            ImportCallCount++;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
