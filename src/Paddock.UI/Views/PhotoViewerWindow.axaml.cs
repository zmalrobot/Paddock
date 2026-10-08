using Avalonia.Controls;
using Avalonia.Input;
using Paddock.UI.ViewModels;

namespace Paddock.UI.Views;

public partial class PhotoViewerWindow : Window
{
    public PhotoViewerWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is PhotoViewerViewModel vm)
        {
            vm.RequestClose += () => Close();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (DataContext is PhotoViewerViewModel vm)
        {
            if (vm.IsWatermarkDialogOpen)
            {
                if (e.Key == Key.Escape)
                {
                    vm.CloseWatermarkDialog();
                    e.Handled = true;
                }
                return;
            }

            if (e.Key == Key.Escape)
            {
                Close();
            }
            else if (e.Key == Key.Left)
            {
                vm.PreviousPhoto();
            }
            else if (e.Key == Key.Right)
            {
                vm.NextPhoto();
            }
            else if (e.Key == Key.Delete)
            {
                _ = vm.DeleteCurrentPhotoAsync();
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (DataContext is PhotoViewerViewModel vm)
        {
            vm.Dispose();
        }
    }
}

