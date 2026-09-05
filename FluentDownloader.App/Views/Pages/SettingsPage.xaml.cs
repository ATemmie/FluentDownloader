using System.ComponentModel;
using System.Windows;
using FluentDownloader.App.ViewModels;
using Wpf.Ui.Controls;

namespace FluentDownloader.App.Views.Pages;

public partial class SettingsPage
{
    private readonly SettingsViewModel _viewModel;

    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Resources["PageEnterAnimation"] is System.Windows.Media.Animation.Storyboard sb)
            BeginStoryboard(sb);
    }

    private void OnThemeChanged(object sender, RoutedEventArgs e) => _viewModel.ApplyThemeCommand.Execute(null);
}
