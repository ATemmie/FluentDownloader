using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using FluentDownloader.Contracts;
using FluentDownloader.Core;

namespace FluentDownloader.Module.Http;

/// <summary>
/// 自研 HTTP 多线程下载引擎。
/// 流程：Range 0-1 探测 → 均分 N 段并发下载（校验 206）→ 预分配 seek 写入
/// → checkpoint 断点续传 → 不支持 Range 时降级单流 → 单段指数退避重试 → 全局限速。
/// </summary>
public sealed class HttpDownloadEngine : IDownloadEngine, ITaskSpeedSource
{
    private const int MaxRetriesPerSegment = 5;
    private const int BufferSize = 64 * 1024;
    private const long MinSegmentSize = 1024 * 512; // 单段小于 512KB 时不再细拆

    private static readonly HttpClient SharedClient = CreateClient();

    private readonly ISettingsService _settingsService;
    private readonly RateLimiter _rateLimiter = new();
    private readonly SpeedMeter _speed = new();
    private long _currentSpeed;

    public HttpDownloadEngine(ISettingsService settingsService) => _settingsService = settingsService;

    public string Id => "http";

    public string DisplayName => "HTTP 多线程";

    public int Priority => 100;

    public long CurrentSpeedBytesPerSecond => Interlocked.Read(ref _currentSpeed);

    public bool CanHandle(DownloadRequest request)
    {
        if (request.KindHint is DownloadKind.MediaStream or DownloadKind.MediaPage or DownloadKind.Torrent)
            return false;
        return request.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || request.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    public async Task ExecuteAsync(DownloadTask task, PauseToken pause, CancellationToken ct)
    {
        Directory.CreateDirectory(task.SaveDirectory);
        var targetPath = task.FullPath;

        var checkpoint = CheckpointState.TryLoad(targetPath);
        if (checkpoint != null && checkpoint.Url != task.Url)
            checkpoint = null; // 目标变了，进度作废

        if (checkpoint is not null && checkpoint.IsComplete() && checkpoint.TotalBytes > 0
            && File.Exists(targetPath) && new FileInfo(targetPath).Length == checkpoint.TotalBytes)
        {
            // 重启恢复：进度已完整且文件大小吻合，直接完成，不重下
            checkpoint.Delete(targetPath);
            task.ReceivedBytes = checkpoint.TotalBytes;
            return;
        }

        if (checkpoint != null && checkpoint.IsComplete())
        {
            checkpoint.Delete(targetPath);
            checkpoint = null;
        }

        var probe = await ProbeAsync(task, ct).ConfigureAwait(false);
        task.TotalBytes = probe.TotalBytes;

        // 文件名：优先 Content-Disposition，其次沿用入队时的 URL 推断名
        if (checkpoint == null)
        {
            var fileName = FileNameParser.Sanitize(
                probe.FileName
                ?? (Path.GetFileNameWithoutExtension(task.FileName).StartsWith("download")
                    ? FileNameParser.FromUrl(probe.FinalUrl ?? task.Url)
                    : task.FileName));
            if (!string.Equals(task.FileName, fileName, StringComparison.OrdinalIgnoreCase))
            {
                task.FileName = FileNameParser.EnsureUnique(task.SaveDirectory, fileName);
                targetPath = task.FullPath;
            }
        }

        task.Title = task.FileName;
        task.Status = DownloadTaskStatus.Downloading;

        var etagChanged = checkpoint != null && ResourceChanged(checkpoint, probe);
        if (checkpoint == null || etagChanged)
        {
            checkpoint = NewCheckpoint(probe, task.Url);
            if (etagChanged) File.Delete(targetPath);
        }
        else
        {
            checkpoint.ETag = probe.ETag;
            checkpoint.LastModified = probe.LastModified;
        }

        if (probe.SupportsRange && probe.TotalBytes > MinSegmentSize)
            await DownloadMultiSegmentAsync(task, targetPath, probe, checkpoint, pause, ct).ConfigureAwait(false);
        else
            await DownloadSingleStreamAsync(task, targetPath, probe, checkpoint, pause, ct).ConfigureAwait(false);

        checkpoint.Delete(targetPath);
    }

    // ---------- 探测 ----------

    private sealed record ProbeResult(
        long TotalBytes,
        bool SupportsRange,
        string? ETag,
        string? LastModified,
        string? FileName,
        string? FinalUrl,
        string? CookieHeader);

    private async Task<ProbeResult> ProbeAsync(DownloadTask task, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, task.Url);
        ApplyCommonHeaders(task, request, includeRange: true);
        request.Headers.Range = new RangeHeaderValue(0, 1);

        using var response = await SharedClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long total;
        bool supportsRange;
        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            supportsRange = true;
            total = ParseTotalFromContentRange(response.Content.Headers.ContentRange?.ToString())
                    ?? response.Content.Headers.ContentLength ?? 0;
        }
        else
        {
            supportsRange = false;
            total = response.Content.Headers.ContentLength ?? 0;
        }

