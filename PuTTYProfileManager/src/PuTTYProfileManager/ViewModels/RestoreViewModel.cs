using System.Collections.ObjectModel;
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

    public ObservableCollection<SelectableSession> ArchivedSessions { get; } = [];

    [ObservableProperty]
    private string? _archivePath;

    [ObservableProperty]
    private SelectableSession? _selectedSession;

    [ObservableProperty]
    private bool _isLoading;

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
        LoadArchive();
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

            var sessions = _archiveService.ImportFromZip(ArchivePath, _archivePassword);
            ArchivedSessions.Clear();

            foreach (var session in sessions.OrderBy(s => s.DisplayName))
            {
                var selectable = new SelectableSession(session, true);
                selectable.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(SelectableSession.IsSelected))
                        OnPropertyChanged(nameof(SelectedCount));
                };
                ArchivedSessions.Add(selectable);
            }

            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(SelectedCount));
            _setStatus($"Loaded {sessions.Count} session(s) from archive");
        }
        catch (Exception ex)
        {
            _archivePassword = null;
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
        try
        {
            foreach (var session in selected)
            {
                _registryService.WriteSession(session);
                restored++;
            }

            _setStatus($"Restored {restored} session(s) to registry");

            MessageBox.Show(
                $"Successfully restored {restored} session(s) to the registry.",
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
