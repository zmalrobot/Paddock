using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Paddock.Updater.ViewModels;
using Paddock.Updater.Views;

namespace Paddock.Updater;

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
            var args = desktop.Args ?? Array.Empty<string>();
            var vm = UpdaterMainWindowViewModel.FromArgs(args);

            desktop.MainWindow = new UpdaterMainWindow
            {
                DataContext = vm
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
