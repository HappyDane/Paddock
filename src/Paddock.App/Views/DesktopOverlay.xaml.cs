using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Paddock.App.ViewModels;
using Paddock.Shell;

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
        var app = (App)Application.Current;
        var handle = new WindowInteropHelper(this).Handle;

        // Set window size to cover the primary screen working area
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left;
        Top = workArea.Top;
        Width = workArea.Width;
        Height = workArea.Height;

        // Try to embed into the desktop shell (WorkerW)
        bool embedded = app.ShellHookService.EmbedInDesktop(handle);

        if (!embedded)
        {
            // Fall back to a normal transparent window
            Topmost = false;
        }

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
