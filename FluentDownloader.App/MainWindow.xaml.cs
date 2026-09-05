using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
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
    private readonly DispatcherTimer _toastHideTimer;
    private INavigationService? _navigationService;

    public MainWindow(
        ITaskManager taskManager,
        INavigationService navigationService,
        ISettingsService settingsService,
        IServiceProvider serviceProvider)
    {
        SystemThemeWatcher.Watch(this);

        _taskManager = taskManager;

        InitializeComponent();

        navigationService.SetNavigationControl(RootNavigation);

        BuildNavigationItems(serviceProvider);

        _taskManager.TaskCompleted += (_, task) => Notify(
            "下载完成", task.Title, ToastKind.Success);
        _taskManager.TaskFailed += (_, task) => Notify(
            "下载失败", $"{task.Title}：{task.ErrorMessage}", ToastKind.Danger);

        _toastHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.5) };
        _toastHideTimer.Tick += (_, _) => HideToast();

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

    // ---------- Toast 通知（右下角）----------
    // WPF-UI 4.3.0 的 Snackbar Success/Danger 彩色外观在当前主题下渲染为空盒，弃用之，改为自绘。

    private enum ToastKind { Success, Danger, Info }

    private void Notify(string title, string message, ToastKind kind)
    {
        try
        {
            Dispatcher.Invoke(() => ShowToast(title, message, kind));
        }
        catch
        {
            // 通知失败不影响主流程
        }
    }

    private void ShowToast(string title, string message, ToastKind kind)
    {
        ToastTitle.Text = title;
        ToastMessage.Text = message;
        ToastMessage.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;

        var accent = kind switch
        {
            ToastKind.Success => Color.FromRgb(0x6C, 0xCB, 0x5F),
            ToastKind.Danger => Color.FromRgb(0xE8, 0x11, 0x23),
            _ => Color.FromRgb(0x0F, 0x6C, 0xBD),
        };
        ToastAccentBar.Background = new SolidColorBrush(accent);

        _toastHideTimer.Stop();
        ToastBorder.Visibility = Visibility.Visible;

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var slideIn = new DoubleAnimation(24, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        ToastBorder.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        ToastTransform.BeginAnimation(TranslateTransform.YProperty, slideIn);

        _toastHideTimer.Start();
    }

    private void HideToast()
    {
        _toastHideTimer.Stop();

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fadeOut.Completed += (_, _) =>
        {
            if (_toastHideTimer.IsEnabled) return; // 隐藏期间又有新通知，保持显示
            ToastBorder.Visibility = Visibility.Collapsed;
        };
        ToastBorder.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void OnToastClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _toastHideTimer.Stop();
        HideToast();
        _navigationService?.Navigate(typeof(TasksPage));
    }
}
