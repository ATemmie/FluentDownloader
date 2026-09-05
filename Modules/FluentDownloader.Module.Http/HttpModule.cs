using FluentDownloader.Contracts;
using FluentDownloader.Core;
using Microsoft.Extensions.DependencyInjection;

namespace FluentDownloader.Module.Http;

/// <summary>HTTP 多线程下载模块。</summary>
public sealed class HttpModule : IModule
{
    public string Id => "http";

    public string DisplayName => "HTTP 直链";

    public string IconGlyph => "ArrowDownload24";

    /// <summary>无独立页面，能力融入首页与任务页；&lt;0 表示不挂导航。</summary>
    public int NavigationOrder => -1;

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<HttpDownloadEngine>();
        services.AddSingleton<IDownloadEngine>(sp => sp.GetRequiredService<HttpDownloadEngine>());
    }

    public IEnumerable<ModulePageDescriptor> GetPages(IServiceProvider serviceProvider) => [];

    public IEnumerable<IDownloadEngine> GetEngines(IServiceProvider serviceProvider)
        => [serviceProvider.GetRequiredService<HttpDownloadEngine>()];
}
