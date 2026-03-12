using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PuTTYProfileManager.Avalonia.Helpers;
using PuTTYProfileManager.Avalonia.Models;
using PuTTYProfileManager.Avalonia.Views;
using PuTTYProfileManager.Core.Models;
using PuTTYProfileManager.Core.Services;

namespace PuTTYProfileManager.Avalonia.ViewModels;

public partial class RestoreViewModel : ObservableObject
{
    private readonly ISessionService _sessionService;
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

    public RestoreViewModel(ISessionService sessionService, ISessionArchiveService archiveService, Action<string> setStatus)
    {
        _sessionService = sessionService;
        _archiveService = archiveService;
        _setStatus = setStatus;
    }

    [RelayCommand]
    private async Task BrowseArchiveAsync()
    {
        var path = await DialogHelper.OpenFileAsync(
            "Select PuTTY Profile Backup", "ZIP Archive", "*.zip");

        if (path is null) return;

        ArchivePath = path;
        _archivePassword = null;
        _archiveContents = null;
        await LoadArchiveAsync();
    }

    [RelayCommand]
    private async Task BrowseRestoreFolderAsync()
    {
        var folder = await DialogHelper.OpenFolderAsync(
            "Select folder to restore linked files", FileRestoreFolder);

        if (folder is not null)
            FileRestoreFolder = folder;
    }

    private async Task LoadArchiveAsync()
    {
        if (string.IsNullOrEmpty(ArchivePath))
            return;

        IsLoading = true;
        _setStatus("Loading archive...");

        try
        {
            if (_archivePassword is null && _archiveService.IsPasswordProtected(ArchivePath))
            {
                var (result, pw) = await PasswordDialog.ShowAsync(
                    "This archive is password protected. Enter the password:", allowSkip: false);

                if (result != PasswordDialogResult.Ok)
                {
                    _setStatus("Archive open cancelled");
                    return;
                }

                _archivePassword = pw;
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
            await MessageDialog.ShowAsync("Archive Error",
                $"Failed to read archive:\n{ex.Message}",
                MessageDialogButtons.Ok, MessageDialogIcon.Error);
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
    private async Task RestoreSelectedAsync()
    {
        var selected = ArchivedSessions.Where(s => s.IsSelected).Select(s => s.Session).ToList();
        if (selected.Count == 0)
        {
            _setStatus("No sessions selected for restore");
            return;
        }

        var existingNames = selected
            .Where(s => _sessionService.SessionExists(s.EncodedName))
            .Select(s => s.DisplayName)
            .ToList();

        if (existingNames.Count > 0)
        {
            var message = existingNames.Count == 1
                ? $"Session \"{existingNames[0]}\" already exists. Overwrite?"
                : $"{existingNames.Count} sessions already exist:\n\n{string.Join("\n", existingNames.Take(10))}" +
                  (existingNames.Count > 10 ? $"\n... and {existingNames.Count - 10} more" : "") +
                  "\n\nOverwrite all?";

            var confirm = await MessageDialog.ShowAsync("Confirm Overwrite", message,
                MessageDialogButtons.YesNo, MessageDialogIcon.Warning);

            if (confirm != MessageDialogResult.Yes)
            {
                _setStatus("Restore cancelled");
                return;
            }
        }

        var restored = 0;
        var filesExtracted = 0;

        try
        {
            if (HasLinkedFiles && _archiveContents?.FileMapping.Count > 0)
            {
                _archiveService.ExtractLinkedFiles(ArchivePath!, FileRestoreFolder, _archivePassword);
                filesExtracted = _archiveContents.LinkedFileEntries.Count;

                foreach (var session in selected)
                {
                    LinkedFileService.UpdateSessionPaths(session, FileRestoreFolder, _archiveContents.FileMapping);
                }
            }

            foreach (var session in selected)
            {
                _sessionService.WriteSession(session);
                restored++;
            }

            var filesMsg = filesExtracted > 0 ? $"\n{filesExtracted} linked file(s) extracted to {FileRestoreFolder}" : "";
            _setStatus($"Restored {restored} session(s)");

            await MessageDialog.ShowAsync("Restore Complete",
                $"Successfully restored {restored} session(s).{filesMsg}",
                MessageDialogButtons.Ok, MessageDialogIcon.Info);
        }
        catch (Exception ex)
        {
            _setStatus($"Restore failed after {restored} session(s): {ex.Message}");
            await MessageDialog.ShowAsync("Restore Error",
                $"Restore failed after {restored} session(s):\n{ex.Message}",
                MessageDialogButtons.Ok, MessageDialogIcon.Error);
        }
    }
}
