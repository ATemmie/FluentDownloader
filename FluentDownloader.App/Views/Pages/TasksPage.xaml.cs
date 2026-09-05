using System.ComponentModel;
using System.Windows;
using FluentDownloader.App.ViewModels;
using Wpf.Ui.Controls;

namespace FluentDownloader.App.Views.Pages;

public partial class TasksPage
{
    public TasksPage(TasksViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Resources["PageEnterAnimation"] is System.Windows.Media.Animation.Storyboard sb)
            BeginStoryboard(sb);
    }
}
