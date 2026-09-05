using System.Collections.ObjectModel;

namespace FluentDownloader.Contracts;

/// <summary>任务管理器：所有模块通过它添加任务，App 的任务页直接绑定 Tasks。</summary>
public interface ITaskManager
{
    ObservableCollection<DownloadTask> Tasks { get; }

    /// <summary>全局聚合速度（字节/秒），UI 定时读取。</summary>
    long GlobalSpeedBytesPerSecond { get; }

    /// <summary>路由到引擎并入队。找不到能接管的引擎时抛 NotSupportedException。</summary>
    DownloadTask Enqueue(DownloadRequest request);

    void Pause(DownloadTask task);

    void Resume(DownloadTask task);

    void Cancel(DownloadTask task);

    /// <summary>取消（若在跑）并从列表移除；引擎决定是否清理半成品文件。</summary>
    void Remove(DownloadTask task);

    /// <summary>打开文件夹/选中文件。</summary>
    void RevealInExplorer(DownloadTask task);

    event EventHandler<DownloadTask>? TaskCompleted;

    event EventHandler<DownloadTask>? TaskFailed;
}
