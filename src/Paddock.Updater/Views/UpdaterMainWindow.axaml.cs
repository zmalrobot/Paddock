using Avalonia.Controls;
using Avalonia.Interactivity;
using Paddock.Updater.ViewModels;

namespace Paddock.Updater.Views;

public partial class UpdaterMainWindow : Window
{
    public UpdaterMainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is UpdaterMainWindowViewModel vm)
        {
            vm.RequestClose += Close;
            _ = vm.RunUpdatePipelineAsync();
        }
    }
}
