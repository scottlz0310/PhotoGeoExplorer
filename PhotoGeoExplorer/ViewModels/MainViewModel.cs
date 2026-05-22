using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;

namespace PhotoGeoExplorer.ViewModels;

internal sealed class MainViewModel : BindableBase
{
    private string? _notificationMessage;
    private InfoBarSeverity _notificationSeverity = InfoBarSeverity.Informational;
    private bool _isNotificationOpen;
    private string? _notificationActionLabel;
    private string? _notificationActionUrl;
    private Visibility _notificationActionVisibility = Visibility.Collapsed;
    private string? _currentLanguage;
    private ThemePreference _currentTheme = ThemePreference.System;
    private int _currentMapZoomLevel = 14;
    private MapTileSourceType _currentMapTileSource = MapTileSourceType.OpenStreetMap;
    private IHelpService? _helpService;
    private ISettingsCoordinator? _settingsCoordinator;

    public MainViewModel()
        : this(new State.WorkspaceState())
    {
    }

    internal MainViewModel(State.WorkspaceState workspaceState)
    {
        WorkspaceState = workspaceState ?? throw new ArgumentNullException(nameof(workspaceState));

        ShowGettingStartedCommand = new RelayCommand(
            () => ExecuteHelpActionAsync(helpService => helpService.ShowGettingStartedAsync()),
            CanExecuteHelpAction);
        ShowBasicOperationsCommand = new RelayCommand(
            () => ExecuteHelpActionAsync(helpService => helpService.ShowBasicsAsync()),
            CanExecuteHelpAction);
        ShowDetailedHelpCommand = new RelayCommand(
            () => ExecuteHelpActionAsync(helpService => helpService.ShowHelpHtmlWindowAsync()),
            CanExecuteHelpAction);
        ShowAboutCommand = new RelayCommand(
            () => ExecuteHelpActionAsync(helpService => helpService.ShowAboutAsync()),
            CanExecuteHelpAction);
        ChangeLanguageCommand = new RelayCommand<string>(
            parameter => ExecuteSettingsActionAsync(settingsCoordinator =>
                settingsCoordinator.ChangeLanguageAsync(parameter, showRestartPrompt: true)),
            _ => CanExecuteSettingsAction());
        ChangeThemeCommand = new RelayCommand<string>(
            parameter => ExecuteSettingsActionAsync(settingsCoordinator =>
            {
                if (parameter is null || !Enum.TryParse(parameter, ignoreCase: true, out ThemePreference theme))
                {
                    return Task.CompletedTask;
                }

                settingsCoordinator.ChangeTheme(theme);
                return Task.CompletedTask;
            }),
            _ => CanExecuteSettingsAction());
        ChangeMapZoomLevelCommand = new RelayCommand<string>(
            parameter => ExecuteSettingsActionAsync(settingsCoordinator =>
            {
                if (parameter is null || !int.TryParse(parameter, out var level))
                {
                    return Task.CompletedTask;
                }

                settingsCoordinator.ChangeMapZoomLevel(level);
                return Task.CompletedTask;
            }),
            _ => CanExecuteSettingsAction());
        ChangeMapTileSourceCommand = new RelayCommand<string>(
            parameter => ExecuteSettingsActionAsync(settingsCoordinator =>
            {
                if (parameter is null || !Enum.TryParse(parameter, ignoreCase: true, out MapTileSourceType sourceType))
                {
                    return Task.CompletedTask;
                }

                settingsCoordinator.ChangeMapTileSource(sourceType);
                return Task.CompletedTask;
            }),
            _ => CanExecuteSettingsAction());
        ExportSettingsCommand = new RelayCommand(
            () => ExecuteSettingsActionAsync(settingsCoordinator => settingsCoordinator.ExportSettingsAsync()),
            CanExecuteSettingsAction);
        ImportSettingsCommand = new RelayCommand(
            () => ExecuteSettingsActionAsync(settingsCoordinator => settingsCoordinator.ImportSettingsAsync()),
            CanExecuteSettingsAction);
        PersistLayoutSettingsCommand = new RelayCommand(
            () => ExecuteSettingsActionAsync(settingsCoordinator =>
            {
                settingsCoordinator.ScheduleSave();
                return Task.CompletedTask;
            }),
            CanExecuteSettingsAction);
    }

