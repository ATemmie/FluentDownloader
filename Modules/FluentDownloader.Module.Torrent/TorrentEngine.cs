using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using FluentDownloader.Contracts;
using FluentDownloader.Core;
using MonoTorrent;
using MonoTorrent.Client;

namespace FluentDownloader.Module.Torrent;

/// <summary>BT/磁力链引擎（MonoTorrent）：DHT、元数据获取、进度/做种、限速。</summary>
public sealed class TorrentEngine : IDownloadEngine, ITaskSpeedSource
{
    private readonly ISettingsService _settingsService;
    private readonly object _gate = new();
    private ClientEngine? _clientEngine;
    private readonly Dictionary<string, TorrentManager> _managers = new(StringComparer.Ordinal);
    private long _currentSpeed;

    public TorrentEngine(ISettingsService settingsService) => _settingsService = settingsService;

    public string Id => "torrent";

    public string DisplayName => "BitTorrent";

    public int Priority => 10;

    public long CurrentSpeedBytesPerSecond => Interlocked.Read(ref _currentSpeed);

    public bool CanHandle(DownloadRequest request)
    {
        if (request.Url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase)) return true;
        if (request.KindHint == DownloadKind.Torrent && request.Url.StartsWith("http")) return true;
        if (request.Url.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) && File.Exists(request.Url)) return true;
        return false;
    }

    public async Task ExecuteAsync(DownloadTask task, PauseToken pause, CancellationToken ct)
    {
        var engine = await EnsureEngineAsync().ConfigureAwait(false);
        var savePath = task.SaveDirectory;
        Directory.CreateDirectory(savePath);

        task.Status = DownloadTaskStatus.Preparing;

        TorrentManager manager;
        if (task.Url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
        {
            var magnet = MagnetLink.Parse(task.Url);
            task.Title = "磁力链 " + ShortHash(magnet);
            manager = await engine.AddAsync(magnet, savePath).ConfigureAwait(false);

            if (!manager.HasMetadata)
            {
                task.QualityLabel = "获取元数据中";
                using var metadataCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                metadataCts.CancelAfter(TimeSpan.FromMinutes(5));
                try
                {
                    await manager.WaitForMetadataAsync(metadataCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new InvalidOperationException("获取磁力链元数据超时（可能没有可用节点），请改用 .torrent 种子文件");
                }
            }
        }
        else
        {
            var torrentPath = await ResolveTorrentFileAsync(task, ct).ConfigureAwait(false);
            var torrent = await MonoTorrent.Torrent.LoadAsync(torrentPath).ConfigureAwait(false);
            task.Title = torrent.Name;
            manager = await engine.AddAsync(torrent, savePath).ConfigureAwait(false);
        }

        lock (_gate) _managers[task.Id] = manager;
        task.Title = manager.Torrent?.Name ?? task.Title;
        task.TotalBytes = manager.Torrent?.Size ?? 0;
        task.QualityLabel = "BT";
        task.Status = DownloadTaskStatus.Downloading;

        await manager.StartAsync().ConfigureAwait(false);

        try
        {
            var wasPaused = false;
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(1000, ct).ConfigureAwait(false);

                UpdateTaskStatus(task, manager);

                if (pause.IsPaused && !wasPaused)
                {
                    await manager.PauseAsync().ConfigureAwait(false);
                    wasPaused = true;
                }
                else if (!pause.IsPaused && wasPaused)
                {
                    await manager.StartAsync().ConfigureAwait(false);
                    wasPaused = false;
                }

                if (manager.Progress >= 100.0 && manager.State == TorrentState.Seeding)
                {
                    await manager.StopAsync().ConfigureAwait(false);
                    return; // 下载完成（停止做种，任务标记完成）
                }
            }
        }
        finally
        {
            try
            {
                if (manager.State != TorrentState.Stopped)
                    await manager.StopAsync().ConfigureAwait(false);
                await engine.RemoveAsync(manager, RemoveMode.KeepAllData).ConfigureAwait(false);
            }
            catch
            {
                // 收尾失败忽略
            }
            lock (_gate) _managers.Remove(task.Id);
        }
    }

    private void UpdateTaskStatus(DownloadTask task, TorrentManager manager)
    {
        task.ReceivedBytes = manager.Monitor.DataBytesReceived;
        task.TotalBytes = Math.Max(task.TotalBytes, manager.Torrent?.Size ?? 0);
        var rate = manager.Monitor.DownloadRate;
        Interlocked.Exchange(ref _currentSpeed, rate);
        task.SpeedBytesPerSecond = rate;
        task.QualityLabel = manager.HasMetadata
            ? $"BT · {manager.Peers.Seeds} 做种 / {manager.Peers.Leechs} 下载中"
            : "获取元数据中";
    }

    private async Task<string> ResolveTorrentFileAsync(DownloadTask task, CancellationToken ct)
    {
        if (File.Exists(task.Url)) return task.Url;

        // http(s) 直链种子文件
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd(_settingsService.Settings.BrowserUserAgent);
        var bytes = await http.GetByteArrayAsync(task.Url, ct).ConfigureAwait(false);
        var local = Path.Combine(Path.GetTempPath(), "fdl-" + Guid.NewGuid().ToString("N")[..8] + ".torrent");
        await File.WriteAllBytesAsync(local, bytes, ct).ConfigureAwait(false);
        return local;
    }

    private static string ShortHash(MagnetLink magnet)
    {
        try
        {
            var hex = magnet.InfoHashes.V1OrV2.ToString();
            return hex.Length > 16 ? hex[..16] : hex;
        }
        catch
        {
            return "未知";
        }
    }

    private async Task<ClientEngine> EnsureEngineAsync()
    {
        lock (_gate)
        {
            if (_clientEngine != null) return _clientEngine;
        }

        var settingsBuilder = new EngineSettingsBuilder
        {
            // 监听用户设置端口；DHT 走随机端口启用
            ListenEndPoints = new Dictionary<string, IPEndPoint>
            {
                ["0.0.0.0"] = new IPEndPoint(IPAddress.Any, _settingsService.Settings.TorrentListenPort),
            },
            DhtEndPoint = new IPEndPoint(IPAddress.Any, 0),
            AllowLocalPeerDiscovery = true,
            AllowPortForwarding = true,
            MaximumUploadRate = (int)Math.Max(0, _settingsService.Settings.TorrentMaxUploadSpeedBytesPerSecond),
        };

        var engine = new ClientEngine(settingsBuilder.ToSettings());
        lock (_gate)
        {
            _clientEngine ??= engine;
        }
        return _clientEngine;
    }
}
