using System.Diagnostics;
using System.IO;
using FluentDownloader.Contracts;
using FluentDownloader.Core;

namespace FluentDownloader.Module.Media;

/// <summary>
/// 媒体下载引擎：接管视频/音乐平台页面链接与 m3u8/MPD 流，交由 yt-dlp 下载、ffmpeg 合并。
/// VIP/DRM 加密内容不支持（yt-dlp 不解密 DRM），界面已明示。
/// </summary>
public sealed class MediaEngine : IDownloadEngine, ITaskSpeedSource
{
    private readonly ISettingsService _settingsService;
    private readonly YtDlpRunner _runner;
    private readonly SpeedMeter _speed = new();
    private long _currentSpeed;

    public MediaEngine(ISettingsService settingsService, YtDlpRunner runner)
    {
        _settingsService = settingsService;
        _runner = runner;
    }

    public string Id => "media";

    public string DisplayName => "媒体解析";

    public int Priority => 10;

    public long CurrentSpeedBytesPerSecond => Interlocked.Read(ref _currentSpeed);

    private static readonly string[] KnownHosts =
    [
        "youtube.com", "youtu.be", "bilibili.com", "b23.tv", "douyin.com", "iesdouyin.com",
        "kuaishou.com", "music.163.com", "y.qq.com", "soundcloud.com", "tiktok.com",
        "instagram.com", "x.com", "twitter.com", "vimeo.com", "dailymotion.com",
        "weibo.com", "ixigua.com", "xiaohongshu.com", "twitch.tv",
    ];

    public bool CanHandle(DownloadRequest request)
    {
        if (request.KindHint is DownloadKind.MediaStream or DownloadKind.MediaPage) return true;
        if (request.KindHint is DownloadKind.Torrent or DownloadKind.DirectLink) return false;

        var url = request.Url.ToLowerInvariant();
        if (!url.StartsWith("http")) return false;

        if (url.Contains(".m3u8") || url.Contains(".mpd")) return true;

        return KnownHosts.Any(host => url.Contains(host, StringComparison.Ordinal));
    }

    public async Task ExecuteAsync(DownloadTask task, PauseToken pause, CancellationToken ct)
    {
        task.Status = DownloadTaskStatus.Preparing;

        var (ytDlpReady, _) = await BinaryManager.EnsureAllAsync(
            msg => task.Title = msg, ct).ConfigureAwait(false);
        if (!ytDlpReady)
            throw new InvalidOperationException("yt-dlp 引擎下载失败，请检查网络后重试");

        await pause.WaitWhilePausedAsync(ct).ConfigureAwait(false);

        // 1. 解析视频信息（拿到标题与默认格式）
        var info = await _runner.FetchInfoAsync(task.Url, task.CookiesFilePath, ct).ConfigureAwait(false);
        task.Title = info.Title;

        // 2. 选定格式：任务 Options 里的 format 索引优先，否则默认最佳
        var format = PickFormat(task, info);
        task.QualityLabel = format.Label;

        // 3. 下载（暂停 = 杀进程，恢复 = --continue 续传）
        var outputDirectory = task.SaveDirectory;
        task.Status = DownloadTaskStatus.Downloading;
        _speed.Reset();

        long? lastTotal = null;
        var filePath = await _runner.DownloadAsync(
            task.Url, format, task.CookiesFilePath, outputDirectory,
            (downloaded, total) =>
            {
                task.TotalBytes = total ?? 0;
                task.ReceivedBytes = downloaded;
                var speed = _speed.Update(downloaded);
                Interlocked.Exchange(ref _currentSpeed, speed);
                task.SpeedBytesPerSecond = speed;
            },
            pause, ct).ConfigureAwait(false);

        task.SpeedBytesPerSecond = 0;
        _ = lastTotal;
        task.FileName = Path.GetFileName(filePath);
        task.ReceivedBytes = File.Exists(filePath) ? new FileInfo(filePath).Length : 0;
        task.TotalBytes = task.ReceivedBytes;
    }

    private static YtDlpRunner.FormatOption PickFormat(DownloadTask task, YtDlpRunner.MediaInfo info)
    {
        // 优先用解析页写入的选择器字符串（跨进程重跑最稳）
        if (task.Options.TryGetValue("selector", out var selector) && !string.IsNullOrWhiteSpace(selector))
        {
            var audioOnly = task.Options.TryGetValue("audioOnly", out var a) && a == "1";
            return new YtDlpRunner.FormatOption(
                task.Options.TryGetValue("label", out var label) ? label : "选定格式",
                selector, audioOnly, null);
        }

        if (task.Options.TryGetValue("formatIndex", out var indexText)
            && int.TryParse(indexText, out var index)
            && index >= 0 && index < info.Formats.Count)
        {
            return info.Formats[index];
        }
        return info.Formats[0];
    }
}
