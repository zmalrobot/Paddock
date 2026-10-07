using Avalonia.Controls;
using Avalonia.Platform;

namespace Paddock.UI;

public partial class MainWindow : Window
{
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
    }
}