using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Paddock.UI.ViewModels;

namespace Paddock.UI.Views;

public partial class EventEditDialog : UserControl
{
    public EventEditDialog()
    {
        InitializeComponent();
    }

    private async void BrowseDestinazioneRoot_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel != null && DataContext is EventEditDialogViewModel vm)
        {
            IStorageFolder? startFolder = null;
            if (!string.IsNullOrWhiteSpace(vm.ResolvedCartellaDestinazioneRoot) && System.IO.Directory.Exists(vm.ResolvedCartellaDestinazioneRoot))
            {
                startFolder = await topLevel.StorageProvider.TryGetFolderFromPathAsync(vm.ResolvedCartellaDestinazioneRoot);
            }
            else
            {
                var containerDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, ".."));
                var defaultFotoDir = System.IO.Path.Combine(containerDir, "Foto");
                if (System.IO.Directory.Exists(defaultFotoDir))
                {
                    startFolder = await topLevel.StorageProvider.TryGetFolderFromPathAsync(defaultFotoDir);
                }
            }

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Seleziona Cartella Radice Foto per l'Evento",
                AllowMultiple = false,
                SuggestedStartLocation = startFolder
            });

            if (folders.Count > 0)
            {
                var chosenPath = folders[0].Path.LocalPath;
                var containerDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, ".."));
                var defaultFotoDir = System.IO.Path.Combine(containerDir, "Foto");

                if (chosenPath.Equals(defaultFotoDir, StringComparison.OrdinalIgnoreCase))
                {
                    vm.CartellaDestinazioneRoot = @"..\Foto";
                }
                else
                {
                    var relPath = System.IO.Path.GetRelativePath(AppContext.BaseDirectory, chosenPath);
                    if (!relPath.StartsWith("..\\..\\..") && !relPath.StartsWith("../../../") && relPath.StartsWith(".."))
                    {
                        vm.CartellaDestinazioneRoot = relPath;
                    }
                    else
                    {
                        vm.CartellaDestinazioneRoot = chosenPath;
                    }
                }
            }
        }
    }
}
