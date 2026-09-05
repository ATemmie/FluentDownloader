namespace FluentDownloader.Contracts;

/// <summary>浏览器嗅探/点选得到的可下载资源。</summary>
public sealed class ResourceInfo
{
    public required string Url { get; init; }

    public string? FileName { get; init; }

    public string? MimeType { get; init; }

    public long? SizeBytes { get; init; }

    /// <summary>嗅探推测的清晰度标签（如 1080p、audio）。</summary>
    public string? QualityLabel { get; init; }

    public ResourceKind Kind { get; init; } = ResourceKind.Unknown;

    /// <summary>资源所在页面 URL（作为下载 Referer）。</summary>
    public string? PageUrl { get; init; }

    public DownloadRequest ToRequest(string? cookiesFilePath = null, string? saveDirectory = null) => new()
    {
        Url = Url,
        KindHint = Kind switch
        {
            ResourceKind.Playlist => DownloadKind.MediaStream,
            _ => DownloadKind.Auto,
        },
        FileName = FileName,
        Referrer = PageUrl,
        CookiesFilePath = cookiesFilePath,
        SaveDirectory = saveDirectory,
        SourceModule = "browser",
    };
}
