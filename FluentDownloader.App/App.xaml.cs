using System.IO;
using System.Windows;
using FluentDownloader.Contracts;
using FluentDownloader.Core;
using FluentDownloader.App.Services;
using Wpf.Ui.Abstractions;
using FluentDownloader.App.Views.Pages;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui;
using Microsoft.Win32;

namespace FluentDownloader.App;

public partial class App : Application
{
    private static ServiceProvider? _services;

    public static IServiceProvider Services => _services
        ?? throw new InvalidOperationException("服务容器尚未初始化");

    public static T GetRequiredService<T>() where T : class => Services.GetRequiredService<T>();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settingsService = new SettingsService();
        settingsService.Load();

        var services = new ServiceCollection();
        services.AddSingleton<ISettingsService>(settingsService);
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ISnackbarService, SnackbarService>();

        // 核心页面（App 壳自带）
        services.AddSingleton<DashboardPage>();
        services.AddSingleton<TasksPage>();
        services.AddSingleton<SettingsPage>();
        services.AddSingleton<ViewModels.DashboardViewModel>();
        services.AddSingleton<ViewModels.TasksViewModel>();
        services.AddSingleton<ViewModels.SettingsViewModel>();

        // 模块自注册（服务 + 页面 + 引擎）
        var modules = ModuleLoader.DiscoverModules();
        foreach (var module in modules)
            module.RegisterServices(services);

        services.AddSingleton<INavigationViewPageProvider>(_ =>
            new DiPageProvider(t => Services.GetService(t)));
        services.AddSingleton<ITaskManager>(sp => new TaskManager(
            sp.GetServices<IDownloadEngine>().ToList(),
            settingsService,
            action => Current?.Dispatcher.Invoke(action)));
        services.AddSingleton<MainWindow>();

        _services = services.BuildServiceProvider();

        FluentDownloader.App.Services.ThemeService.Apply(settingsService.Settings);
        _services.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        GetRequiredService<ISettingsService>().Save();
        _services?.Dispose();
        base.OnExit(e);
    }

    /// <summary>选择文件夹对话框（.NET 8 内置）。</summary>
    public static string? PickFolder(string? initial = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择文件夹",
            InitialDirectory = Directory.Exists(initial ?? "") ? initial : null,
        };
        return dialog.ShowDialog(MainWindowOrNull()) == true ? dialog.FolderName : null;
    }

    private static Window? MainWindowOrNull() => Current?.MainWindow;

    private sealed class DiPageProvider(Func<Type, object?> resolver) : INavigationViewPageProvider
    {
        public T? GetPage<T>() where T : class => resolver(typeof(T)) as T;

        public object? GetPage(Type pageType) => resolver(pageType);
    }
}
