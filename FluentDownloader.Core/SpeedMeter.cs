using System.Diagnostics;

namespace FluentDownloader.Core;

/// <summary>指数滑动平均测速器：输出稳定的速度值而非抖动的瞬时值。</summary>
public sealed class SpeedMeter
{
    private long _lastBytes;
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private double _ema;

    /// <summary>每收到一段数据调用一次，返回平滑后的字节/秒。</summary>
    public long Update(long totalBytesNow)
    {
        var elapsed = _sw.Elapsed.TotalSeconds;
        if (elapsed < 0.35) return (long)_ema;

        var delta = totalBytesNow - Interlocked.Exchange(ref _lastBytes, totalBytesNow);
        if (delta < 0) delta = 0;
        var speed = delta / elapsed;
        _sw.Restart();
        _ema = _ema <= 0 ? speed : _ema * 0.65 + speed * 0.35;
        return (long)_ema;
    }

    public void Reset()
    {
        _lastBytes = 0;
        _ema = 0;
        _sw.Restart();
    }
}
