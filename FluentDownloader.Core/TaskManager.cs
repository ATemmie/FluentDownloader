using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentDownloader.Contracts;

namespace FluentDownloader.Core;

/// <summary>
/// 统一任务管理器：路由 → 队列 → 并发槽位 → 引擎生命周期。
/// </summary>
public sealed class TaskManager : ITaskManager
{
    private const string PersistenceFileName = "tasks.json";

    private readonly IEnumerable<IDownloadEngine> _engines;
    private readonly ISettingsService _settingsService;
    private readonly Action<Action>? _dispatchToUi;
    private readonly object _gate = new();
    private readonly Queue<DownloadTask> _queue = new();
    private readonly Dictionary<string, RunningState> _running = new(StringComparer.Ordinal);
    private readonly SpeedMeter _globalSpeed = new();
    private readonly Timer _timer;
    private long _globalSpeedValue;

    private sealed class RunningState
    {
        public required IDownloadEngine Engine;
        public required PauseTokenSource PauseSource;
        public required CancellationTokenSource Cts;
        public required DownloadRequest Request;
        public Task? Worker;
    }

    public TaskManager(IEnumerable<IDownloadEngine> engines, ISettingsService settingsService, Action<Action>? dispatchToUi = null)
    {
        _engines = engines;
        _settingsService = settingsService;
        _dispatchToUi = dispatchToUi;
        _timer = new Timer(_ => TickSpeed(), null, 1000, 1000);
        LoadPersisted();
    }

    public ObservableCollection<DownloadTask> Tasks { get; } = new();

    public long GlobalSpeedBytesPerSecond => Interlocked.Read(ref _globalSpeedValue);

    public event EventHandler<DownloadTask>? TaskCompleted;

    public event EventHandler<DownloadTask>? TaskFailed;

    // ---------- 入队与路由 ----------

    public DownloadTask Enqueue(DownloadRequest request)
    {
        var engine = Route(request) ?? throw new NotSupportedException($"没有引擎能处理该链接：{request.Url}");

        var task = new DownloadTask
        {
            Url = request.Url,
            Kind = request.KindHint == DownloadKind.Auto ? KindFromEngine(engine) : request.KindHint,
            EngineId = engine.Id,
            EngineDisplayName = engine.DisplayName,
            SaveDirectory = request.SaveDirectory ?? _settingsService.Settings.DownloadDirectory,
            Referrer = request.Referrer,
            CookiesFilePath = request.CookiesFilePath,
        };
        task.Title = request.FileName ?? task.Url;
        if (request.Options != null)
            foreach (var kv in request.Options) task.Options[kv.Key] = kv.Value;

        var fileName = request.FileName;
        if (string.IsNullOrWhiteSpace(fileName) && engine.Id == "http")
            fileName = FileNameParser.Sanitize(FileNameParser.FromUrl(request.Url));
        if (!string.IsNullOrWhiteSpace(fileName))
            task.FileName = FileNameParser.EnsureUnique(task.SaveDirectory, fileName);

        OnUi(() => Tasks.Add(task));
        lock (_gate)
        {
            _queue.Enqueue(task);
            PumpQueueLocked();
        }
        Persist();
        return task;
    }

    private IDownloadEngine? Route(DownloadRequest request)
        => _engines.Where(e => e.CanHandle(request)).OrderBy(e => e.Priority).FirstOrDefault();

    private static DownloadKind KindFromEngine(IDownloadEngine engine) => engine.Id switch
    {
        "media" => DownloadKind.MediaPage,
        "torrent" => DownloadKind.Torrent,
        _ => DownloadKind.DirectLink,
    };

    // ---------- 控制 ----------

    public void Pause(DownloadTask task)
    {
        RunningState? state;
        lock (_gate) _running.TryGetValue(task.Id, out state);
        if (state != null)
        {
            state.PauseSource.Pause();
            task.Status = DownloadTaskStatus.Paused;
        }
        else if (task.IsActive)
        {
            task.Status = DownloadTaskStatus.Paused;
            lock (_gate)
            {
                // 还没开跑：从队列移除由状态标记实现（PumpQueue 跳过非 Pending）
            }
        }
        Persist();
    }

