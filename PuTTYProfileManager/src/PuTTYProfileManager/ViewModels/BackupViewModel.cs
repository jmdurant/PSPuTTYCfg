using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PuTTYProfileManager.Models;
using PuTTYProfileManager.Services;
using PuTTYProfileManager.Views;

namespace PuTTYProfileManager.ViewModels;

public partial class BackupViewModel : ObservableObject
{
    private readonly ISessionRegistryService _registryService;
    private readonly ISessionArchiveService _archiveService;
    private readonly Action<string> _setStatus;

    public ObservableCollection<SelectableSession> Sessions { get; } = [];

    [ObservableProperty]
    private SelectableSession? _selectedSession;

    [ObservableProperty]
    private bool _isLoading;

    public int SelectedCount => Sessions.Count(s => s.IsSelected);
    public int TotalCount => Sessions.Count;

    public BackupViewModel(ISessionRegistryService registryService, ISessionArchiveService archiveService, Action<string> setStatus)
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
                        OnPropertyChanged(nameof(SelectedCount));
                };
                Sessions.Add(selectable);
            }

            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(SelectedCount));
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
            _archiveService.ExportToZip(dialog.FileName, selected, password);
            var protectedText = password is not null ? " (password protected)" : "";
            _setStatus($"Backed up {selected.Count} session(s) to {dialog.FileName}{protectedText}");

            MessageBox.Show(
                $"Successfully backed up {selected.Count} session(s).{protectedText}",
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
