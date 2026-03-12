using CommunityToolkit.Mvvm.ComponentModel;
using PuTTYProfileManager.Core.Services;

namespace PuTTYProfileManager.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _statusMessage = "Ready";

    public BackupViewModel Backup { get; }
    public RestoreViewModel Restore { get; }

    public MainWindowViewModel(ISessionService sessionService, ISessionArchiveService archiveService)
    {
        Backup = new BackupViewModel(sessionService, archiveService, status => StatusMessage = status);
        Restore = new RestoreViewModel(sessionService, archiveService, status => StatusMessage = status);
    }
}
