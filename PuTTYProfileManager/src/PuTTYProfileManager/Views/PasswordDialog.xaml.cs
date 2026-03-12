using System.Windows;

namespace PuTTYProfileManager.Views;

public partial class PasswordDialog : Window
{
    public string? Password { get; private set; }
    public bool Skipped { get; private set; }

    public string Prompt { get; }
    public Visibility SkipVisibility { get; }

    public PasswordDialog(string prompt, bool allowSkip = false)
    {
        Prompt = prompt;
        SkipVisibility = allowSkip ? Visibility.Visible : Visibility.Collapsed;
        DataContext = this;
        InitializeComponent();
        PasswordInput.Focus();
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Password = PasswordInput.Password;
        DialogResult = true;
    }

    private void OnSkip(object sender, RoutedEventArgs e)
    {
        Skipped = true;
        Password = null;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
