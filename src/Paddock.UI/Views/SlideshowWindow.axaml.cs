using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Paddock.UI.ViewModels;

namespace Paddock.UI.Views;

public partial class SlideshowWindow : Window
{
    public SlideshowWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is SlideshowWindowViewModel vm)
        {
            UpdateTransitions(vm.TransitionsEnabled);
            vm.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(SlideshowWindowViewModel.TransitionsEnabled))
                {
                    UpdateTransitions(vm.TransitionsEnabled);
                }
            };
        }
    }

    private void UpdateTransitions(bool enabled)
    {
        try
        {
            if (enabled)
            {
                LayerA.Transitions = new Transitions
                {
                    new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(700) }
                };
                LayerB.Transitions = new Transitions
                {
                    new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(700) }
                };
            }
            else
            {
                LayerA.Transitions = null;
                LayerB.Transitions = null;
            }
        }
        catch
        {
            // Fail-safe: se fallisce l'assegnazione delle transizioni, rimuovile completamente
            LayerA.Transitions = null;
            LayerB.Transitions = null;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Close();
        }
        else if (e.Key == Key.Right || e.Key == Key.Space)
        {
            if (DataContext is SlideshowWindowViewModel vm)
            {
                _ = vm.NextPhotoManualAsync();
            }
        }
        else if (e.Key == Key.Left)
        {
            if (DataContext is SlideshowWindowViewModel vm)
            {
                _ = vm.PreviousPhotoManualAsync();
            }
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
