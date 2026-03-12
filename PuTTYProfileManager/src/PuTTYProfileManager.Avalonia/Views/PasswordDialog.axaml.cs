using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;

namespace PuTTYProfileManager.Avalonia.Views;

public enum PasswordDialogResult { Ok, Skip, Cancel }

public partial class PasswordDialog : Window
{
    public string? Password { get; private set; }
    public PasswordDialogResult Result { get; private set; } = PasswordDialogResult.Cancel;

    public PasswordDialog()
    {
        InitializeComponent();
    }

    public PasswordDialog(string prompt, bool allowSkip) : this()
    {
        PromptText.Text = prompt;
        SkipButton.IsVisible = allowSkip;
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        Password = PasswordInput.Text;
        Result = PasswordDialogResult.Ok;
        Close();
    }

    private void OnSkip(object? sender, RoutedEventArgs e)
    {
        Result = PasswordDialogResult.Skip;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Result = PasswordDialogResult.Cancel;
        Close();
    }

    public static async Task<(PasswordDialogResult result, string? password)> ShowAsync(
        string prompt, bool allowSkip)
    {
        var mainWindow = (Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        var dialog = new PasswordDialog(prompt, allowSkip);

        if (mainWindow is not null)
            await dialog.ShowDialog(mainWindow);
        else
            dialog.Show();

        return (dialog.Result, dialog.Password);
    }
}
