using System.Windows;
using System.Windows.Input;
using Paddock.App.ViewModels;

namespace Paddock.App.Views;

public partial class DesktopOverlay : Window
{
    private readonly DesktopOverlayViewModel _viewModel;

    public DesktopOverlay()
    {
        InitializeComponent();

        _viewModel = new DesktopOverlayViewModel();
        DataContext = _viewModel;

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // TODO: Embed this window into the desktop shell (WorkerW)
        // TODO: Load saved paddocks and render them on the canvas
        _viewModel.Initialize(PaddockCanvas);
    }

    private void Canvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Show context menu for creating new paddocks
        var menu = new System.Windows.Controls.ContextMenu();

        var createItem = new System.Windows.Controls.MenuItem { Header = "New Paddock" };
        createItem.Click += (_, _) =>
        {
            var position = e.GetPosition(PaddockCanvas);
            _viewModel.BeginCreatePaddock(position);
        };

        menu.Items.Add(createItem);
        menu.IsOpen = true;
    }
}
