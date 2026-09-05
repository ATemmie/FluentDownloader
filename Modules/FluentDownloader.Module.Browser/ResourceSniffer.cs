using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using FluentDownloader.Contracts;
using Microsoft.Web.WebView2.Core;

namespace FluentDownloader.Module.Browser;

/// <summary>
/// 基于 CDP（Chrome DevTools Protocol）Network 域的资源嗅探器：
/// 监听页面全部网络请求，按媒体类型/附件特征筛出可下载资源。
/// </summary>
public sealed class ResourceSniffer : IDisposable
{
    private const int MaxResources = 300;

    private readonly CoreWebView2 _core;
    private readonly ITaskManager _taskManager;
    private readonly ISettingsService _settingsService;
    private readonly Dictionary<string, string> _requestUrls = new(StringComparer.Ordinal); // requestId → url
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    public ObservableCollection<ResourceInfo> Resources { get; } = new();

    public event Action<ResourceInfo>? ResourceAdded;

    public ResourceSniffer(CoreWebView2 core, ITaskManager taskManager, ISettingsService settingsService)
    {
        _core = core;
        _taskManager = taskManager;
        _settingsService = settingsService;
    }

    public bool Enabled { get; private set; } = true;

    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        if (!enabled) Clear();
    }

    public async Task StartAsync()
    {
        await _core.CallDevToolsProtocolMethodAsync("Network.enable", "{}").ConfigureAwait(true);
        _core.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent").DevToolsProtocolEventReceived += OnRequestWillBeSent;
        _core.GetDevToolsProtocolEventReceiver("Network.responseReceived").DevToolsProtocolEventReceived += OnResponseReceived;
    }

    public void Clear()
    {
        _requestUrls.Clear();
        _seen.Clear();
        Resources.Clear();
    }

    private void OnRequestWillBeSent(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.ParameterObjectAsJson);
            var requestId = doc.RootElement.GetProperty("requestId").GetString();
            var url = doc.RootElement.GetProperty("request").GetProperty("url").GetString();
            if (requestId != null && url != null) _requestUrls[requestId] = url;
        }
        catch
        {
            // 嗅探解析失败忽略
        }
    }

    private void OnResponseReceived(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.ParameterObjectAsJson);
            var root = doc.RootElement;

            var requestId = root.GetProperty("requestId").GetString();
            var response = root.GetProperty("response");
            var url = response.GetProperty("url").GetString() ?? "";
            var mimeType = response.TryGetProperty("mimeType", out var mt) ? mt.GetString() ?? "" : "";

            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;

            // 附件 / 媒体才记录
            var headers = response.TryGetProperty("headers", out var hs) ? hs : default;
            var disposition = headers.ValueKind == JsonValueKind.Object
                && headers.TryGetProperty("content-disposition", out var cd)
                ? cd.GetString() : null;

            long? size = null;
            if (response.TryGetProperty("encodedDataLength", out var len) && len.ValueKind == JsonValueKind.Number)
                size = len.GetInt64();

            var kind = Classify(url, mimeType, disposition);
            if (kind == ResourceKind.Unknown) return;
            if (_seen.Contains(url)) return;

            if (_requestUrls.TryGetValue(requestId ?? "", out var reqUrl) && reqUrl != url)
            {
                // 重定向后的最终 URL
            }

            _seen.Add(url);
            var info = new ResourceInfo
            {
                Url = url,
                FileName = GuessFileName(url, mimeType),
                MimeType = mimeType,
                SizeBytes = size,
                Kind = kind,
                PageUrl = _core.Source,
            };

            if (Resources.Count < MaxResources) Resources.Add(info);
            ResourceAdded?.Invoke(info);
        }
        catch
        {
            // 嗅探解析失败忽略
        }
    }

    private static ResourceKind Classify(string url, string mimeType, string? disposition)
    {
        var lowerUrl = url.ToLowerInvariant();
        var mime = mimeType.ToLowerInvariant();

        if (mime.Contains("mpegurl") || lowerUrl.Contains(".m3u8")) return ResourceKind.Playlist;
        if (mime.Contains("dash+xml") || lowerUrl.Contains(".mpd")) return ResourceKind.Playlist;
        if (mime.StartsWith("video/")) return ResourceKind.Video;
        if (mime.StartsWith("audio/")) return ResourceKind.Audio;
        if (disposition != null && disposition.Contains("attachment", StringComparison.OrdinalIgnoreCase))
            return ResourceKind.File;
        if (mime.Contains("octet-stream") && !lowerUrl.Contains(".zip.css")) return ResourceKind.File;
        if (mime.StartsWith("image/") && (lowerUrl.Contains(".svg") || lowerUrl.Contains(".gif"))) return ResourceKind.Image;
        return ResourceKind.Unknown;
    }

    private static string GuessFileName(string url, string mimeType)
    {
        try
        {
            var uri = new Uri(url);
            var last = Uri.UnescapeDataString(uri.Segments[^1]).TrimEnd('/');
            if (last.Length == 0) last = "resource";
            if (!Path.HasExtension(last) && mimeType.Length > 0)
            {
                var ext = mimeType switch
                {
                    var m when m.Contains("mpegurl") => ".m3u8",
                    var m when m.Contains("dash") => ".mpd",
                    var m when m.StartsWith("video/") => "." + m.Split('/')[1].Split(';')[0],
                    var m when m.StartsWith("audio/") => "." + m.Split('/')[1].Split(';')[0],
                    _ => "",
                };
                last += ext;
            }
            var invalid = Path.GetInvalidFileNameChars();
            var sb = last.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
            return new string(sb);
        }
        catch
        {
            return "resource";
        }
    }

    public void Dispose()
    {
        _core.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent").DevToolsProtocolEventReceived -= OnRequestWillBeSent;
        _core.GetDevToolsProtocolEventReceiver("Network.responseReceived").DevToolsProtocolEventReceived -= OnResponseReceived;
    }
}
