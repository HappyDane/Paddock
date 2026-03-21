using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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

        // With AllowsTransparency=True and Background="Transparent" on the
        // Window, WPF creates a layered window. Pixels with alpha=0 are
        // automatically click-through at the Win32 level. PaddockControls
        // have non-transparent backgrounds, so they intercept clicks normally.
        //
        // The Canvas uses Background="Transparent" so that WPF routed events
        // (like MouseRightButtonDown for the context menu) still fire when the
        // user right-clicks empty space. Left-clicks on empty space pass
        // through to the desktop because the layered window's per-pixel
        // alpha is 0 in those areas.
        //
        // No Win32 WS_EX_TRANSPARENT style is needed here -- that would
        // disable hit-testing for PaddockControls too.
    }

    private void Canvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Only show the "New Paddock" menu when clicking on empty canvas space,
        // not when right-clicking a PaddockControl (which has its own context menu).
        var hit = VisualTreeHelper.HitTest(PaddockCanvas, e.GetPosition(PaddockCanvas));
        if (hit?.VisualHit is not null && IsInsidePaddockControl(hit.VisualHit))
            return;

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

    /// <summary>
    /// Walks up the visual tree to check whether the hit element is inside
    /// a <see cref="PaddockControl"/>.
    /// </summary>
    private static bool IsInsidePaddockControl(DependencyObject element)
    {
        var current = element;
        while (current is not null)
        {
            if (current is PaddockControl)
                return true;
            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }
}
