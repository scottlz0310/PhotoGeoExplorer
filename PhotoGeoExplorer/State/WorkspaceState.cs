using System;
using System.Collections.Generic;
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
    private IReadOnlyList<PhotoListItem>? _selectedPhotos;
    private int _photoListCount;
    private int _currentPhotoIndex = -1;

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

    /// <summary>
    /// 現在選択されている写真のリスト
    /// Map Pane などで位置情報を表示するために使用
    /// </summary>
    public IReadOnlyList<PhotoListItem>? SelectedPhotos
    {
        get => _selectedPhotos;
        set => SetProperty(ref _selectedPhotos, value);
    }

    /// <summary>
    /// 写真リストの総数（フォルダを除く画像のみ）
    /// </summary>
    public int PhotoListCount
    {
        get => _photoListCount;
        set => SetProperty(ref _photoListCount, value);
    }

    /// <summary>
    /// 現在選択されている写真のインデックス（写真リスト内）
    /// -1 の場合は未選択
    /// </summary>
    public int CurrentPhotoIndex
    {
        get => _currentPhotoIndex;
        set => SetProperty(ref _currentPhotoIndex, value);
    }

    /// <summary>
    /// 次の画像に移動するためのコールバック
    /// MainViewModel が設定し、PreviewPaneViewModel が呼び出す
    /// </summary>
    public Action? SelectNextAction { get; set; }

    /// <summary>
    /// 前の画像に移動するためのコールバック
    /// MainViewModel が設定し、PreviewPaneViewModel が呼び出す
    /// </summary>
    public Action? SelectPreviousAction { get; set; }

    /// <summary>
    /// 次の画像に移動可能かどうか
    /// </summary>
    public bool CanSelectNext => CurrentPhotoIndex >= 0 && CurrentPhotoIndex < PhotoListCount - 1;

    /// <summary>
    /// 前の画像に移動可能かどうか
    /// </summary>
    public bool CanSelectPrevious => CurrentPhotoIndex > 0;

    /// <summary>
    /// 次の画像を選択
    /// </summary>
    public void SelectNext()
    {
        SelectNextAction?.Invoke();
    }

    /// <summary>
    /// 前の画像を選択
    /// </summary>
    public void SelectPrevious()
    {
        SelectPreviousAction?.Invoke();
    }
}
