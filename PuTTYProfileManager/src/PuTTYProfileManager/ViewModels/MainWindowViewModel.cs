using CommunityToolkit.Mvvm.ComponentModel;
using PuTTYProfileManager.Services;

namespace PuTTYProfileManager.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _statusMessage = "Ready";

    public BackupViewModel Backup { get; }
    public RestoreViewModel Restore { get; }

    public MainWindowViewModel(ISessionRegistryService registryService, ISessionArchiveService archiveService)
    {
        Backup = new BackupViewModel(registryService, archiveService, status => StatusMessage = status);
        Restore = new RestoreViewModel(registryService, archiveService, status => StatusMessage = status);
    }
}
