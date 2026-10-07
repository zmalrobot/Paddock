using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Paddock.UI.ViewModels;

namespace Paddock.UI.Views;

public partial class SlideshowWindow : Window
{
    public SlideshowWindow()
    {
        InitializeComponent();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (DataContext is SlideshowWindowViewModel vm)
        {
            vm.UserActivityDetected();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (DataContext is SlideshowWindowViewModel vm)
        {
            vm.UserActivityDetected();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (DataContext is SlideshowWindowViewModel vm)
        {
            vm.CloseSlideshow();
        }
    }
}

