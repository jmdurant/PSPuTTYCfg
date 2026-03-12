using System.Windows;
using PuTTYProfileManager.Services;
using PuTTYProfileManager.ViewModels;

namespace PuTTYProfileManager.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var registryService = new SessionRegistryService();
        var archiveService = new SessionArchiveService();
        DataContext = new MainWindowViewModel(registryService, archiveService);
    }
}
