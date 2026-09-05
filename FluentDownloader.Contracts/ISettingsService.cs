namespace FluentDownloader.Contracts;

/// <summary>应用设置（JSON 持久化于 %LocalAppData%\FluentDownloader\settings.json）。</summary>
public sealed class AppSettings : ObservableModel
{
    private string _downloadDirectory = DefaultDownloadDirectory;
    private ThemeMode _theme = ThemeMode.System;
    private string _accentColor = "#0F6CBD";
    private int _maxConcurrentDownloads = 3;
    private int _httpConnectionsPerTask = 8;
    private long _httpSpeedLimitBytesPerSecond;
    private int _torrentListenPort = 51413;
    private long _torrentMaxUploadSpeedBytesPerSecond;
    private bool _autoUpdateYtDlp = true;
    private string _cookiesFilePath = string.Empty;
    private string _browserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36 Edg/126.0.0.0";

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FluentDownloader");

    public static string DefaultDownloadDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads", "FluentDownloader");

    /// <summary>模块自管的二进制（yt-dlp.exe / ffmpeg.exe）所在目录。</summary>
    public static string BinariesDirectory { get; } = Path.Combine(DataDirectory, "bin");

    public string DownloadDirectory { get => _downloadDirectory; set => Set(ref _downloadDirectory, value); }

    public ThemeMode Theme { get => _theme; set => Set(ref _theme, value); }

    public string AccentColor { get => _accentColor; set => Set(ref _accentColor, value); }

    /// <summary>同时进行的任务数上限。</summary>
    public int MaxConcurrentDownloads { get => _maxConcurrentDownloads; set => Set(ref _maxConcurrentDownloads, Math.Max(1, value)); }

    /// <summary>HTTP 任务每文件的连接（分段）数。</summary>
    public int HttpConnectionsPerTask { get => _httpConnectionsPerTask; set => Set(ref _httpConnectionsPerTask, Math.Clamp(value, 1, 32)); }

    /// <summary>HTTP 全局限速（字节/秒），0 = 不限速。</summary>
    public long HttpSpeedLimitBytesPerSecond { get => _httpSpeedLimitBytesPerSecond; set => Set(ref _httpSpeedLimitBytesPerSecond, Math.Max(0, value)); }

    public int TorrentListenPort { get => _torrentListenPort; set => Set(ref _torrentListenPort, Math.Clamp(value, 1024, 65535)); }

    public long TorrentMaxUploadSpeedBytesPerSecond { get => _torrentMaxUploadSpeedBytesPerSecond; set => Set(ref _torrentMaxUploadSpeedBytesPerSecond, Math.Max(0, value)); }

    public bool AutoUpdateYtDlp { get => _autoUpdateYtDlp; set => Set(ref _autoUpdateYtDlp, value); }

    /// <summary>内置浏览器导出的 cookies 文件路径（Netscape 格式）。</summary>
    public string CookiesFilePath { get => _cookiesFilePath; set => Set(ref _cookiesFilePath, value); }

    public string BrowserUserAgent { get => _browserUserAgent; set => Set(ref _browserUserAgent, value); }

    public string YtDlpPath { get; set; } = string.Empty;

    public string FfmpegPath { get; set; } = string.Empty;
}

public interface ISettingsService
{
    AppSettings Settings { get; }

    /// <summary>启动时调用；文件不存在时使用默认值。</summary>
    void Load();

    void Save();
}
