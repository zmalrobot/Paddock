using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Paddock.UI.ViewModels;

namespace Paddock.UI.Views;

public partial class PhotoWatermarkDialog : UserControl
{
    public PhotoWatermarkDialog()
    {
        InitializeComponent();
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

            if (files.Count > 0 && DataContext is PhotoWatermarkDialogViewModel vm)
            {
                vm.WatermarkImagePath = files[0].Path.LocalPath;
            }
        }
    }
}

