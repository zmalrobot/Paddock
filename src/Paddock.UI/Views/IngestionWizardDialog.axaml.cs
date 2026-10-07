using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Paddock.UI.ViewModels;

namespace Paddock.UI.Views;

public partial class IngestionWizardDialog : UserControl
{
    public IngestionWizardDialog()
    {
        InitializeComponent();
    }

    private async void BrowseSourceFolder_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel != null)
        {
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Seleziona Scheda SD o Cartella DCIM Sorgente",
                AllowMultiple = false
            });

            if (folders.Count > 0 && DataContext is IngestionWizardViewModel vm)
            {
                vm.SourceDirectory = folders[0].Path.LocalPath;
            }
        }
    }

    private async void BrowseWatermarkFile_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel != null)
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Seleziona Immagine Watermark (PNG)",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("File Immagine (*.png, *.jpg, *.jpeg)")
                    {
                        Patterns = new[] { "*.png", "*.jpg", "*.jpeg" }
                    }
                }
            });

            if (files.Count > 0 && DataContext is IngestionWizardViewModel vm)
            {
                vm.WatermarkImagePath = files[0].Path.LocalPath;
            }
        }
    }
}
