using System.Collections.ObjectModel;
using System.Windows.Input;
using MeshWave.Wpf.Mvvm;
using MeshWave.Wpf.Services;

namespace MeshWave.Wpf.ViewModels;

public class DownloadManagerViewModel : ViewModelBase
{
    private readonly ApplicationViewModel _appViewModel;

    public DownloadManagerViewModel(ApplicationViewModel appViewModel)
    {
        _appViewModel = appViewModel;

        CancelDownloadCommand = new RelayCommand<DownloadQueueItem>(item =>
        {
            if (item != null)
            {
                _appViewModel.DownloadQueueItems.Remove(item);
            }
        }, item => item != null && !item.IsDone);

        PauseDownloadCommand = new RelayCommand<DownloadQueueItem>(item =>
        {
            if (item != null)
            {
                item.State = DownloadState.Paused;
            }
        }, item => item != null && (item.State == DownloadState.Downloading || item.State == DownloadState.Pending));

        ResumeDownloadCommand = new RelayCommand<DownloadQueueItem>(item =>
        {
            if (item != null)
            {
                item.State = DownloadState.Pending;
                // Currently, we just set it back to Pending. The actual retry logic would be picked up
                // by the background download job, or if it was interrupted we'd need more complex logic.
                // For UI purposes, setting to pending triggers the UI updates.
            }
        }, item => item != null && item.State == DownloadState.Paused);

        RetryDownloadCommand = new RelayCommand<DownloadQueueItem>(item =>
        {
            if (item != null)
            {
                item.State = DownloadState.Pending;
                item.StatusMessage = string.Empty;
                item.PercentComplete = 0;
            }
        }, item => item != null && item.IsFailed);

        ClearCompletedCommand = new RelayCommand(_ =>
        {
            var completedItems = _appViewModel.DownloadQueueItems.Where(i => i.IsDone).ToList();
            foreach (var item in completedItems)
            {
                _appViewModel.DownloadQueueItems.Remove(item);
            }
        });
    }

    public ObservableCollection<DownloadQueueItem> DownloadQueueItems => _appViewModel.DownloadQueueItems;

    public ICommand CancelDownloadCommand { get; }
    public ICommand PauseDownloadCommand { get; }
    public ICommand ResumeDownloadCommand { get; }
    public ICommand RetryDownloadCommand { get; }
    public ICommand ClearCompletedCommand { get; }
}