    public void Resume(DownloadTask task)
    {
        if (task.Status is DownloadTaskStatus.Completed or DownloadTaskStatus.Downloading) return;

        RunningState? state;
        lock (_gate) _running.TryGetValue(task.Id, out state);
        if (state != null && state.Worker is { IsCompleted: false })
        {
            // 引擎还在（暂停等待中），直接唤醒
            state.PauseSource.Resume();
            task.Status = DownloadTaskStatus.Downloading;
        }
        else
        {
            // 应用重启后的恢复：重新起一个执行
            lock (_gate)
            {
                _running.Remove(task.Id);
                _queue.Enqueue(task);
                PumpQueueLocked();
            }
        }
        Persist();
    }

    public void Cancel(DownloadTask task)
    {
        RunningState? state;
        lock (_gate) _running.TryGetValue(task.Id, out state);
        if (state != null)
        {
            state.Cts.Cancel();
        }
        else
        {
            task.Status = DownloadTaskStatus.Cancelled;
        }
        Persist();
    }

    public void Remove(DownloadTask task)
    {
        Cancel(task);
        OnUi(() => Tasks.Remove(task));
        Persist();
    }

    public void RevealInExplorer(DownloadTask task)
    {
        var path = task.FullPath;
        if (File.Exists(path))
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        else if (Directory.Exists(task.SaveDirectory))
            Process.Start("explorer.exe", task.SaveDirectory);
    }

    // ---------- 执行 ----------

    private void PumpQueueLocked()
    {
        var max = _settingsService.Settings.MaxConcurrentDownloads;
        while (_running.Count < max && _queue.TryDequeue(out var task))
        {
            if (task.Status is not (DownloadTaskStatus.Pending or DownloadTaskStatus.Paused or DownloadTaskStatus.Failed or DownloadTaskStatus.Cancelled))
                continue;
            StartLocked(task);
        }
    }

    private void StartLocked(DownloadTask task)
    {
        var engine = _engines.FirstOrDefault(e => e.Id == task.EngineId);
        if (engine == null)
        {
            task.Status = DownloadTaskStatus.Failed;
            task.ErrorMessage = "对应的下载模块未加载";
            return;
        }

        var state = new RunningState
        {
            Engine = engine,
            PauseSource = new PauseTokenSource(),
            Cts = new CancellationTokenSource(),
            Request = RequestFromTask(task),
        };
        _running[task.Id] = state;

        task.Status = DownloadTaskStatus.Preparing;
        task.ErrorMessage = null;

        state.Worker = Task.Run(async () =>
        {
            try
            {
                await engine.ExecuteAsync(task, state.PauseSource.Token, state.Cts.Token).ConfigureAwait(false);
                task.Status = DownloadTaskStatus.Completed;
                task.SpeedBytesPerSecond = 0;
                OnUi(() => TaskCompleted?.Invoke(this, task));
            }
            catch (OperationCanceledException)
            {
                task.Status = DownloadTaskStatus.Cancelled;
                task.SpeedBytesPerSecond = 0;
            }
            catch (Exception ex)
            {
                task.Status = DownloadTaskStatus.Failed;
                task.ErrorMessage = ex.Message;
                task.SpeedBytesPerSecond = 0;
                OnUi(() => TaskFailed?.Invoke(this, task));
            }
            finally
            {
                lock (_gate)
                {
                    _running.Remove(task.Id);
                    PumpQueueLocked();
                }
                Persist();
            }
        });
    }

    private static DownloadRequest RequestFromTask(DownloadTask task) => new()
    {
        Url = task.Url,
        KindHint = task.Kind,
        FileName = task.FileName,
        SaveDirectory = task.SaveDirectory,
        Referrer = task.Referrer,
        CookiesFilePath = task.CookiesFilePath,
        Options = new Dictionary<string, string>(task.Options),
    };

    // ---------- 速度聚合 ----------

