namespace FluentDownloader.Contracts;

/// <summary>一次“添加下载”请求：来源可以是首页输入、浏览器嗅探、点选模式等。</summary>
public sealed class DownloadRequest
{
    public required string Url { get; init; }

    public DownloadKind KindHint { get; init; } = DownloadKind.Auto;

    /// <summary>用户/来源模块指定的文件名（无则由引擎探测）。</summary>
    public string? FileName { get; init; }

    public string? SaveDirectory { get; init; }

    public string? Referrer { get; init; }

    /// <summary>Netscape 格式 cookies 文件路径（内置浏览器可一键导出）。</summary>
    public string? CookiesFilePath { get; init; }

    /// <summary>引擎私有参数（如媒体任务的格式 Id、仅音频标记）。</summary>
    public IReadOnlyDictionary<string, string>? Options { get; init; }

    /// <summary>产生此请求的来源模块 Id（诊断/统计用）。</summary>
    public string? SourceModule { get; init; }
}
