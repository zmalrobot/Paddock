using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Paddock.Infrastructure.Configuration;
using Paddock.Infrastructure.Excel;
using Paddock.Infrastructure.Hardware;
using Paddock.Infrastructure.ImageProcessing;
using Paddock.Infrastructure.Ingestion;
using Paddock.Infrastructure.Metadata;
using Paddock.Infrastructure.Services;
using Paddock.Infrastructure.Storage;
using Paddock.UI.ViewModels;

namespace Paddock.UI;

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
            var prefsService = new AppPreferencesService();
            prefsService.Load();

            var initialDb = prefsService.LastDatabasePath;
            var dbRepo = new DatabaseRepositoryRouter(initialDb);
            var fileOrgService = new FileOrganizationService();
            var imageService = new ImageSharpProcessingService();
            var metadataService = new ExifToolMetadataService();
            var sdWatcherService = new SdCardWatcherService();
            var photoRenamerService = new PhotoRenamerService();
            var pipelineService = new ChannelIngestionPipelineService(
                fileOrgService,
                imageService,
                metadataService,
                dbRepo,
                photoRenamerService);

            var mainVm = new MainViewModel(
                dbRepo,
                fileOrgService,
                pipelineService,
                sdWatcherService,
                prefsService,
                imageService,
                metadataService);

            var mainWindow = new MainWindow
            {
                DataContext = mainVm,
                WindowState = Avalonia.Controls.WindowState.Maximized
            };

            mainWindow.Loaded += async (s, e) =>
            {
                await mainVm.InitializeAsync();
            };

            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}