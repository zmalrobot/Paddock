using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Paddock.UI.ViewModels;

namespace Paddock.UI.Views;

public partial class RawConversionDialog : UserControl
{
    public RawConversionDialog()
    {
        InitializeComponent();
    }

    private async void BrowseWatermark_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Seleziona Logo Watermark (PNG)",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Immagini PNG") { Patterns = new[] { "*.png" } }
            }
        });

        if (files.Count > 0 && DataContext is RawConversionDialogViewModel vm)
        {
            vm.WatermarkImagePath = files[0].Path.LocalPath;
        }
    }
}

