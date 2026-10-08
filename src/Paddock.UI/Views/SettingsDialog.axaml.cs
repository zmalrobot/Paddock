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
            var containerDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, ".."));
            var defaultDbDir = System.IO.Path.Combine(containerDir, "Database");
            var startFolder = System.IO.Directory.Exists(defaultDbDir)
                ? await topLevel.StorageProvider.TryGetFolderFromPathAsync(defaultDbDir)
                : null;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Seleziona Database Excel (.xlsx)",
                AllowMultiple = false,
                SuggestedStartLocation = startFolder,
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
                var chosenPath = files[0].Path.LocalPath;
                var relPath = System.IO.Path.GetRelativePath(AppContext.BaseDirectory, chosenPath);
                var pathToUse = (!relPath.StartsWith("..\\..\\..") && !relPath.StartsWith("../../../")) ? relPath : chosenPath;
                await vm.SwitchDatabaseAsync(pathToUse);
            }
        }
    }

    private async void BrowseNewDatabase_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel != null)
        {
            var containerDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, ".."));
            var defaultDbDir = System.IO.Path.Combine(containerDir, "Database");
            if (!System.IO.Directory.Exists(defaultDbDir))
            {
                try { System.IO.Directory.CreateDirectory(defaultDbDir); } catch { /* ignore */ }
            }
            var startFolder = await topLevel.StorageProvider.TryGetFolderFromPathAsync(defaultDbDir);

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Crea Nuovo Database Excel",
                DefaultExtension = "xlsx",
                SuggestedFileName = "Paddock_Database.xlsx",
                SuggestedStartLocation = startFolder,
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
                var chosenPath = file.Path.LocalPath;
                var relPath = System.IO.Path.GetRelativePath(AppContext.BaseDirectory, chosenPath);
                var pathToUse = (!relPath.StartsWith("..\\..\\..") && !relPath.StartsWith("../../../")) ? relPath : chosenPath;
                await vm.SwitchDatabaseAsync(pathToUse);
            }
        }
    }

    private async void BrowseBasePath_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel != null)
        {
            var containerDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, ".."));
            var defaultFotoDir = System.IO.Path.Combine(containerDir, "Foto");
            var startFolder = System.IO.Directory.Exists(defaultFotoDir)
                ? await topLevel.StorageProvider.TryGetFolderFromPathAsync(defaultFotoDir)
                : null;

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Seleziona Cartella Radice BasePath per l'Archivio Foto",
                AllowMultiple = false,
                SuggestedStartLocation = startFolder
            });

            if (folders.Count > 0 && DataContext is SettingsViewModel vm)
            {
                var chosenPath = folders[0].Path.LocalPath;
                var refDir = !string.IsNullOrWhiteSpace(vm.ResolvedDatabasePath)
                    ? System.IO.Path.GetDirectoryName(vm.ResolvedDatabasePath) ?? AppContext.BaseDirectory
                    : AppContext.BaseDirectory;

                var relPath = System.IO.Path.GetRelativePath(refDir, chosenPath);
                var pathToUse = (!relPath.StartsWith("..\\..\\..") && !relPath.StartsWith("../../../")) ? relPath : chosenPath;
                vm.BasePath = pathToUse;
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

