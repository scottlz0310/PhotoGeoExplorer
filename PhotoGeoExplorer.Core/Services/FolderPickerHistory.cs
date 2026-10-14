using System;
using System.Collections.Generic;
using System.IO;

namespace PhotoGeoExplorer.Services;

/// <summary>
/// フォルダー選択ダイアログの機能別（開く・移動・コピー）の前回確定フォルダーを、アプリ実行中だけ保持する。
/// Windows App SDK の FolderPicker は SettingsIdentifier を指定しても前回フォルダーを復元しないため、アプリ側で記憶する。
/// 再起動後は無関係なフォルダーが開くことを避けるため、保存しない。
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
    /// 確定したフォルダーを記録する。
    /// </summary>
    public void Record(string key, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _folders[key] = path;
    }
}
