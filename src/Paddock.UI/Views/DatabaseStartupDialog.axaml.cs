using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Paddock.UI.ViewModels;

namespace Paddock.UI.Views;

public partial class DatabaseStartupDialog : UserControl
{
    public DatabaseStartupDialog()
    {
        InitializeComponent();
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

            if (file != null && DataContext is DatabaseStartupDialogViewModel vm)
            {
                var chosenPath = file.Path.LocalPath;
                var relPath = System.IO.Path.GetRelativePath(AppContext.BaseDirectory, chosenPath);
                var pathToUse = (!relPath.StartsWith("..\\..\\..") && !relPath.StartsWith("../../../")) ? relPath : chosenPath;
                await vm.SelectDatabasePathAsync(pathToUse);
            }
        }
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

            if (files.Count > 0 && DataContext is DatabaseStartupDialogViewModel vm)
            {
                var chosenPath = files[0].Path.LocalPath;
                var relPath = System.IO.Path.GetRelativePath(AppContext.BaseDirectory, chosenPath);
                var pathToUse = (!relPath.StartsWith("..\\..\\..") && !relPath.StartsWith("../../../")) ? relPath : chosenPath;
                await vm.SelectDatabasePathAsync(pathToUse);
            }
        }
    }
}

