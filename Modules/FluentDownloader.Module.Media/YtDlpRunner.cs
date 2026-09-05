using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FluentDownloader.Contracts;
using FluentDownloader.Core;

namespace FluentDownloader.Module.Media;

/// <summary>yt-dlp 二进制管理：缺失时自动下载，支持自更新。</summary>
public static class BinaryManager
{
    public static string BinDirectory => AppSettings.BinariesDirectory;

    public static string YtDlpPath => Path.Combine(BinDirectory, "yt-dlp.exe");

    public static string FfmpegPath => Path.Combine(BinDirectory, "ffmpeg.exe");

    public static bool YtDlpExists => File.Exists(YtDlpPath);

    public static bool FfmpegExists => File.Exists(FfmpegPath);

    /// <summary>确保引擎二进制存在，返回（yt-dlp 是否已就绪, ffmpeg 是否已就绪）。</summary>
    public static async Task<(bool YtDlp, bool Ffmpeg)> EnsureAllAsync(Action<string>? status, CancellationToken ct)
    {
        Directory.CreateDirectory(BinDirectory);

        if (!YtDlpExists)
        {
            status?.Invoke("首次使用：正在下载 yt-dlp 引擎…");
            await DownloadFileAsync(
                "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe",
                YtDlpPath, ct).ConfigureAwait(false);
        }

        if (!FfmpegExists)
        {
            status?.Invoke("首次使用：正在下载 ffmpeg（合并音视频用，体积较大请稍候）…");
            await DownloadFileAsync(
                "https://github.com/yt-dlp/FFmpeg-Builds/releases/latest/download/ffmpeg-master-latest-win64-gpl.zip",
                Path.Combine(BinDirectory, "ffmpeg.zip"), ct).ConfigureAwait(false);
            ExtractFfmpegZip();
        }

        return (YtDlpExists, FfmpegExists);
    }

    /// <summary>yt-dlp 自更新（-U）。</summary>
    public static async Task<string> UpdateYtDlpAsync(CancellationToken ct)
    {
        if (!YtDlpExists) return "尚未安装 yt-dlp";
        var (code, output) = await RunCaptureAsync(YtDlpPath, "-U", ct).ConfigureAwait(false);
        return code == 0 ? "yt-dlp 已更新到最新版本" : $"更新失败：{output.Split('\n')[^1].Trim()}";
    }

    private static void ExtractFfmpegZip()
    {
        var zipPath = Path.Combine(BinDirectory, "ffmpeg.zip");
        using var archive = System.IO.Compression.ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.First(e =>
            e.FullName.EndsWith("ffmpeg.exe", StringComparison.OrdinalIgnoreCase) && e.FullName.Contains("bin"));
        entry.ExtractToFile(FfmpegPath, true);

        var probe = archive.Entries.FirstOrDefault(e =>
            e.FullName.EndsWith("ffprobe.exe", StringComparison.OrdinalIgnoreCase) && e.FullName.Contains("bin"));
        probe?.ExtractToFile(Path.Combine(BinDirectory, "ffprobe.exe"), true);

        File.Delete(zipPath);
    }

    private static async Task DownloadFileAsync(string url, string target, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FluentDownloader/0.1");
        await using var stream = await http.GetStreamAsync(url, ct).ConfigureAwait(false);
        await using var file = new FileStream(target + ".tmp", FileMode.Create, FileAccess.Write);
        await stream.CopyToAsync(file, ct).ConfigureAwait(false);
        await file.FlushAsync(ct).ConfigureAwait(false);
        file.Close();
        File.Move(target + ".tmp", target, true);
    }

    private static async Task<(int Code, string Output)> RunCaptureAsync(string exe, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(psi)!;
        var output = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        return (process.ExitCode, output);
    }
}

/// <summary>
/// yt-dlp 进程封装（--newline + --progress-template 逐行解析进度，-J 取视频信息）。
/// </summary>
public sealed class YtDlpRunner
{
    private readonly ISettingsService _settingsService;

    public YtDlpRunner(ISettingsService settingsService) => _settingsService = settingsService;

    public sealed record MediaInfo(string Title, string? Uploader, string? Thumbnail, bool IsPlaylist, List<FormatOption> Formats);

    public sealed record FormatOption(string Label, string Selector, bool AudioOnly, long? SizeBytes);