    public State.WorkspaceState WorkspaceState { get; }

    public string? NotificationMessage
    {
        get => _notificationMessage;
        private set => SetProperty(ref _notificationMessage, value);
    }

    public InfoBarSeverity NotificationSeverity
    {
        get => _notificationSeverity;
        private set => SetProperty(ref _notificationSeverity, value);
    }

    public bool IsNotificationOpen
    {
        get => _isNotificationOpen;
        set => SetProperty(ref _isNotificationOpen, value);
    }

    public string? NotificationActionLabel
    {
        get => _notificationActionLabel;
        private set => SetProperty(ref _notificationActionLabel, value);
    }

    public string? NotificationActionUrl
    {
        get => _notificationActionUrl;
        private set => SetProperty(ref _notificationActionUrl, value);
    }

    public Visibility NotificationActionVisibility
    {
        get => _notificationActionVisibility;
        private set => SetProperty(ref _notificationActionVisibility, value);
    }

    public ICommand ShowGettingStartedCommand { get; }
    public ICommand ShowBasicOperationsCommand { get; }
    public ICommand ShowDetailedHelpCommand { get; }
    public ICommand ShowAboutCommand { get; }
    public ICommand ChangeLanguageCommand { get; }
    public ICommand ChangeThemeCommand { get; }
    public ICommand ChangeMapZoomLevelCommand { get; }
    public ICommand ChangeMapTileSourceCommand { get; }
    public ICommand ExportSettingsCommand { get; }
    public ICommand ImportSettingsCommand { get; }
    public ICommand PersistLayoutSettingsCommand { get; }

    public string? CurrentLanguage
    {
        get => _currentLanguage;
        private set => SetProperty(ref _currentLanguage, value);
    }

    public ThemePreference CurrentTheme
    {
        get => _currentTheme;
        private set => SetProperty(ref _currentTheme, value);
    }

    public int CurrentMapZoomLevel
    {
        get => _currentMapZoomLevel;
        private set => SetProperty(ref _currentMapZoomLevel, value);
    }

    public MapTileSourceType CurrentMapTileSource
    {
        get => _currentMapTileSource;
        private set => SetProperty(ref _currentMapTileSource, value);
    }

    public bool IsLanguageSystem => string.IsNullOrWhiteSpace(CurrentLanguage);
    public bool IsLanguageJapanese => string.Equals(CurrentLanguage, "ja-JP", StringComparison.OrdinalIgnoreCase);
    public bool IsLanguageEnglish => string.Equals(CurrentLanguage, "en-US", StringComparison.OrdinalIgnoreCase);

    public bool IsThemeSystem => CurrentTheme == ThemePreference.System;
    public bool IsThemeLight => CurrentTheme == ThemePreference.Light;
    public bool IsThemeDark => CurrentTheme == ThemePreference.Dark;

    public bool IsMapZoomLevel8 => CurrentMapZoomLevel == 8;
    public bool IsMapZoomLevel10 => CurrentMapZoomLevel == 10;
    public bool IsMapZoomLevel12 => CurrentMapZoomLevel == 12;
    public bool IsMapZoomLevel14 => CurrentMapZoomLevel == 14;
    public bool IsMapZoomLevel16 => CurrentMapZoomLevel == 16;
    public bool IsMapZoomLevel18 => CurrentMapZoomLevel == 18;

    public bool IsMapTileSourceOsm => CurrentMapTileSource == MapTileSourceType.OpenStreetMap;
    public bool IsMapTileSourceEsri => CurrentMapTileSource == MapTileSourceType.EsriWorldImagery;

    public void ShowNotificationMessage(string message, InfoBarSeverity severity)
    {
        SetNotification(message, severity);
    }

