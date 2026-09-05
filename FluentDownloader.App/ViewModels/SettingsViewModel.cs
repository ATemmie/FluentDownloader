using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentDownloader.App.Services;
using FluentDownloader.Contracts;
using FluentDownloader.Core;

namespace FluentDownloader.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;

    public SettingsViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        Settings.PropertyChanged += (_, _) => _settingsService.Save();
    }

    public AppSettings Settings => _settingsService.Settings;

    [RelayCommand]
    private void BrowseDownloadDirectory()
    {
        var picked = App.PickFolder(Directory.Exists(Settings.DownloadDirectory) ? Settings.DownloadDirectory : null);
        if (picked != null) Settings.DownloadDirectory = picked;
    }

    [RelayCommand]
    private void OpenDataDirectory()
    {
        Directory.CreateDirectory(AppSettings.DataDirectory);
        System.Diagnostics.Process.Start("explorer.exe", AppSettings.DataDirectory);
    }

    [RelayCommand]
    private void ApplyTheme()
    {
        ThemeService.Apply(Settings);
        _settingsService.Save();
    }
}
