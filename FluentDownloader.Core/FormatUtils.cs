namespace FluentDownloader.Core;

/// <summary>通用格式化（UI 转换器使用）。</summary>
public static class FormatUtils
{
    public static string Bytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024L * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:F2} MB",
        < 1024L * 1024 * 1024 * 1024 => $"{bytes / 1024.0 / 1024 / 1024:F2} GB",
        _ => $"{bytes / 1024.0 / 1024 / 1024 / 1024:F2} TB",
    };

    public static string Speed(long bytesPerSecond) => bytesPerSecond <= 0 ? "--" : $"{Bytes(bytesPerSecond)}/s";

    public static string TimeSpan(long remainingSeconds)
    {
        if (remainingSeconds <= 0 || remainingSeconds > 86400 * 30) return "--";
        var ts = System.TimeSpan.FromSeconds(remainingSeconds);
        return ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}小时{ts.Minutes}分" : ts.TotalMinutes >= 1 ? $"{ts.Minutes}分{ts.Seconds}秒" : $"{ts.Seconds}秒";
    }
}