    private void TickSpeed()
    {
        long total;
        lock (_gate)
        {
            total = _running.Keys
                .Select(id => _running[id])
                .Where(s => s.Worker is { IsCompleted: false })
                .Sum(s => s.Engine is ITaskSpeedSource src ? src.CurrentSpeedBytesPerSecond : 0);
        }
        total = _globalSpeed.Update(total);
        Interlocked.Exchange(ref _globalSpeedValue, total);
    }

    // ---------- 持久化 ----------

    private sealed record PersistedTask
    {
        [JsonPropertyName("url")] public string Url = "";
        [JsonPropertyName("kind")] public DownloadKind Kind;
        [JsonPropertyName("engineId")] public string EngineId = "";
        [JsonPropertyName("fileName")] public string FileName = "";
        [JsonPropertyName("saveDir")] public string SaveDirectory = "";
        [JsonPropertyName("title")] public string Title = "";
        [JsonPropertyName("quality")] public string QualityLabel = "";
        [JsonPropertyName("referrer")] public string? Referrer;
        [JsonPropertyName("cookies")] public string? CookiesFilePath;
        [JsonPropertyName("paused")] public bool Paused;
        [JsonPropertyName("options")] public Dictionary<string, string> Options = new();
    }

    private void Persist()
    {
        try
        {
            List<PersistedTask> snapshot;
            lock (_gate)
            {
                snapshot = Tasks
                    .Where(t => t.Status is DownloadTaskStatus.Pending or DownloadTaskStatus.Preparing
                        or DownloadTaskStatus.Downloading or DownloadTaskStatus.Paused or DownloadTaskStatus.Failed)
                    .Select(t => new PersistedTask
                    {
                        Url = t.Url,
                        Kind = t.Kind,
                        EngineId = t.EngineId,
                        FileName = t.FileName,
                        SaveDirectory = t.SaveDirectory,
                        Title = t.Title,
                        QualityLabel = t.QualityLabel,
                        Referrer = t.Referrer,
                        CookiesFilePath = t.CookiesFilePath,
                        Paused = t.Status is DownloadTaskStatus.Paused or DownloadTaskStatus.Failed,
                        Options = new Dictionary<string, string>(t.Options),
                    })
                    .ToList();
            }

            var file = Path.Combine(AppSettings.DataDirectory, PersistenceFileName);
            Directory.CreateDirectory(AppSettings.DataDirectory);
            File.WriteAllText(file, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 持久化失败不致命
        }
    }

    private void LoadPersisted()
    {
        try
        {
            var file = Path.Combine(AppSettings.DataDirectory, PersistenceFileName);
            if (!File.Exists(file)) return;

            var list = JsonSerializer.Deserialize<List<PersistedTask>>(File.ReadAllText(file));
            if (list == null) return;

            foreach (var p in list)
            {
                var task = new DownloadTask
                {
                    Url = p.Url,
                    Kind = p.Kind,
                    EngineId = p.EngineId,
                    EngineDisplayName = p.EngineId,
                    FileName = p.FileName,
                    SaveDirectory = p.SaveDirectory,
                    Title = string.IsNullOrEmpty(p.Title) ? p.FileName : p.Title,
                    QualityLabel = p.QualityLabel,
                    Referrer = p.Referrer,
                    CookiesFilePath = p.CookiesFilePath,
                    Status = p.Paused ? DownloadTaskStatus.Paused : DownloadTaskStatus.Pending,
                };
                foreach (var kv in p.Options) task.Options[kv.Key] = kv.Value;
                Tasks.Add(task);
                if (!p.Paused)
                {
                    lock (_gate) _queue.Enqueue(task);
                }
            }
        }
        catch
        {
            // 恢复失败则从头开始
        }
    }

    private void OnUi(Action action)
    {
        var d = _dispatchToUi;
        if (d != null) d(action);
        else action();
    }
}

/// <summary>引擎可选实现：向 TaskManager 暴露实时速度，用于全局速度聚合。</summary>
public interface ITaskSpeedSource
{
    long CurrentSpeedBytesPerSecond { get; }
}