    public void ShowNotificationWithAction(string message, InfoBarSeverity severity, string actionLabel, string actionUrl)
    {
        SetNotification(message, severity);
        NotificationActionLabel = actionLabel;
        NotificationActionUrl = actionUrl;
        NotificationActionVisibility = Visibility.Visible;
    }

    public void ConfigureHelpService(IHelpService helpService)
    {
        _helpService = helpService ?? throw new ArgumentNullException(nameof(helpService));
        RaiseHelpCommandCanExecuteChanged();
    }

    public void ConfigureSettingsCoordinator(ISettingsCoordinator settingsCoordinator)
    {
        _settingsCoordinator = settingsCoordinator ?? throw new ArgumentNullException(nameof(settingsCoordinator));
        RaiseSettingsCommandCanExecuteChanged();
    }

    public void ApplySettingsState(
        string? language,
        ThemePreference theme,
        int mapZoomLevel,
        MapTileSourceType mapTileSource)
    {
        CurrentLanguage = language;
        CurrentTheme = theme;
        CurrentMapZoomLevel = mapZoomLevel;
        CurrentMapTileSource = mapTileSource;

        OnPropertyChanged(nameof(IsLanguageSystem));
        OnPropertyChanged(nameof(IsLanguageJapanese));
        OnPropertyChanged(nameof(IsLanguageEnglish));

        OnPropertyChanged(nameof(IsThemeSystem));
        OnPropertyChanged(nameof(IsThemeLight));
        OnPropertyChanged(nameof(IsThemeDark));

        OnPropertyChanged(nameof(IsMapZoomLevel8));
        OnPropertyChanged(nameof(IsMapZoomLevel10));
        OnPropertyChanged(nameof(IsMapZoomLevel12));
        OnPropertyChanged(nameof(IsMapZoomLevel14));
        OnPropertyChanged(nameof(IsMapZoomLevel16));
        OnPropertyChanged(nameof(IsMapZoomLevel18));

        OnPropertyChanged(nameof(IsMapTileSourceOsm));
        OnPropertyChanged(nameof(IsMapTileSourceEsri));
    }

    private void SetNotification(string? message, InfoBarSeverity severity)
    {
        ClearNotificationAction();
        if (string.IsNullOrWhiteSpace(message))
        {
            NotificationMessage = null;
            IsNotificationOpen = false;
            NotificationSeverity = InfoBarSeverity.Informational;
            return;
        }

        NotificationMessage = message;
        NotificationSeverity = severity;
        IsNotificationOpen = true;
    }

    private void ClearNotificationAction()
    {
        NotificationActionLabel = null;
        NotificationActionUrl = null;
        NotificationActionVisibility = Visibility.Collapsed;
    }

    private bool CanExecuteHelpAction()
    {
        return _helpService is not null;
    }

    private Task ExecuteHelpActionAsync(Func<IHelpService, Task> execute)
    {
        ArgumentNullException.ThrowIfNull(execute);

        if (_helpService is null)
        {
            return Task.CompletedTask;
        }

        return execute(_helpService);
    }

    private void RaiseHelpCommandCanExecuteChanged()
    {
        (ShowGettingStartedCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ShowBasicOperationsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ShowDetailedHelpCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ShowAboutCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private bool CanExecuteSettingsAction()
    {
        return _settingsCoordinator is not null;
    }

    private Task ExecuteSettingsActionAsync(Func<ISettingsCoordinator, Task> execute)
    {
        ArgumentNullException.ThrowIfNull(execute);

        if (_settingsCoordinator is null)
        {
            return Task.CompletedTask;
        }

        return execute(_settingsCoordinator);
    }

    private void RaiseSettingsCommandCanExecuteChanged()
    {
        (ChangeLanguageCommand as RelayCommand<string>)?.RaiseCanExecuteChanged();
        (ChangeThemeCommand as RelayCommand<string>)?.RaiseCanExecuteChanged();
        (ChangeMapZoomLevelCommand as RelayCommand<string>)?.RaiseCanExecuteChanged();
        (ChangeMapTileSourceCommand as RelayCommand<string>)?.RaiseCanExecuteChanged();
        (ExportSettingsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ImportSettingsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (PersistLayoutSettingsCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }
}
