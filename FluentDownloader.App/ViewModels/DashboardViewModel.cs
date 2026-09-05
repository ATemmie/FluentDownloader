using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentDownloader.Contracts;

namespace FluentDownloader.App.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly ITaskManager _taskManager;

    public DashboardViewModel(ITaskManager taskManager)
    {
        _taskManager = taskManager;
        _taskManager.Tasks.CollectionChanged += (_, _) => RefreshStats();
    }

    [ObservableProperty]
    private string _urlInput = string.Empty;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private long _globalSpeed;

    [ObservableProperty]
    private int _activeCount;

    [ObservableProperty]
    private int _completedCount;

    public void RefreshStats()
    {
        GlobalSpeed = _taskManager.GlobalSpeedBytesPerSecond;
        ActiveCount = _taskManager.Tasks.Count(t => t.IsActive);
        CompletedCount = _taskManager.Tasks.Count(t => t.Status == DownloadTaskStatus.Completed);
    }

    [RelayCommand]
    private void Add()
    {
        var url = UrlInput.Trim();
        if (url.Length == 0)
        {
            Message = "请先粘贴一个链接";
            IsError = true;
            return;
        }

        try
        {
            var task = _taskManager.Enqueue(new DownloadRequest
            {
                Url = url,
                KindHint = InferKind(url),
            });
            Message = $"已添加任务：{task.Title}";
            IsError = false;
            UrlInput = string.Empty;
        }
        catch (Exception ex)
        {
            Message = ex.Message;
            IsError = true;
        }
    }

    /// <summary>按 URL 特征给出引擎路由提示，其余交给自动路由。</summary>
    private static DownloadKind InferKind(string url)
    {
        var lower = url.ToLowerInvariant();
        if (lower.StartsWith("magnet:")) return DownloadKind.Torrent;
        if (lower.StartsWith("http") && lower.EndsWith(".torrent")) return DownloadKind.Torrent;
        if (lower.Contains(".m3u8") || lower.Contains(".mpd")) return DownloadKind.MediaStream;
        return DownloadKind.Auto;
    }
}
