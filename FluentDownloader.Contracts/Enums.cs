namespace FluentDownloader.Contracts;

public enum DownloadTaskStatus
{
    /// <summary>在队列中等待空闲槽位。</summary>
    Pending,

    /// <summary>探测/解析中（尚未开始传数据）。</summary>
    Preparing,

    Downloading,

    Paused,

    Completed,

    Failed,

    Cancelled,
}

public enum DownloadKind
{
    /// <summary>由路由按 URL 特征自动判断。</summary>
    Auto,

    /// <summary>普通 HTTP(S) 直链文件。</summary>
    DirectLink,

    /// <summary>m3u8 / MPD 等流媒体清单，交给媒体引擎。</summary>
    MediaStream,

    /// <summary>视频/音乐平台页面链接（YouTube、B站、网易云等），先解析再下载。</summary>
    MediaPage,

    /// <summary>BT 种子或磁力链。</summary>
    Torrent,
}

public enum ResourceKind
{
    Unknown,
    Video,
    Audio,
    /// <summary>m3u8 / MPD 播放清单。</summary>
    Playlist,
    File,
    Image,
}

public enum ThemeMode
{
    Light,
    Dark,
    System,
}
