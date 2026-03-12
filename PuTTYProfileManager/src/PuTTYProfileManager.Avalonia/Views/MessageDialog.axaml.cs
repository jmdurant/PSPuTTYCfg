using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;

namespace PuTTYProfileManager.Avalonia.Views;

public enum MessageDialogButtons { Ok, YesNo }
public enum MessageDialogIcon { Info, Warning, Error }
public enum MessageDialogResult { Ok, Yes, No }

public partial class MessageDialog : Window
{
    public MessageDialogResult Result { get; private set; } = MessageDialogResult.No;

    public MessageDialog()
    {
        InitializeComponent();
    }

    public MessageDialog(string title, string message, MessageDialogButtons buttons, MessageDialogIcon icon) : this()
    {
        Title = title;

        MessageText.Text = message;

        IconText.Text = icon switch
        {
            MessageDialogIcon.Info => "\u2139\uFE0F",
            MessageDialogIcon.Warning => "\u26A0\uFE0F",
            MessageDialogIcon.Error => "\u274C",
            _ => ""
        };

        if (buttons == MessageDialogButtons.Ok)
        {
            OkButton.IsVisible = true;
        }
        else
        {
            YesButton.IsVisible = true;
            NoButton.IsVisible = true;
        }

        // Auto-size height based on message length
        if (message.Length > 150 || message.Count(c => c == '\n') > 3)
            Height = 300;
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        Result = MessageDialogResult.Ok;
        Close();
    }

    private void OnYes(object? sender, RoutedEventArgs e)
    {
        Result = MessageDialogResult.Yes;
        Close();
    }

    private void OnNo(object? sender, RoutedEventArgs e)
    {
        Result = MessageDialogResult.No;
        Close();
    }

    public static async Task<MessageDialogResult> ShowAsync(
        string title, string message,
        MessageDialogButtons buttons = MessageDialogButtons.Ok,
        MessageDialogIcon icon = MessageDialogIcon.Info)
    {
        var mainWindow = (Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        var dialog = new MessageDialog(title, message, buttons, icon);

        if (mainWindow is not null)
            await dialog.ShowDialog(mainWindow);
        else
            dialog.Show();

        return dialog.Result;
    }
}
