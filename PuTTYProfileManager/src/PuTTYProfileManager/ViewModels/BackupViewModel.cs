using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PuTTYProfileManager.Core.Models;
using PuTTYProfileManager.Core.Services;
using PuTTYProfileManager.Models;
using PuTTYProfileManager.Views;

namespace PuTTYProfileManager.ViewModels;

public partial class BackupViewModel : ObservableObject
{
    private readonly ISessionService _registryService;
    private readonly ISessionArchiveService _archiveService;
    private readonly Action<string> _setStatus;

    public ObservableCollection<SelectableSession> Sessions { get; } = [];
    public ObservableCollection<LinkedFile> LinkedFiles { get; } = [];

    [ObservableProperty]
    private SelectableSession? _selectedSession;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _includeLinkedFiles = true;

    public int SelectedCount => Sessions.Count(s => s.IsSelected);
    public int TotalCount => Sessions.Count;
    public int LinkedFileCount => LinkedFiles.Count(f => f.Exists);

    public BackupViewModel(ISessionService registryService, ISessionArchiveService archiveService, Action<string> setStatus)
    {
        _registryService = registryService;
        _archiveService = archiveService;
        _setStatus = setStatus;

        // Auto-refresh on load
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        IsLoading = true;
        _setStatus("Loading sessions from registry...");

        try
        {
            var sessions = _registryService.GetAllSessions();
            Sessions.Clear();

            foreach (var session in sessions.OrderBy(s => s.DisplayName))
            {
                var selectable = new SelectableSession(session, true);
                selectable.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(SelectableSession.IsSelected))
                    {
                        OnPropertyChanged(nameof(SelectedCount));
                        RefreshLinkedFiles();
                    }
                };
                Sessions.Add(selectable);
            }

            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(SelectedCount));
            RefreshLinkedFiles();
            _setStatus($"Loaded {sessions.Count} session(s) from registry");
        }
        catch (Exception ex)
        {
            _setStatus($"Error: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void RefreshLinkedFiles()
    {
        var selectedSessions = Sessions.Where(s => s.IsSelected).Select(s => s.Session);
        var files = LinkedFileService.GetLinkedFiles(selectedSessions);

        LinkedFiles.Clear();
        foreach (var f in files)
            LinkedFiles.Add(f);

        OnPropertyChanged(nameof(LinkedFileCount));
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var s in Sessions) s.IsSelected = true;
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var s in Sessions) s.IsSelected = false;
    }

    [RelayCommand]
    private void BackupSelected()
    {
        var selected = Sessions.Where(s => s.IsSelected).Select(s => s.Session).ToList();
        if (selected.Count == 0)
        {
            _setStatus("No sessions selected for backup");
            return;
        }

        // Warn about linked files
        if (IncludeLinkedFiles && LinkedFiles.Count > 0)
        {
            var missing = LinkedFiles.Where(f => !f.Exists).ToList();
            if (missing.Count > 0)
            {
                var missingList = string.Join("\n", missing.Select(f => $"  - {f.FileName} ({f.SettingLabel})"));
                MessageBox.Show(
                    $"The following linked files were not found and will be skipped:\n\n{missingList}",
                    "Missing Files",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        var dialog = new SaveFileDialog
        {
            Title = "Save PuTTY Profile Backup",
            Filter = "ZIP Archive (*.zip)|*.zip",
            DefaultExt = ".zip",
            FileName = $"PuTTY_Backup_{DateTime.Now:yyyy-MM-dd_HHmmss}.zip"
        };

        if (dialog.ShowDialog() != true)
            return;

        // Ask about password protection
        string? password = null;
        var pwDialog = new PasswordDialog("Enter a password to protect the backup (or skip for no password):", allowSkip: true)
        {
            Owner = Application.Current.MainWindow
        };

        if (pwDialog.ShowDialog() == true && !pwDialog.Skipped && !string.IsNullOrEmpty(pwDialog.Password))
        {
            password = pwDialog.Password;
        }

        try
        {
            _archiveService.ExportToZip(dialog.FileName, selected, IncludeLinkedFiles, password);

            var fileCount = IncludeLinkedFiles ? LinkedFiles.Count(f => f.Exists) : 0;
            var protectedText = password is not null ? " (password protected)" : "";
            var filesText = fileCount > 0 ? $"\n{fileCount} linked file(s) included." : "";

            _setStatus($"Backed up {selected.Count} session(s) to {dialog.FileName}{protectedText}");

            MessageBox.Show(
                $"Successfully backed up {selected.Count} session(s).{filesText}{protectedText}",
                "Backup Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _setStatus($"Backup failed: {ex.Message}");
            MessageBox.Show($"Backup failed:\n{ex.Message}", "Backup Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
