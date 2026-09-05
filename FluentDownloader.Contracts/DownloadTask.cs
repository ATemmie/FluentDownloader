namespace FluentDownloader.Contracts;

/// <summary>
/// 统一下载任务模型：UI 与所有引擎只面向此模型。
/// 引擎通过更新 ReceivedBytes/TotalBytes/Status 驱动 UI。
/// </summary>
public sealed class DownloadTask : ObservableModel
{
    private DownloadTaskStatus _status = DownloadTaskStatus.Pending;
    private long _receivedBytes;
    private long _totalBytes;
    private long _speedBytesPerSecond;
    private string? _errorMessage;
    private string _fileName = string.Empty;
    private string _saveDirectory = string.Empty;
    private string _qualityLabel = string.Empty;
    private string _title = string.Empty;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string Url { get; init; } = string.Empty;

    public DownloadKind Kind { get; init; } = DownloadKind.Auto;

    /// <summary>接管此任务的引擎 Id（路由结果）。</summary>
    public string EngineId { get; set; } = string.Empty;

    /// <summary>引擎显示名（用于 UI 标签，如 “HTTP 多线程”）。</summary>
    public string EngineDisplayName { get; set; } = string.Empty;

    /// <summary>标题（媒体任务为视频标题，其余为文件名）。</summary>
    public string Title { get => _title; set => Set(ref _title, value); }

    public string FileName { get => _fileName; set => Set(ref _fileName, value); }

    public string SaveDirectory { get => _saveDirectory; set => Set(ref _saveDirectory, value); }

    public string FullPath => Path.Combine(SaveDirectory, FileName);

    /// <summary>清晰度/格式标签（媒体任务用，如 1080P / mp3）。</summary>
    public string QualityLabel { get => _qualityLabel; set => Set(ref _qualityLabel, value); }

    public DateTime CreatedAt { get; init; } = DateTime.Now;

    public string? Referrer { get; set; }

    public string? CookiesFilePath { get; set; }

    /// <summary>引擎私有参数（格式 Id、仅音频等），随任务持久化。</summary>
    public Dictionary<string, string> Options { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public DownloadTaskStatus Status
    {
        get => _status;
        set
        {
            if (Set(ref _status, value))
            {
                Raise(nameof(IsIndeterminate));
                Raise(nameof(IsActive));
            }
        }
    }

    public long ReceivedBytes { get => _receivedBytes; set { if (Set(ref _receivedBytes, value)) Raise(nameof(ProgressPercent)); } }

    public long TotalBytes { get => _totalBytes; set { if (Set(ref _totalBytes, value)) Raise(nameof(ProgressPercent)); } }

    /// <summary>单任务实时速度（字节/秒），由引擎更新。</summary>
    public long SpeedBytesPerSecond { get => _speedBytesPerSecond; set => Set(ref _speedBytesPerSecond, value); }

    public string? ErrorMessage { get => _errorMessage; set => Set(ref _errorMessage, value); }

    public double ProgressPercent => TotalBytes > 0 ? Math.Min(100, ReceivedBytes * 100.0 / TotalBytes) : 0;

    /// <summary>总大小未知或探测中时进度条走不确定动画。</summary>
    public bool IsIndeterminate => Status == DownloadTaskStatus.Preparing || (TotalBytes <= 0 && Status == DownloadTaskStatus.Downloading);

    public bool IsActive => Status is DownloadTaskStatus.Pending or DownloadTaskStatus.Preparing or DownloadTaskStatus.Downloading;
}
