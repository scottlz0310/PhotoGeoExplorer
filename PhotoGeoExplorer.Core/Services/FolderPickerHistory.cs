using System;
using System.Collections.Generic;
using System.IO;

namespace PhotoGeoExplorer.Services;

/// <summary>
/// フォルダー選択ダイアログの機能別（開く・移動・コピー）の前回確定フォルダーを保持する。
/// Windows App SDK の FolderPicker は SettingsIdentifier を指定しても前回フォルダーを復元しないため、アプリ側で記憶する。
/// </summary>
internal sealed class FolderPickerHistory
{
    private readonly Dictionary<string, string> _folders = new(StringComparer.Ordinal);
    private readonly Func<string, bool> _directoryExists;

    public FolderPickerHistory()
        : this(Directory.Exists)
    {
    }

    internal FolderPickerHistory(Func<string, bool> directoryExists)
    {
        _directoryExists = directoryExists ?? throw new ArgumentNullException(nameof(directoryExists));
    }

    /// <summary>
    /// 機能別の前回フォルダーを返す。記録が無い、またはフォルダーが存在しない場合は null。
    /// </summary>
    public string? GetStartFolder(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return _folders.TryGetValue(key, out var path) && _directoryExists(path) ? path : null;
    }

    /// <summary>
    /// 確定したフォルダーを記録する。記録が変わった場合は true。
    /// </summary>
    public bool Record(string key, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (_folders.TryGetValue(key, out var current) && string.Equals(current, path, StringComparison.Ordinal))
        {
            return false;
        }

        _folders[key] = path;
        return true;
    }

    /// <summary>
    /// 保存済みの記録を読み込む。キーまたはパスが空の項目は無視し、既存の記録は置き換える。
    /// </summary>
    public void Load(IReadOnlyDictionary<string, string>? saved)
    {
        _folders.Clear();
        if (saved is null)
        {
            return;
        }

        foreach (var (key, path) in saved)
        {
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(path))
            {
                _folders[key] = path;
            }
        }
    }

    public IReadOnlyDictionary<string, string> ToDictionary()
    {
        return new Dictionary<string, string>(_folders, StringComparer.Ordinal);
    }
}
