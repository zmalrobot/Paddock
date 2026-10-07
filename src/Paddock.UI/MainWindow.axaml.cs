using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Paddock.Core.Models;
using Paddock.UI.ViewModels;
using Paddock.UI.Views;

namespace Paddock.UI;

public partial class MainWindow : Window
{
    private SlideshowWindow? _activeSlideshowWindow;

    public MainWindow()
    {
        InitializeComponent();

        try
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Paddock.UI/Assets/logo.jpg")));
        }
        catch
        {
            // Fallback gestito da XAML Icon="/Assets/logo.jpg"
        }

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel mainVm)
        {
            mainVm.GetAvailableScreens = () =>
            {
                var list = new List<DisplayScreenInfo>();
                var all = Screens.All;
                for (int i = 0; i < all.Count; i++)
                {
                    var sc = all[i];
                    list.Add(new DisplayScreenInfo
                    {
                        Index = i,
                        DisplayName = string.IsNullOrWhiteSpace(sc.DisplayName) ? $"Schermo {i + 1}" : sc.DisplayName,
                        BoundsX = sc.Bounds.X,
                        BoundsY = sc.Bounds.Y,
                        Width = sc.Bounds.Width,
                        Height = sc.Bounds.Height,
                        IsPrimary = sc.IsPrimary
                    });
                }
                return list;
            };

            mainVm.RequestLaunchSlideshow += config =>
            {
                var all = Screens.All;
                var target = all.FirstOrDefault(s => s.Bounds.X == config.TargetScreen?.BoundsX && s.Bounds.Y == config.TargetScreen?.BoundsY)
                    ?? all.FirstOrDefault();

                var vm = new SlideshowWindowViewModel(config, mainVm.ExcelRepo);
                var win = new SlideshowWindow
                {
                    DataContext = vm
                };

                if (target != null)
                {
                    win.Position = target.Bounds.Position;
                }

                win.WindowState = WindowState.FullScreen;

                win.Closed += (s, ev) =>
                {
                    mainVm.IsSlideshowActive = false;
                    _activeSlideshowWindow = null;
                };

                _activeSlideshowWindow = win;
                mainVm.IsSlideshowActive = true;
                win.Show();
                _ = vm.StartAsync();
            };

            mainVm.RequestStopSlideshow += () =>
            {
                _activeSlideshowWindow?.Close();
                _activeSlideshowWindow = null;
                mainVm.IsSlideshowActive = false;
            };

            mainVm.RequestOpenPhotoViewer += vm =>
            {
                var viewerWin = new PhotoViewerWindow
                {
                    DataContext = vm
                };
                viewerWin.Show();
            };
        }
    }
}