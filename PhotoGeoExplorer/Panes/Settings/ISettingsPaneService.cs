using System.Threading.Tasks;
using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Panes.Settings;

internal interface ISettingsPaneService
{
    Task<AppSettings> LoadSettingsAsync();

    Task SaveSettingsAsync(AppSettings settings);

    Task ExportSettingsAsync(AppSettings settings, string filePath);

    Task<AppSettings?> ImportSettingsAsync(string filePath);

    AppSettings CreateDefaultSettings();
}
