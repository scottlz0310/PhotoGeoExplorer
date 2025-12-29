using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Services;

internal sealed class SettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _settingsPath;

    public SettingsService()
    {
        _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhotoGeoExplorer",
            "settings.json");
    }

    public async Task<AppSettings> LoadAsync()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AppSettings();
            }

            using var stream = File.OpenRead(_settingsPath);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, SerializerOptions).ConfigureAwait(true);
            return settings ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Error("Failed to load settings.", ex);
            return new AppSettings();
        }
    }

    public Task SaveAsync(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return WriteAsync(_settingsPath, settings);
    }

    public static Task ExportAsync(AppSettings settings, string path)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.CompletedTask;
        }

        return WriteAsync(path, settings);
    }

    public static async Task<AppSettings?> ImportAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, SerializerOptions).ConfigureAwait(true);
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Error("Failed to import settings.", ex);
            return null;
        }
    }

    private static async Task WriteAsync(string path, AppSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
            using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions).ConfigureAwait(true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Error("Failed to save settings.", ex);
        }
    }
}
