using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Paddock.UI.ViewModels;

namespace Paddock.UI.Views;

public partial class SettingsDialog : UserControl
{
    public SettingsDialog()
    {
        InitializeComponent();
    }

    private async void BrowseOpenDatabase_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel != null)
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Seleziona Database Excel (.xlsx)",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("File Excel (*.xlsx)")
                    {
                        Patterns = new[] { "*.xlsx" }
                    }
                }
            });

            if (files.Count > 0 && DataContext is SettingsViewModel vm)
            {
                await vm.SwitchDatabaseAsync(files[0].Path.LocalPath);
            }
        }
    }

    private async void BrowseNewDatabase_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel != null)
        {
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Crea Nuovo Database Excel",
                DefaultExtension = "xlsx",
                SuggestedFileName = "Paddock_Database.xlsx",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("File Excel (*.xlsx)")
                    {
                        Patterns = new[] { "*.xlsx" }
                    }
                }
            });

            if (file != null && DataContext is SettingsViewModel vm)
            {
                await vm.SwitchDatabaseAsync(file.Path.LocalPath);
            }
        }
    }

    private async void BrowseBasePath_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel != null)
        {
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Seleziona Cartella Radice BasePath per l'Archivio Foto",
                AllowMultiple = false
            });

            if (folders.Count > 0 && DataContext is SettingsViewModel vm)
            {
                vm.BasePath = folders[0].Path.LocalPath;
            }
        }
    }

    private async void BrowseWatermarkImage_Click(object? sender, RoutedEventArgs e)
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

            if (files.Count > 0 && DataContext is SettingsViewModel vm)
            {
                vm.DefaultWatermarkImagePath = files[0].Path.LocalPath;
            }
        }
    }
}

