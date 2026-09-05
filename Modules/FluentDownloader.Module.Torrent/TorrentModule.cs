using FluentDownloader.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace FluentDownloader.Module.Torrent;

/// <summary>BitTorrent 模块：磁力链/种子下载（MonoTorrent）。</summary>
public sealed class TorrentModule : IModule
{
    public string Id => "torrent";

    public string DisplayName => "BitTorrent";

    public string IconGlyph => "ArrowSyncCircle24";

    public int NavigationOrder => 30;

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<TorrentEngine>();
        services.AddSingleton<IDownloadEngine>(sp => sp.GetRequiredService<TorrentEngine>());
        services.AddSingleton<TorrentPage>();
        services.AddSingleton<TorrentViewModel>();
    }

    public IEnumerable<ModulePageDescriptor> GetPages(IServiceProvider serviceProvider)
    {
        yield return new ModulePageDescriptor(DisplayName, IconGlyph, typeof(TorrentPage));
    }

    public IEnumerable<IDownloadEngine> GetEngines(IServiceProvider serviceProvider)
        => [serviceProvider.GetRequiredService<TorrentEngine>()];
}
