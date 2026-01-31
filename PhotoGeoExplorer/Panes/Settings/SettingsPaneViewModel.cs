using System.Threading.Tasks;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Panes.Settings;

/// <summary>
/// 設定Paneの ViewModel（サンプル実装）
/// このクラスは将来的に実際の設定機能を実装する際のテンプレートとして使用できます
/// </summary>
internal sealed class SettingsPaneViewModel : PaneViewModelBase
{
    private string _languageSetting = "System Default";
    private string _themeSetting = "System Default";

    public SettingsPaneViewModel()
    {
        Title = "Settings";
    }

    /// <summary>
    /// 言語設定
    /// </summary>
    public string LanguageSetting
    {
        get => _languageSetting;
        set => SetProperty(ref _languageSetting, value);
    }

    /// <summary>
    /// テーマ設定
    /// </summary>
    public string ThemeSetting
    {
        get => _themeSetting;
        set => SetProperty(ref _themeSetting, value);
    }

    protected override async Task OnInitializeAsync()
    {
        // 設定の読み込みをここで実行
        // 例: SettingsService からの読み込み
        await Task.CompletedTask;
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
            // 設定を再読み込みするなど
        }
    }
}
