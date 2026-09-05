using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentDownloader.Contracts;

namespace FluentDownloader.App.ViewModels;

public partial class TasksViewModel : ObservableObject
{
    private readonly ITaskManager _taskManager;

    public TasksViewModel(ITaskManager taskManager)
    {
        _taskManager = taskManager;
    }

    public ObservableCollection<DownloadTask> Tasks => _taskManager.Tasks;

    [RelayCommand]
    private void Pause(DownloadTask task) => _taskManager.Pause(task);

    [RelayCommand]
    private void Resume(DownloadTask task) => _taskManager.Resume(task);

    [RelayCommand]
    private void Cancel(DownloadTask task) => _taskManager.Cancel(task);

    [RelayCommand]
    private void Remove(DownloadTask task) => _taskManager.Remove(task);

    [RelayCommand]
    private void Reveal(DownloadTask task) => _taskManager.RevealInExplorer(task);
}
