using System.Globalization;
using System.Windows;
using System.Windows.Data;
using FluentDownloader.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace FluentDownloader.Module.Media;

/// <summary>媒体模块：视频/音乐平台解析下载（yt-dlp + ffmpeg）。</summary>
public sealed class MediaModule : IModule
{
    public string Id => "media";

    public string DisplayName => "视频 / 音乐";

    public string IconGlyph => "MoviesAndTv24";

    public int NavigationOrder => 20;

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<YtDlpRunner>();
        services.AddSingleton<MediaEngine>();
        services.AddSingleton<IDownloadEngine>(sp => sp.GetRequiredService<MediaEngine>());
        services.AddSingleton<MediaPage>();
        services.AddSingleton<MediaViewModel>();
    }

    public IEnumerable<ModulePageDescriptor> GetPages(IServiceProvider serviceProvider)
    {
        yield return new ModulePageDescriptor(DisplayName, IconGlyph, typeof(MediaPage));
    }

    public IEnumerable<IDownloadEngine> GetEngines(IServiceProvider serviceProvider)
        => [serviceProvider.GetRequiredService<MediaEngine>()];
}