        var fileName = FileNameParser.FromContentDisposition(
            response.Content.Headers.ContentDisposition?.ToString());

        return new ProbeResult(
            total,
            supportsRange,
            response.Headers.ETag?.Tag,
            response.Content.Headers.LastModified?.ToString("R"),
            fileName,
            response.RequestMessage?.RequestUri?.ToString(),
            BuildCookieHeader(task.CookiesFilePath));
    }

    private static long? ParseTotalFromContentRange(string? contentRange)
    {
        if (string.IsNullOrEmpty(contentRange)) return null;
        var parts = contentRange.Split('/');
        return parts.Length == 2 && long.TryParse(parts[1], out var total) ? total : null;
    }

    // ---------- 多段并发 ----------

    private async Task DownloadMultiSegmentAsync(
        DownloadTask task, string targetPath, ProbeResult probe, CheckpointState checkpoint,
        PauseToken pause, CancellationToken ct)
    {
        var total = checkpoint.TotalBytes = probe.TotalBytes;
        if (checkpoint.Segments.Count == 0 || checkpoint.Segments.Sum(s => s.End - s.Start + 1) != total)
            checkpoint.Segments = SplitSegments(total, _settingsService.Settings.HttpConnectionsPerTask).ToList();

        // 预分配文件，避免多线程随机写造成碎片
        using (var fs = new FileStream(targetPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read | FileShare.Write))
            fs.SetLength(total);

        _speed.Reset();
        checkpoint.Save(targetPath);

        var workers = checkpoint.Segments
            .Where(s => s.Remaining > 0)
            .Select(s => DownloadSegmentAsync(task, targetPath, probe, checkpoint, s, pause, ct));

        // 周期性落盘 checkpoint（暂停/取消时也能保证最新）；完成后用 linked CTS 停掉
        using var persistCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var persistTask = PersistPeriodicallyAsync(checkpoint, targetPath, pause, persistCts.Token);

        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (Exception)
        {
            persistCts.Cancel();
            await persistTask.IgnoreCancellationAsync().ConfigureAwait(false);
            checkpoint.Save(targetPath);
            throw;
        }

        persistCts.Cancel();
        await persistTask.IgnoreCancellationAsync().ConfigureAwait(false);
        task.ReceivedBytes = total;
        task.SpeedBytesPerSecond = 0;
    }

    private async Task DownloadSegmentAsync(
        DownloadTask task, string targetPath, ProbeResult probe, CheckpointState checkpoint,
        SegmentState segment, PauseToken pause, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await pause.WaitWhilePausedAsync(ct).ConfigureAwait(false);

                var from = segment.Start + segment.Done;
                if (from > segment.End) return;

                using var request = new HttpRequestMessage(HttpMethod.Get, task.Url);
                ApplyCommonHeaders(task, request, includeRange: true);
                request.Headers.Range = new RangeHeaderValue(from, segment.End);
                if (probe.ETag != null) request.Headers.IfRange = new RangeConditionHeaderValue(probe.ETag);

                using var response = await SharedClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    segment.Done = segment.End - segment.Start + 1; // 越界按完成处理
                    return;
                }
                if (response.StatusCode == HttpStatusCode.OK)
                    throw new InvalidOperationException("服务器忽略了 Range 请求（返回 200 全量），该链接请用单连接模式");

                response.EnsureSuccessStatusCode();

                await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using (var file = new FileStream(targetPath, FileMode.Open, FileAccess.Write, FileShare.Read | FileShare.Write))
                {
                    file.Seek(from, SeekOrigin.Begin);
                    var buffer = new byte[BufferSize];
                    while (true)
                    {
                        await pause.WaitWhilePausedAsync(ct).ConfigureAwait(false);
                        var read = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
                        if (read == 0) break;

                        var limit = _settingsService.Settings.HttpSpeedLimitBytesPerSecond / Math.Max(1, _settingsService.Settings.MaxConcurrentDownloads);
                        await _rateLimiter.AcquireAsync(read, limit, ct).ConfigureAwait(false);

                        await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                        segment.Done += read;
                        UpdateProgress(task, checkpoint);
                    }
                }

                if (segment.Remaining <= 0) return;
                throw new IOException($"连接提前断开，还剩 {segment.Remaining} 字节");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception) when (attempt < MaxRetriesPerSegment && !ct.IsCancellationRequested)
            {
                var backoff = Math.Min(30, Math.Pow(2, attempt + 1));
                await Task.Delay(TimeSpan.FromSeconds(backoff), ct).ConfigureAwait(false);
            }
        }
    }

    // ---------- 单流降级 ----------

    private async Task DownloadSingleStreamAsync(
        DownloadTask task, string targetPath, ProbeResult probe, CheckpointState checkpoint,
        PauseToken pause, CancellationToken ct)
    {
        checkpoint.SingleStream = true;
        _speed.Reset();

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await pause.WaitWhilePausedAsync(ct).ConfigureAwait(false);

                using var request = new HttpRequestMessage(HttpMethod.Get, task.Url);
                ApplyCommonHeaders(task, request, includeRange: false);

                // 支持从 Range 单段续传（有的服务器不支持多段但支持单段）
                if (checkpoint.SingleDone > 0 && probe.SupportsRange)
                    request.Headers.Range = new RangeHeaderValue(checkpoint.SingleDone, null);

                using var response = await SharedClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.OK && checkpoint.SingleDone > 0)
                    checkpoint.SingleDone = 0; // 服务器发了全量，重新来

                response.EnsureSuccessStatusCode();
                task.TotalBytes = response.Content.Headers.ContentLength > 0
                    ? response.Content.Headers.ContentLength.Value + checkpoint.SingleDone
                    : 0;
                task.Status = DownloadTaskStatus.Downloading;

                var openMode = checkpoint.SingleDone > 0 ? FileMode.Append : FileMode.Create;
                await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using (var file = new FileStream(targetPath, openMode, FileAccess.Write, FileShare.Read))
                {
                    var buffer = new byte[BufferSize];
                    while (true)
                    {
                        await pause.WaitWhilePausedAsync(ct).ConfigureAwait(false);
                        var read = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
                        if (read == 0) break;

                        var limit = _settingsService.Settings.HttpSpeedLimitBytesPerSecond / Math.Max(1, _settingsService.Settings.MaxConcurrentDownloads);
                        await _rateLimiter.AcquireAsync(read, limit, ct).ConfigureAwait(false);

                        await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                        checkpoint.SingleDone += read;
                        UpdateProgress(task, checkpoint);
                    }
                }

                if (task.TotalBytes > 0 && checkpoint.SingleDone < task.TotalBytes)
                    throw new IOException("连接提前断开");
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception) when (attempt < MaxRetriesPerSegment && !ct.IsCancellationRequested)
            {
                checkpoint.Save(targetPath);
                var backoff = Math.Min(30, Math.Pow(2, attempt + 1));
                await Task.Delay(TimeSpan.FromSeconds(backoff), ct).ConfigureAwait(false);
            }
        }
    }

    // ---------- 公共辅助 ----------

    private static IEnumerable<SegmentState> SplitSegments(long total, int connections)
    {
        var count = Math.Clamp(connections, 1, 32);
        count = (int)Math.Min(count, Math.Max(1, total / MinSegmentSize));
        var size = total / count;
        for (var i = 0; i < count; i++)
        {
            var start = i * size;
            var end = i == count - 1 ? total - 1 : start + size - 1;
            yield return new SegmentState { Start = start, End = end };
        }
    }

    private void UpdateProgress(DownloadTask task, CheckpointState checkpoint)
    {
        var received = checkpoint.ReceivedBytes();
        task.ReceivedBytes = received;
        var speed = _speed.Update(received);
        Interlocked.Exchange(ref _currentSpeed, speed);
        task.SpeedBytesPerSecond = speed;
    }

    private async Task PersistPeriodicallyAsync(CheckpointState checkpoint, string targetPath, PauseToken pause, CancellationToken ct)
    {
        var lastPaused = false;
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(2000, ct).ConfigureAwait(false);
            checkpoint.Save(targetPath);
            if (pause.IsPaused && !lastPaused)
            {
                checkpoint.Save(targetPath);
                lastPaused = true;
            }
            else if (!pause.IsPaused)
            {
                lastPaused = false;
            }
        }
    }

    private static bool ResourceChanged(CheckpointState checkpoint, ProbeResult probe)
    {
        if (checkpoint.TotalBytes != probe.TotalBytes) return true;
        if (checkpoint.ETag != null && probe.ETag != null) return checkpoint.ETag != probe.ETag;
        if (checkpoint.LastModified != null && probe.LastModified != null) return checkpoint.LastModified != probe.LastModified;
        return false;
    }

    private static CheckpointState NewCheckpoint(ProbeResult probe, string url) => new()
    {
        Url = url,
        ETag = probe.ETag,
        LastModified = probe.LastModified,
        TotalBytes = probe.TotalBytes,
    };

    private void ApplyCommonHeaders(DownloadTask task, HttpRequestMessage request, bool includeRange)
    {
        request.Headers.UserAgent.ParseAdd(_settingsService.Settings.BrowserUserAgent);
        if (task.Referrer != null && Uri.TryCreate(task.Referrer, UriKind.Absolute, out _))
            request.Headers.Referrer = new Uri(task.Referrer);

        var cookie = BuildCookieHeader(task.CookiesFilePath);
        if (!string.IsNullOrEmpty(cookie))
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
    }

    /// <summary>Netscape cookies.txt → Cookie 请求头。</summary>
    private static string? BuildCookieHeader(string? cookiesFilePath)
    {
        if (string.IsNullOrEmpty(cookiesFilePath) || !File.Exists(cookiesFilePath)) return null;

        try
        {
            var pairs = new List<string>();
            foreach (var line in File.ReadLines(cookiesFilePath))
            {
                if (line.StartsWith('#') || string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split('\t');
                if (parts.Length < 7) continue;
                pairs.Add($"{parts[5]}={parts[6]}");
            }
            return pairs.Count > 0 ? string.Join("; ", pairs) : null;
        }
        catch
        {
            return null;
        }
    }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.None, // 保持原始长度，Range 才可靠
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(15),
        };
        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan, // 读超时交给 CancellationToken 与重试
        };
    }
}

internal static class TaskExtensions
{
    public static async Task IgnoreCancellationAsync(this Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }
}
