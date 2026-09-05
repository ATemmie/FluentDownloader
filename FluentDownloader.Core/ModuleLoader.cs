using System.Reflection;
using System.Runtime.Loader;
using FluentDownloader.Contracts;

namespace FluentDownloader.Core;

/// <summary>
/// 模块加载器：扫描输出目录里的 FluentDownloader.Module.*.dll 并实例化其中所有 IModule。
/// 删除某个模块 dll 即可移除对应功能，App 与其余模块无需任何改动。
///
/// 加载策略：独立 AssemblyLoadContext 加载模块本体；依赖解析先走默认上下文
/// （Contracts/Core/WPF-UI 等与宿主共享，保证类型一致），失败后回退到应用目录
/// （MonoTorrent 等仅模块使用的依赖）。
/// </summary>
public static class ModuleLoader
{
    private static readonly object Gate = new();
    private static ModuleLoadContext? _moduleContext;

    public static List<IModule> DiscoverModules()
    {
        var modules = new List<IModule>();
        var baseDir = AppContext.BaseDirectory;

        var context = GetSharedContext(baseDir);

        foreach (var dll in Directory.EnumerateFiles(baseDir, "FluentDownloader.Module.*.dll"))
        {
            Assembly assembly;
            try
            {
                assembly = context.LoadFromAssemblyPath(dll);
            }
            catch
            {
                continue; // 缺依赖等原因，跳过该模块
            }

            TryCollect(assembly, modules);
        }

        // 开发期直接被宿主引用时，模块可能已在默认上下文中
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!assembly.GetName().Name!.StartsWith("FluentDownloader", StringComparison.Ordinal)) continue;
            TryCollect(assembly, modules);
        }

        return modules
            .GroupBy(m => m.Id)
            .Select(g => g.First())
            .OrderBy(m => m.NavigationOrder)
            .ToList();
    }

    private static ModuleLoadContext GetSharedContext(string baseDir)
    {
        lock (Gate)
        {
            if (_moduleContext != null) return _moduleContext;
            _moduleContext = new ModuleLoadContext(baseDir);
            return _moduleContext;
        }
    }

    private static void TryCollect(Assembly assembly, List<IModule> modules)
    {
        try
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || !typeof(IModule).IsAssignableFrom(type)) continue;
                if (Activator.CreateInstance(type) is not IModule module) continue;
                modules.Add(module);
            }
        }
        catch
        {
            // 单个模块加载失败不影响其它模块
        }
    }

    /// <summary>共享的模块加载上下文：共享依赖交给默认上下文，其余在应用目录兜底。</summary>
    private sealed class ModuleLoadContext : AssemblyLoadContext
    {
        private readonly string _basePath;

        public ModuleLoadContext(string basePath)
            : base("FluentDownloader.Modules", isCollectible: false)
        {
            _basePath = basePath;
            Resolving += OnResolving;
        }

        protected override Assembly? Load(AssemblyName name) => null; // 先交给默认上下文

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) => IntPtr.Zero;

        private Assembly? OnResolving(AssemblyLoadContext context, AssemblyName name)
        {
            try
            {
                var candidate = Path.Combine(_basePath, name.Name + ".dll");
                return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