    public async Task<MediaInfo> FetchInfoAsync(string url, string? cookiesPath, CancellationToken ct)
    {
        var args = BuildBaseArgs(cookiesPath);
        args += " -J --no-warnings --skip-download " + Quote(url);

        var (code, stdout, stderr) = await RunAsync(args, line => { }, ct).ConfigureAwait(false);
        if (code != 0)
            throw new InvalidOperationException(FirstError(stderr));

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;

        if (root.TryGetProperty("_type", out var type) && type.GetString() == "playlist")
            throw new InvalidOperationException("该链接是播放列表，请粘贴单个视频/歌曲的链接");

        var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "video" : "video";
        var uploader = root.TryGetProperty("uploader", out var u) ? u.GetString() : null;
        var thumbnail = root.TryGetProperty("thumbnail", out var th) ? th.GetString() : null;

        var formats = new List<FormatOption>();
        var heights = new SortedSet<int>();
        if (root.TryGetProperty("formats", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in arr.EnumerateArray())
            {
                if (!f.TryGetProperty("vcodec", out var vcodecEl)) continue;
                var vcodec = vcodecEl.GetString() ?? "none";
                if (vcodec == "none") continue;
                if (!f.TryGetProperty("height", out var h) || h.ValueKind != JsonValueKind.Number) continue;
                heights.Add(h.GetInt32());
            }
        }

        foreach (var height in heights.OrderByDescending(h => h))
        {
            formats.Add(new FormatOption(
                $"{height}P 视频 (自动合并音轨)",
                $"bestvideo[height<={height}]+bestaudio/best[height<={height}]/best",
                false,
                null));
        }

        if (formats.Count == 0)
            formats.Add(new FormatOption("最佳画质", "bestvideo*+bestaudio/best", false, null));

        formats.Add(new FormatOption("仅音频 MP3", "bestaudio/best", true, null));
        return new MediaInfo(title, uploader, thumbnail, false, formats);
    }

    public delegate void ProgressHandler(long downloadedBytes, long? totalBytes);

    /// <summary>执行下载；返回产出的文件路径。</summary>
    public async Task<string> DownloadAsync(
        string url, FormatOption format, string? cookiesPath, string outputDirectory,
        ProgressHandler onProgress, PauseToken pause, CancellationToken ct)
    {
        Directory.CreateDirectory(outputDirectory);

        var args = BuildBaseArgs(cookiesPath);
        args += " --newline --progress-template \"download:FDL|%(progress.downloaded_bytes)s|%(progress.total_bytes_estimate)s\"";
        args += " --print after_move:filepath";
        args += " --no-playlist --no-mtime --windows-filenames";
        args += $" -f \"{format.Selector}\"";
        args += format.AudioOnly
            ? " --extract-audio --audio-format mp3 --audio-quality 0"
            : " --merge-output-format mp4";
        args += $" -P \"{outputDirectory}\"";
        args += " " + Quote(url);

        string? filePath = null;

        var (code, _, stderr) = await RunAsync(args, line =>
        {
            if (line.StartsWith("FDL|"))
            {
                var parts = line.Split('|');
                var downloaded = long.TryParse(parts.Length > 1 ? parts[1] : null, out var d) ? d : 0;
                long? total = long.TryParse(parts.Length > 2 ? parts[2] : null, out var t) ? t : null;
                onProgress(downloaded, total);
            }
            else if (line.Length > 3 && File.Exists(line.Trim()))
            {
                filePath = line.Trim();
            }
        }, ct, pause).ConfigureAwait(false);

        if (code != 0)
            throw new InvalidOperationException(FirstError(stderr));

        if (filePath != null)
        {
            var name = Path.GetFileName(filePath);
            if (!string.IsNullOrEmpty(name))
            {
                // 由媒体引擎更新任务显示
                return filePath;
            }
        }

        // 兜底：扫描输出目录里最新的媒体文件
        var latest = new DirectoryInfo(outputDirectory)
            .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
            .Where(f => f.LastWriteTime > DateTime.Now.AddMinutes(-30))
            .OrderByDescending(f => f.LastWriteTime)
            .FirstOrDefault();
        return latest?.FullName ?? throw new InvalidOperationException("下载完成但未找到产出文件");
    }

    private string BuildBaseArgs(string? cookiesPath)
    {
        var args = $"--ffmpeg-location \"{BinaryManager.BinDirectory}\"";
        if (!string.IsNullOrEmpty(cookiesPath) && File.Exists(cookiesPath))
            args += $" --cookies \"{cookiesPath}\"";
        return args;
    }

    private static string Quote(string s) => "\"" + s.Replace("\"", "") + "\"";

    private static string FirstError(string stderr)
    {
        var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var error = lines.FirstOrDefault(l => l.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)) ?? lines.LastOrDefault();
        return error ?? "yt-dlp 下载失败";
    }

    private static async Task<(int Code, string Stdout, string Stderr)> RunAsync(
        string args, Action<string> onLine, CancellationToken ct, PauseToken? pause = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = BinaryManager.YtDlpPath,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdoutTask = PumpAsync(process.StandardOutput, onLine, ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        // 暂停策略：yt-dlp 不支持暂停，暂停时终止进程，恢复时重跑（--continue 自动续传）
        if (pause != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!process.HasExited)
                    {
                        if (pause is { IsPaused: true })
                        {
                            process.Kill(entireProcessTree: true);
                            return;
                        }
                        await Task.Delay(400, ct).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) { }
                catch { /* 进程已退出 */ }
            }, CancellationToken.None);
        }

        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return (process.ExitCode, stdout, stderr);
    }

    private static async Task<string> PumpAsync(StreamReader reader, Action<string> onLine, CancellationToken ct)
    {
        var sb = new StringBuilder();
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            sb.AppendLine(line);
            try { onLine(line); } catch { /* 单行解析失败忽略 */ }
        }
        return sb.ToString();
    }
}
