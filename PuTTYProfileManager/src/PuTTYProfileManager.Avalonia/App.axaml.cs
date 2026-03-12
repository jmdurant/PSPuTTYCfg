using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PuTTYProfileManager.Avalonia.Views;
using PuTTYProfileManager.Avalonia.ViewModels;
using PuTTYProfileManager.Core.Services;

namespace PuTTYProfileManager.Avalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ISessionService sessionService;

            if (OperatingSystem.IsWindows())
                sessionService = new RegistrySessionService();
            else
                sessionService = new LinuxSessionService();

            var archiveService = new SessionArchiveService();
            var vm = new MainWindowViewModel(sessionService, archiveService);

            desktop.MainWindow = new MainWindow
            {
                DataContext = vm
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
