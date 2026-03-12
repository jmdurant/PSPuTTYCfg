using CommunityToolkit.Mvvm.ComponentModel;
using PuTTYProfileManager.Core.Services;

namespace PuTTYProfileManager.Avalonia.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _statusMessage = "Ready";

    public BackupViewModel Backup { get; }
    public RestoreViewModel Restore { get; }

    public MainWindowViewModel(ISessionService sessionService, ISessionArchiveService archiveService)
    {
        Backup = new BackupViewModel(sessionService, archiveService, s => StatusMessage = s);
        Restore = new RestoreViewModel(sessionService, archiveService, s => StatusMessage = s);
    }
}
