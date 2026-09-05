using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using FluentDownloader.App.ViewModels;
using Wpf.Ui.Controls;

namespace FluentDownloader.App.Views.Pages;

public partial class DashboardPage
{
    private readonly DashboardViewModel _viewModel;
    private readonly DispatcherTimer _timer;

    public DashboardPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => _viewModel.RefreshStats();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.RefreshStats();
        _timer.Start();
        AnimateEnter();
    }

    private void OnUrlKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) _viewModel.AddCommand.Execute(null);
    }

    /// <summary>页面入场动画（淡入 + 上移）。</summary>
    private void AnimateEnter()
    {
        if (Resources["PageEnterAnimation"] is System.Windows.Media.Animation.Storyboard sb)
            BeginStoryboard(sb);
    }
}
