using System.Diagnostics;

namespace FluentDownloader.Core;

/// <summary>
/// 简单令牌桶限速器（字节/秒），可在多个下载任务间共享一个实例实现全局限速。
/// limit 为 0 表示不限速。
/// </summary>
public sealed class RateLimiter
{
    private readonly object _gate = new();
    private double _available;
    private long _lastTick = Stopwatch.GetTimestamp();

    /// <summary>占走 bytes 个字节；超出当前令牌时异步等待。取消令牌在等待期间生效。</summary>
    public async Task AcquireAsync(long bytes, long bytesPerSecond, CancellationToken ct)
    {
        if (bytesPerSecond <= 0) return;

        while (true)
        {
            long waitMs;
            lock (_gate)
            {
                var now = Stopwatch.GetTimestamp();
                var elapsedMs = (now - _lastTick) * 1000.0 / Stopwatch.Frequency;
                _lastTick = now;

                _available = Math.Min(_available + bytesPerSecond * elapsedMs / 1000.0, bytesPerSecond);

                if (_available >= bytes)
                {
                    _available -= bytes;
                    return;
                }

                waitMs = Math.Clamp((long)((bytes - _available) * 1000.0 / bytesPerSecond), 40, 1000);
            }

            await Task.Delay((int)waitMs, ct).ConfigureAwait(false);
        }
    }
}
