using FluentDownloader.Core;

var baseDir = args.Length > 0 ? args[0] : AppContext.BaseDirectory;
AppDomain.CurrentDomain.SetData("APP_CONTEXT_BASE_DIRECTORY", baseDir);

try
{
    var modules = ModuleLoader.DiscoverModules();
    Console.WriteLine($"发现模块数: {modules.Count}");
    foreach (var m in modules)
        Console.WriteLine($"  - {m.Id} / {m.DisplayName} / nav={m.NavigationOrder}");
}
catch (Exception ex)
{
    Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}
