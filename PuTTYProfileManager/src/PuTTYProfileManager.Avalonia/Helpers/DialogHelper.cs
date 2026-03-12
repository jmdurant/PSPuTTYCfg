using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace PuTTYProfileManager.Avalonia.Helpers;

public static class DialogHelper
{
    public static Window? GetMainWindow()
    {
        return (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
    }

    public static async Task<string?> OpenFileAsync(string title, string filterName, string pattern)
    {
        var window = GetMainWindow();
        if (window is null) return null;

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(filterName) { Patterns = [pattern] }
            ]
        });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    public static async Task<string?> SaveFileAsync(string title, string filterName, string pattern, string? suggestedName = null)
    {
        var window = GetMainWindow();
        if (window is null) return null;

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            FileTypeChoices =
            [
                new FilePickerFileType(filterName) { Patterns = [pattern] }
            ]
        });

        return file?.Path.LocalPath;
    }

    public static async Task<string?> OpenFolderAsync(string title, string? initialFolder = null)
    {
        var window = GetMainWindow();
        if (window is null) return null;

        var options = new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        };

        if (initialFolder is not null)
        {
            try
            {
                options.SuggestedStartLocation = await window.StorageProvider.TryGetFolderFromPathAsync(initialFolder);
            }
            catch { /* ignore if folder doesn't exist */ }
        }

        var folders = await window.StorageProvider.OpenFolderPickerAsync(options);
        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }
}
