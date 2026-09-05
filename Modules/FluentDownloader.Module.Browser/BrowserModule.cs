using FluentDownloader.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace FluentDownloader.Module.Browser;

/// <summary>内置浏览器模块：网页浏览 + 资源嗅探 + 点选下载 + cookies 同步。</summary>
public sealed class BrowserModule : IModule
{
    public string Id => "browser";

    public string DisplayName => "网页嗅探";

    public string IconGlyph => "Globe24";

    public int NavigationOrder => 10;

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<BrowserPage>();
    }

    public IEnumerable<ModulePageDescriptor> GetPages(IServiceProvider serviceProvider)
    {
        yield return new ModulePageDescriptor(DisplayName, IconGlyph, typeof(BrowserPage));
    }

    public IEnumerable<IDownloadEngine> GetEngines(IServiceProvider serviceProvider) => [];
}
