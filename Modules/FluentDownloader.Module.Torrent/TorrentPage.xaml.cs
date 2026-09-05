using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentDownloader.Contracts;
using Microsoft.Win32;

namespace FluentDownloader.Module.Torrent;

public partial class TorrentViewModel : ObservableObject
{
    private readonly ITaskManager _taskManager;

    public TorrentViewModel(ITaskManager taskManager) => _taskManager = taskManager;

    [ObservableProperty]
    private string _magnetInput = string.Empty;

    [ObservableProperty]
    private string? _status;

    [RelayCommand]
    private void AddMagnet()
    {
        var input = MagnetInput.Trim();
        if (input.Length == 0)
        {
            Status = "请先粘贴磁力链";
            return;
        }

        try
        {
            _taskManager.Enqueue(new DownloadRequest
            {
                Url = input,
                KindHint = DownloadKind.Torrent,
            });
            Status = null;
            MagnetInput = string.Empty;
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }

    [RelayCommand]
    private void BrowseTorrent()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择种子文件",
            Filter = "种子文件 (*.torrent)|*.torrent|所有文件 (*.*)|*.*",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            _taskManager.Enqueue(new DownloadRequest
            {
                Url = dialog.FileName,
                KindHint = DownloadKind.Torrent,
            });
            Status = null;
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }
}

public partial class TorrentPage
{
    public TorrentPage(TorrentViewModel viewModel)
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

/// <summary>字符串非空 → Visible。</summary>
public sealed class NotEmptyToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
