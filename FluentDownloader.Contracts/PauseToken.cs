namespace FluentDownloader.Contracts;

/// <summary>可暂停令牌：引擎的 worker 循环里定期 await WaitWhilePaused 实现“暂停不断开任务上下文”。</summary>
public sealed class PauseTokenSource
{
    private readonly object _gate = new();
    private bool _isPaused;

    public bool IsPaused
    {
        get { lock (_gate) return _isPaused; }
    }

    public PauseToken Token => new(this);

    public void Pause()
    {
        lock (_gate) _isPaused = true;
    }

    public void Resume()
    {
        lock (_gate) _isPaused = false;
    }
}

public readonly struct PauseToken
{
    private readonly PauseTokenSource? _source;

    internal PauseToken(PauseTokenSource source) => _source = source;

    public bool IsPaused => _source?.IsPaused ?? false;

    /// <summary>阻塞当前异步流程直到恢复或取消。轮询实现，100ms 粒度对网络 IO 可忽略。</summary>
    public async Task WaitWhilePausedAsync(CancellationToken ct)
    {
        while (_source is { IsPaused: true })
        {
            await Task.Delay(100, ct).ConfigureAwait(false);
        }
    }
}
