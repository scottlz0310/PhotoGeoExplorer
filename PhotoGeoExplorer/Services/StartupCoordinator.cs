using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PhotoGeoExplorer.Panes.FileBrowser;

namespace PhotoGeoExplorer.Services;

internal sealed class StartupCoordinator : IStartupCoordinator
{
    private readonly FileBrowserPaneViewModel _fileBrowserPaneViewModel;
    private readonly Func<string[]> _commandLineArgsProvider;
    private readonly Func<string?> _startupFolderOverrideProvider;
    private bool _disposed;
    private string? _startupFilePath;

    public StartupCoordinator(
        FileBrowserPaneViewModel fileBrowserPaneViewModel,
        Func<string[]>? commandLineArgsProvider = null,
        Func<string?>? startupFolderOverrideProvider = null)
    {
        _fileBrowserPaneViewModel = fileBrowserPaneViewModel ?? throw new ArgumentNullException(nameof(fileBrowserPaneViewModel));
        _commandLineArgsProvider = commandLineArgsProvider ?? Environment.GetCommandLineArgs;
        _startupFolderOverrideProvider = startupFolderOverrideProvider
            ?? (() => Environment.GetEnvironmentVariable("PHOTO_GEO_EXPLORER_E2E_FOLDER"));
    }

    public string? StartupFilePath => _startupFilePath;

    public void SetStartupFilePath(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        _startupFilePath = filePath;
    }

    public async Task ApplyStartupAsync()
    {
        ThrowIfDisposed();

        await ApplyStartupFolderOverrideAsync().ConfigureAwait(true);
        await ApplyStartupFileActivationAsync().ConfigureAwait(true);
    }

    public void Dispose()
    {
        _disposed = true;
    }

    internal static string? ResolveStartupFolderOverride(string[] args, string? envPath)
    {
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            return envPath.Trim('"');
        }

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (TryGetOptionValue(arg, "--folder", out var value)
                || TryGetOptionValue(arg, "/folder", out value)
                || TryGetOptionValue(arg, "--e2e-folder", out value))
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }

                if (i + 1 < args.Length)
                {
                    return args[i + 1].Trim('"');
                }
            }
        }

        return null;
    }

    internal static bool TryGetOptionValue(string argument, string option, out string? value)
    {
        value = null;
        if (!argument.StartsWith(option, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (argument.Length == option.Length)
        {
            return true;
        }

        var separator = argument[option.Length];
        if (separator is not '=' and not ':')
        {
            return false;
        }

        value = argument[(option.Length + 1)..].Trim('"');
        return true;
    }

    private async Task ApplyStartupFolderOverrideAsync()
    {
        var folderPath = ResolveStartupFolderOverride(
            _commandLineArgsProvider(),
            _startupFolderOverrideProvider());
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        if (!Directory.Exists(folderPath))
        {
            AppLog.Error($"Startup folder not found: {folderPath}");
            return;
        }

        await _fileBrowserPaneViewModel.LoadFolderAsync(folderPath).ConfigureAwait(true);
    }

    private async Task ApplyStartupFileActivationAsync()
    {
        if (string.IsNullOrWhiteSpace(_startupFilePath))
        {
            return;
        }

        var filePath = _startupFilePath;
        _startupFilePath = null;

        if (!File.Exists(filePath))
        {
            AppLog.Error($"Startup file not found: {filePath}");
            return;
        }

        var folderPath = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            AppLog.Error($"Failed to resolve startup file folder: {filePath}");
            return;
        }

        await _fileBrowserPaneViewModel.LoadFolderAsync(folderPath).ConfigureAwait(true);

        var item = _fileBrowserPaneViewModel.Items.FirstOrDefault(candidate =>
            string.Equals(candidate.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        if (item is null || item.IsFolder)
        {
            AppLog.Error($"Startup file not listed in folder view: {filePath}");
            return;
        }

        _fileBrowserPaneViewModel.UpdateSelection(new[] { item });
        _fileBrowserPaneViewModel.SelectedItem = item;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
