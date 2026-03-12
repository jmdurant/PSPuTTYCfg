using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PuTTYProfileManager.Avalonia.Helpers;
using PuTTYProfileManager.Avalonia.Models;
using PuTTYProfileManager.Avalonia.Views;
using PuTTYProfileManager.Core.Models;
using PuTTYProfileManager.Core.Services;

namespace PuTTYProfileManager.Avalonia.ViewModels;

public partial class BackupViewModel : ObservableObject
{
    private readonly ISessionService _sessionService;
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

    public BackupViewModel(ISessionService sessionService, ISessionArchiveService archiveService, Action<string> setStatus)
    {
        _sessionService = sessionService;
        _archiveService = archiveService;
        _setStatus = setStatus;

        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        IsLoading = true;
        _setStatus("Loading sessions...");

        try
        {
            var sessions = _sessionService.GetAllSessions();
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
            _setStatus($"Loaded {sessions.Count} session(s)");
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
    private async Task BackupSelectedAsync()
    {
        var selected = Sessions.Where(s => s.IsSelected).Select(s => s.Session).ToList();
        if (selected.Count == 0)
        {
            _setStatus("No sessions selected for backup");
            return;
        }

        // Warn about missing linked files
        if (IncludeLinkedFiles && LinkedFiles.Count > 0)
        {
            var missing = LinkedFiles.Where(f => !f.Exists).ToList();
            if (missing.Count > 0)
            {
                var missingList = string.Join("\n", missing.Select(f => $"  - {f.FileName} ({f.SettingLabel})"));
                await MessageDialog.ShowAsync("Missing Files",
                    $"The following linked files were not found and will be skipped:\n\n{missingList}",
                    MessageDialogButtons.Ok, MessageDialogIcon.Warning);
            }
        }

        var path = await DialogHelper.SaveFileAsync(
            "Save PuTTY Profile Backup",
            "ZIP Archive", "*.zip",
            $"PuTTY_Backup_{DateTime.Now:yyyy-MM-dd_HHmmss}.zip");

        if (path is null) return;

        // Ask about password protection
        string? password = null;
        var (pwResult, pw) = await PasswordDialog.ShowAsync(
            "Enter a password to protect the backup (or skip for no password):", allowSkip: true);

        if (pwResult == PasswordDialogResult.Ok && !string.IsNullOrEmpty(pw))
            password = pw;

        try
        {
            _archiveService.ExportToZip(path, selected, IncludeLinkedFiles, password);

            var fileCount = IncludeLinkedFiles ? LinkedFiles.Count(f => f.Exists) : 0;
            var protectedText = password is not null ? " (password protected)" : "";
            var filesText = fileCount > 0 ? $"\n{fileCount} linked file(s) included." : "";

            _setStatus($"Backed up {selected.Count} session(s) to {path}{protectedText}");

            await MessageDialog.ShowAsync("Backup Complete",
                $"Successfully backed up {selected.Count} session(s).{filesText}{protectedText}",
                MessageDialogButtons.Ok, MessageDialogIcon.Info);
        }
        catch (Exception ex)
        {
            _setStatus($"Backup failed: {ex.Message}");
            await MessageDialog.ShowAsync("Backup Error",
                $"Backup failed:\n{ex.Message}",
                MessageDialogButtons.Ok, MessageDialogIcon.Error);
        }
    }
}
