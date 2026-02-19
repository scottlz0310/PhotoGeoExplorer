using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Controls;
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
    /// Map Pane から FileBrowser へフォーカス要求を通知するイベント
    /// </summary>
    public event EventHandler<WorkspacePhotoFocusRequestedEventArgs>? PhotoFocusRequested;

    /// <summary>
    /// Map Pane から FileBrowser へ複数選択要求を通知するイベント
    /// </summary>
    public event EventHandler<WorkspacePhotoSelectionRequestedEventArgs>? PhotoSelectionRequested;

    /// <summary>
    /// ペインからシェルへ通知表示を要求するイベント
    /// </summary>
    public event EventHandler<WorkspaceNotificationRequestedEventArgs>? NotificationRequested;

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

    /// <summary>
    /// 特定のファイルへフォーカスする要求を発行する
    /// </summary>
    /// <param name="filePath">対象ファイルパス</param>
    public void RequestPhotoFocus(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        PhotoFocusRequested?.Invoke(this, new WorkspacePhotoFocusRequestedEventArgs(filePath));
    }

    /// <summary>
    /// 複数ファイルを選択する要求を発行する
    /// </summary>
    /// <param name="filePaths">対象ファイルパス一覧</param>
    public void RequestPhotoSelection(IReadOnlyList<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        var filteredPaths = new List<string>(filePaths.Count);
        foreach (var filePath in filePaths)
        {
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                filteredPaths.Add(filePath);
            }
        }

        PhotoSelectionRequested?.Invoke(this, new WorkspacePhotoSelectionRequestedEventArgs(filteredPaths));
    }

    /// <summary>
    /// 通知表示の要求を発行する
    /// </summary>
    /// <param name="message">通知メッセージ</param>
    /// <param name="severity">通知種別</param>
    public void RequestNotification(string message, InfoBarSeverity severity)
    {
        ArgumentNullException.ThrowIfNull(message);
        NotificationRequested?.Invoke(this, new WorkspaceNotificationRequestedEventArgs(message, severity));
    }
}

internal sealed class WorkspacePhotoFocusRequestedEventArgs : EventArgs
{
    public WorkspacePhotoFocusRequestedEventArgs(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = filePath;
    }

    public string FilePath { get; }
}

internal sealed class WorkspacePhotoSelectionRequestedEventArgs : EventArgs
{
    public WorkspacePhotoSelectionRequestedEventArgs(IReadOnlyList<string> filePaths)
    {
        FilePaths = filePaths ?? throw new ArgumentNullException(nameof(filePaths));
    }

    public IReadOnlyList<string> FilePaths { get; }
}

internal sealed class WorkspaceNotificationRequestedEventArgs : EventArgs
{
    public WorkspaceNotificationRequestedEventArgs(string message, InfoBarSeverity severity)
    {
        Message = message ?? throw new ArgumentNullException(nameof(message));
        Severity = severity;
    }

    public string Message { get; }

    public InfoBarSeverity Severity { get; }
}
