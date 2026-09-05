using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentDownloader.Module.Http;

/// <summary>HTTP 任务断点状态，持久化为 &lt;目标文件&gt;.fdl.json。</summary>
public sealed class CheckpointState
{
    [JsonPropertyName("url")] public string Url { get; set; } = "";

    [JsonPropertyName("etag")] public string? ETag { get; set; }

    [JsonPropertyName("lastModified")] public string? LastModified { get; set; }

    [JsonPropertyName("total")] public long TotalBytes { get; set; }

    /// <summary>服务器不支持 Range 时为 true，配合 SingleDone 记录单流进度。</summary>
    [JsonPropertyName("single")] public bool SingleStream { get; set; }

    [JsonPropertyName("singleDone")] public long SingleDone { get; set; }

    [JsonPropertyName("segments")] public List<SegmentState> Segments { get; set; } = new();

    public bool IsComplete()
        => SingleStream
            ? TotalBytes > 0 && SingleDone >= TotalBytes
            : Segments.Count > 0 && Segments.All(s => s.Done >= s.End - s.Start + 1);

    public long ReceivedBytes()
        => SingleStream ? SingleDone : Segments.Sum(s => s.Done);

    public static CheckpointState? TryLoad(string targetPath)
    {
        try
        {
            var file = CheckpointPath(targetPath);
            if (!File.Exists(file)) return null;
            var state = JsonSerializer.Deserialize<CheckpointState>(File.ReadAllText(file));
            return state?.Url.Length > 0 ? state : null;
        }
        catch
        {
            return null;
        }
    }

    public void Save(string targetPath)
    {
        try
        {
            var file = CheckpointPath(targetPath);
            var tmp = file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this));
            File.Move(tmp, file, true);
        }
        catch
        {
            // checkpoint 写失败不致命，大不了重下
        }
    }

    public void Delete(string targetPath)
    {
        try
        {
            var file = CheckpointPath(targetPath);
            if (File.Exists(file)) File.Delete(file);
        }
        catch
        {
            // ignore
        }
    }

    private static string CheckpointPath(string targetPath) => targetPath + ".fdl.json";
}

public sealed class SegmentState
{
    [JsonPropertyName("start")] public long Start { get; set; }

    /// <summary>闭区间末字节。</summary>
    [JsonPropertyName("end")] public long End { get; set; }

    [JsonPropertyName("done")] public long Done { get; set; }

    [JsonIgnore]
    public long Remaining => End - (Start + Done) + 1;
}
