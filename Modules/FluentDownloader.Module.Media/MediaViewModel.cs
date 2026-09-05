using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentDownloader.Contracts;
using FluentDownloader.Core;

namespace FluentDownloader.Module.Media;

public partial class MediaViewModel : ObservableObject
{
    private readonly ITaskManager _taskManager;
    private readonly ISettingsService _settingsService;
    private readonly YtDlpRunner _runner;

    public MediaViewModel(ITaskManager taskManager, ISettingsService settingsService, YtDlpRunner runner)
    {
        _taskManager = taskManager;
        _settingsService = settingsService;
        _runner = runner;
    }

    public sealed record FormatItem(string Label, string Selector, bool AudioOnly)
    {
        public override string ToString() => Label;
    }

    public sealed class MediaInfoResult
    {
        public string Title { get; init; } = "";
        public string? Uploader { get; init; }
        public string? Thumbnail { get; init; }
        public ObservableCollection<FormatItem> Formats { get; init; } = new();
    }

    [ObservableProperty]
    private string _urlInput = string.Empty;

    [ObservableProperty]
    private bool _isParsing;

    [ObservableProperty]
    private MediaInfoResult? _current;

    [ObservableProperty]
    private FormatItem? _selectedFormat;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private bool _isError;

    public string EngineStatus => BinaryManager.YtDlpExists
        ? "yt-dlp 已就绪"
        : "yt-dlp 未安装（添加任务时自动下载）";

    public string FfmpegStatus => BinaryManager.FfmpegExists
        ? "ffmpeg 已就绪"
        : "ffmpeg 未安装（添加任务时自动下载）";

    [RelayCommand]
    private async Task ParseAsync(CancellationToken ct)
    {
        var url = UrlInput.Trim();
        if (url.Length == 0)
        {
            Status = "请先粘贴视频或音乐链接";
            IsError = true;
            return;
        }

        IsParsing = true;
        Status = null;
        try
        {
            await BinaryManager.EnsureAllAsync(_ => { }, ct);
            var info = await _runner.FetchInfoAsync(url, _settingsService.Settings.CookiesFilePath, ct);
            var formats = new ObservableCollection<FormatItem>(
                info.Formats.Select(f => new FormatItem(f.Label, f.Selector, f.AudioOnly)));
            Current = new MediaInfoResult
            {
                Title = info.Title,
                Uploader = info.Uploader,
                Thumbnail = info.Thumbnail,
                Formats = formats,
            };
            SelectedFormat = formats.FirstOrDefault();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Status = $"解析失败：{ex.Message}";
            IsError = true;
        }
        finally
        {
            IsParsing = false;
        }
    }

    [RelayCommand]
    private void AddDownload()
    {
        if (Current == null || SelectedFormat == null) return;

        try
        {
            _taskManager.Enqueue(new DownloadRequest
            {
                Url = UrlInput.Trim(),
                KindHint = DownloadKind.MediaPage,
                CookiesFilePath = _settingsService.Settings.CookiesFilePath,
                Options = new Dictionary<string, string>
                {
                    ["selector"] = SelectedFormat.Selector,
                    ["audioOnly"] = SelectedFormat.AudioOnly ? "1" : "0",
                    ["label"] = SelectedFormat.Label,
                },
            });
            Status = $"已加入下载：{Current.Title}（{SelectedFormat.Label}）";
            IsError = false;
            Current = null;
            UrlInput = string.Empty;
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            IsError = true;
        }
    }

    [RelayCommand]
    private async Task UpdateEngineAsync(CancellationToken ct)
    {
        Status = await BinaryManager.UpdateYtDlpAsync(ct);
        IsError = false;
        OnPropertyChanged(nameof(EngineStatus));
        OnPropertyChanged(nameof(FfmpegStatus));
    }
}
