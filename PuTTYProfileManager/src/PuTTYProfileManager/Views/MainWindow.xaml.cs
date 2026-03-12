using System.Windows;
using PuTTYProfileManager.Core.Services;
using PuTTYProfileManager.ViewModels;

namespace PuTTYProfileManager.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var sessionService = new RegistrySessionService();
        var archiveService = new SessionArchiveService();
        DataContext = new MainWindowViewModel(sessionService, archiveService);
    }
}
