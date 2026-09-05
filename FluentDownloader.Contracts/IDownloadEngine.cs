namespace FluentDownloader.Contracts;

/// <summary>
/// 下载引擎接口。一个模块可提供 0..n 个引擎，TaskManager 按 Priority 路由。
/// ExecuteAsync 是长驻调用：完成/暂停等待/取消/抛异常分别对应
/// Completed / Paused（任务留在 Paused 状态）/ Cancelled / Failed。
/// 引擎负责自己的断点续传状态（checkpoint）。
/// </summary>
public interface IDownloadEngine
{
    string Id { get; }

    string DisplayName { get; }

    /// <summary>数值越小越优先接管（如 Media 引擎优先抢 m3u8，HTTP 兜底）。</summary>
    int Priority { get; }

    bool CanHandle(DownloadRequest request);

    Task ExecuteAsync(DownloadTask task, PauseToken pause, CancellationToken ct);
}
