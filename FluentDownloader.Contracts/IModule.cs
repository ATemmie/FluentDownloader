using Microsoft.Extensions.DependencyInjection;

namespace FluentDownloader.Contracts;

/// <summary>
/// 模块页描述。PageType 为 System.Windows.FrameworkElement 派生页（通常是 WPF UI 的 Page），
/// 由模块在自己的 RegisterServices 里注册到 DI，App 通过 INavigationViewPageProvider 解析。
/// IconGlyph 取 WPF UI SymbolRegular 枚举名（如 "Home24"）。
/// </summary>
public sealed record ModulePageDescriptor(string Title, string IconGlyph, Type PageType);

/// <summary>
/// 模块接口：启动时 App 扫描程序集中所有 IModule 实现自动注册。
/// 删功能 = 删程序集；加功能 = 新建实现 IModule 的类库。
/// </summary>
public interface IModule
{
    string Id { get; }

    string DisplayName { get; }

    /// <summary>导航栏图标（Fluent System Icons 字形）。</summary>
    string IconGlyph { get; }

    /// <summary>导航项排序，越小越靠前；&lt;0 表示不出现在导航栏。</summary>
    int NavigationOrder { get; }

    /// <summary>向 DI 注册本模块的服务与页面。</summary>
    void RegisterServices(IServiceCollection services);

    /// <summary>返回要挂到导航栏的页面（在 serviceProvider 构建完成后调用）。</summary>
    IEnumerable<ModulePageDescriptor> GetPages(IServiceProvider serviceProvider);

    /// <summary>返回本模块提供的下载引擎（可空）。</summary>
    IEnumerable<IDownloadEngine> GetEngines(IServiceProvider serviceProvider);
}
