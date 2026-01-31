using System;
using System.Collections.Generic;
using System.ComponentModel;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.State;

/// <summary>
/// ペイン間で共有される状態を管理するクラス
/// ペイン間の直接参照を避け、疎結合を保つために使用
/// </summary>
internal sealed class WorkspaceState : BindableBase
{
    private string? _currentFolderPath;
    private int _selectedPhotoCount;

    /// <summary>
    /// 現在選択されているフォルダのパス
    /// </summary>
    public string? CurrentFolderPath
    {
        get => _currentFolderPath;
        set => SetProperty(ref _currentFolderPath, value);
    }

    /// <summary>
    /// 現在選択されている写真の数
    /// </summary>
    public int SelectedPhotoCount
    {
        get => _selectedPhotoCount;
        set => SetProperty(ref _selectedPhotoCount, value);
    }

    // 将来的な拡張用のプレースホルダー
    // 例: 選択されている写真のリスト、地図の状態など
}
