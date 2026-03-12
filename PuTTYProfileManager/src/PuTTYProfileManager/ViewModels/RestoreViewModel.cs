using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PuTTYProfileManager.Models;
using PuTTYProfileManager.Services;
using PuTTYProfileManager.Views;

namespace PuTTYProfileManager.ViewModels;

public partial class RestoreViewModel : ObservableObject
{
    private readonly ISessionRegistryService _registryService;
    private readonly ISessionArchiveService _archiveService;
    private readonly Action<string> _setStatus;
    private string? _archivePassword;
    private ArchiveContents? _archiveContents;

    public ObservableCollection<SelectableSession> ArchivedSessions { get; } = [];

    [ObservableProperty]
    private string? _archivePath;

    [ObservableProperty]
    private SelectableSession? _selectedSession;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasLinkedFiles;

    [ObservableProperty]
    private int _linkedFileCount;

    [ObservableProperty]
    private string _fileRestoreFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");

    public int SelectedCount => ArchivedSessions.Count(s => s.IsSelected);
    public int TotalCount => ArchivedSessions.Count;

    public RestoreViewModel(ISessionRegistryService registryService, ISessionArchiveService archiveService, Action<string> setStatus)
    {
        _registryService = registryService;
        _archiveService = archiveService;
        _setStatus = setStatus;
    }

    [RelayCommand]
    private void BrowseArchive()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select PuTTY Profile Backup",
            Filter = "ZIP Archive (*.zip)|*.zip",
            DefaultExt = ".zip"
        };

        if (dialog.ShowDialog() != true)
            return;

        ArchivePath = dialog.FileName;
        _archivePassword = null;
        _archiveContents = null;
        LoadArchive();
    }

    [RelayCommand]
    private void BrowseRestoreFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select folder to restore linked files (PPK keys, certificates, etc.)",
            InitialDirectory = FileRestoreFolder
        };

        if (dialog.ShowDialog() == true)
        {
            FileRestoreFolder = dialog.FolderName;
        }
    }

    private void LoadArchive()
    {
        if (string.IsNullOrEmpty(ArchivePath))
            return;

        IsLoading = true;
        _setStatus("Loading archive...");

        try
        {
            // Check if password-protected
            if (_archivePassword is null && _archiveService.IsPasswordProtected(ArchivePath))
            {
                var pwDialog = new PasswordDialog("This archive is password protected. Enter the password:", allowSkip: false)
                {
                    Owner = Application.Current.MainWindow
                };

                if (pwDialog.ShowDialog() != true)
                {
                    _setStatus("Archive open cancelled");
                    return;
                }

                _archivePassword = pwDialog.Password;
            }

            _archiveContents = _archiveService.ImportFromZip(ArchivePath, _archivePassword);
            ArchivedSessions.Clear();

            foreach (var session in _archiveContents.Sessions.OrderBy(s => s.DisplayName))
            {
                var selectable = new SelectableSession(session, true);
                selectable.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(SelectableSession.IsSelected))
                        OnPropertyChanged(nameof(SelectedCount));
                };
                ArchivedSessions.Add(selectable);
            }

            HasLinkedFiles = _archiveContents.LinkedFileEntries.Count > 0;
            LinkedFileCount = _archiveContents.LinkedFileEntries.Count;

            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(SelectedCount));

            var filesMsg = HasLinkedFiles ? $" ({LinkedFileCount} linked file(s))" : "";
            _setStatus($"Loaded {_archiveContents.Sessions.Count} session(s) from archive{filesMsg}");
        }
        catch (Exception ex)
        {
            _archivePassword = null;
            _archiveContents = null;
            _setStatus($"Error reading archive: {ex.Message}");
            MessageBox.Show($"Failed to read archive:\n{ex.Message}", "Archive Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var s in ArchivedSessions) s.IsSelected = true;
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var s in ArchivedSessions) s.IsSelected = false;
    }

    [RelayCommand]
    private void RestoreSelected()
    {
        var selected = ArchivedSessions.Where(s => s.IsSelected).Select(s => s.Session).ToList();
        if (selected.Count == 0)
        {
            _setStatus("No sessions selected for restore");
            return;
        }

        var existingNames = selected
            .Where(s => _registryService.SessionExists(s.EncodedName))
            .Select(s => s.DisplayName)
            .ToList();

        if (existingNames.Count > 0)
        {
            var message = existingNames.Count == 1
                ? $"Session \"{existingNames[0]}\" already exists. Overwrite?"
                : $"{existingNames.Count} sessions already exist:\n\n{string.Join("\n", existingNames.Take(10))}" +
                  (existingNames.Count > 10 ? $"\n... and {existingNames.Count - 10} more" : "") +
                  "\n\nOverwrite all?";

            var result = MessageBox.Show(message, "Confirm Overwrite",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                _setStatus("Restore cancelled");
                return;
            }
        }

        var restored = 0;
        var filesExtracted = 0;

        try
        {
            // Extract linked files if present
            if (HasLinkedFiles && _archiveContents?.FileMapping.Count > 0)
            {
                _archiveService.ExtractLinkedFiles(ArchivePath!, FileRestoreFolder, _archivePassword);
                filesExtracted = _archiveContents.LinkedFileEntries.Count;

                // Update session paths to point to restored file locations
                foreach (var session in selected)
                {
                    LinkedFileService.UpdateSessionPaths(session, FileRestoreFolder, _archiveContents.FileMapping);
                }
            }

            foreach (var session in selected)
            {
                _registryService.WriteSession(session);
                restored++;
            }

            var filesMsg = filesExtracted > 0 ? $"\n{filesExtracted} linked file(s) extracted to {FileRestoreFolder}" : "";
            _setStatus($"Restored {restored} session(s) to registry");

            MessageBox.Show(
                $"Successfully restored {restored} session(s) to the registry.{filesMsg}",
                "Restore Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _setStatus($"Restore failed after {restored} session(s): {ex.Message}");
            MessageBox.Show($"Restore failed after {restored} session(s):\n{ex.Message}", "Restore Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
