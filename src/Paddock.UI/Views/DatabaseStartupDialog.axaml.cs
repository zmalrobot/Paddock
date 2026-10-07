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

            if (file != null && DataContext is DatabaseStartupDialogViewModel vm)
            {
                await vm.SelectDatabasePathAsync(file.Path.LocalPath);
            }
        }
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

            if (files.Count > 0 && DataContext is DatabaseStartupDialogViewModel vm)
            {
                await vm.SelectDatabasePathAsync(files[0].Path.LocalPath);
            }
        }
    }
}

