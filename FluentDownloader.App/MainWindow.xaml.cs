using System.Windows;
using System.Windows.Interop;
using FluentDownloader.Contracts;
using FluentDownloader.App.Views.Pages;
using FluentDownloader.Core;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace FluentDownloader.App;

public partial class MainWindow
{
    private readonly ITaskManager _taskManager;
    private readonly ISnackbarService _snackbarService;

    public MainWindow(
        ITaskManager taskManager,
        INavigationService navigationService,
        ISnackbarService snackbarService,
        ISettingsService settingsService,
        IServiceProvider serviceProvider)
    {
        SystemThemeWatcher.Watch(this);

        _taskManager = taskManager;
        _snackbarService = snackbarService;

        InitializeComponent();

        navigationService.SetNavigationControl(RootNavigation);
        _snackbarService.SetSnackbarPresenter(SnackbarPresenter);

        BuildNavigationItems(serviceProvider);

        _taskManager.TaskCompleted += (_, task) => Notify(
            "下载完成",
            task.Title,
            SymbolRegular.CheckmarkCircle24,
            ControlAppearance.Success);
        _taskManager.TaskFailed += (_, task) => Notify(
            "下载失败",
            $"{task.Title}：{task.ErrorMessage}",
            SymbolRegular.ErrorCircle24,
            ControlAppearance.Danger);

        Loaded += (_, _) => RootNavigation.Navigate(typeof(DashboardPage));
    }

    private void BuildNavigationItems(IServiceProvider sp)
    {
        var menu = RootNavigation.MenuItems;
        menu.Clear();

        menu.Add(new NavigationViewItem("首页", SymbolRegular.Home24, typeof(DashboardPage)));
        menu.Add(new NavigationViewItem("下载任务", SymbolRegular.ArrowDownload24, typeof(TasksPage)));

        foreach (var module in ModuleLoader.DiscoverModules())
        {
            try
            {
                foreach (var page in module.GetPages(sp))
                {
                    if (page.PageType == typeof(DashboardPage) || page.PageType == typeof(TasksPage))
                        continue;
                    menu.Add(new NavigationViewItem(page.Title, ParseSymbol(page.IconGlyph), page.PageType));
                }
            }
            catch
            {
                // 单个模块装配失败不影响其它模块
            }
        }

        var footer = RootNavigation.FooterMenuItems;
        footer.Clear();
        footer.Add(new NavigationViewItem("设置", SymbolRegular.Settings24, typeof(SettingsPage)));
    }

    private static SymbolRegular ParseSymbol(string name)
        => Enum.TryParse(name, out SymbolRegular symbol) ? symbol : SymbolRegular.Fluent24;

    private void Notify(string title, string message, SymbolRegular icon, ControlAppearance appearance)
    {
        try
        {
            Dispatcher.Invoke(() =>
                _snackbarService.Show(title, message, appearance, new SymbolIcon(icon), System.TimeSpan.FromSeconds(4)));
        }
        catch
        {
            // 通知失败不影响主流程
        }
    }
}
