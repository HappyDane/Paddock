using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Paddock.App.ViewModels;

namespace Paddock.App.Views;

public partial class DesktopOverlay : Window
{
    private readonly DesktopOverlayViewModel _viewModel;

    // Draw-to-create state
    private bool _isDrawing;
    private Point _drawStart;
    private Rect _drawnRect;

    // Minimum size (in pixels) for a drawn rectangle to count as a valid area
    private const double MinDrawSize = 40;

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

        // Set window size to cover the entire virtual screen (all monitors).
        // VirtualScreen* properties span all monitors, including negative
        // offsets when a monitor is to the left of or above the primary one.
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        // Try to embed into the desktop shell (WorkerW)
        bool embedded = app.ShellHookService.EmbedInDesktop(handle);

        if (!embedded)
        {
            // Fall back to a normal transparent window
            Topmost = false;
        }

        _viewModel.Initialize(PaddockCanvas);
    }

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
            return;

        // Only trigger on empty canvas space, not inside a paddock
        var hit = VisualTreeHelper.HitTest(PaddockCanvas, e.GetPosition(PaddockCanvas));
        if (hit?.VisualHit is not null && IsInsidePaddockControl(hit.VisualHit))
            return;

        var app = (App)Application.Current;
        if (!app.Settings.Settings.QuickHideEnabled)
            return;

        _viewModel.ToggleQuickHide();
        e.Handled = true;
    }

    private void Canvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Dismiss any existing create popup first
        DismissCreatePopup();

        // Only start drawing when clicking on empty canvas space,
        // not when right-clicking a PaddockControl (which has its own context menu).
        var hit = VisualTreeHelper.HitTest(PaddockCanvas, e.GetPosition(PaddockCanvas));
        if (hit?.VisualHit is not null && IsInsidePaddockControl(hit.VisualHit))
            return;

        // Begin drawing a selection rectangle
        _drawStart = e.GetPosition(PaddockCanvas);
        _isDrawing = true;

        // Show the selection rectangle at zero size
        Canvas.SetLeft(SelectionRect, _drawStart.X);
        Canvas.SetTop(SelectionRect, _drawStart.Y);
        SelectionRect.Width = 0;
        SelectionRect.Height = 0;
        SelectionRect.Visibility = Visibility.Visible;

        // Capture mouse to track movement even outside the canvas
        PaddockCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDrawing)
            return;

        var current = e.GetPosition(PaddockCanvas);

        // Calculate the rectangle from start to current position
        double x = Math.Min(_drawStart.X, current.X);
        double y = Math.Min(_drawStart.Y, current.Y);
        double w = Math.Abs(current.X - _drawStart.X);
        double h = Math.Abs(current.Y - _drawStart.Y);

        // Clamp to canvas bounds
        if (x < 0) { w += x; x = 0; }
        if (y < 0) { h += y; y = 0; }
        if (x + w > PaddockCanvas.ActualWidth) w = PaddockCanvas.ActualWidth - x;
        if (y + h > PaddockCanvas.ActualHeight) h = PaddockCanvas.ActualHeight - y;

        Canvas.SetLeft(SelectionRect, x);
        Canvas.SetTop(SelectionRect, y);
        SelectionRect.Width = Math.Max(0, w);
        SelectionRect.Height = Math.Max(0, h);
    }

    private void Canvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDrawing)
            return;

        _isDrawing = false;
        PaddockCanvas.ReleaseMouseCapture();

        double w = SelectionRect.Width;
        double h = SelectionRect.Height;

        // Hide the selection rectangle
        SelectionRect.Visibility = Visibility.Collapsed;

        if (w >= MinDrawSize && h >= MinDrawSize)
        {
            // Save the drawn rect for creation
            double x = Canvas.GetLeft(SelectionRect);
            double y = Canvas.GetTop(SelectionRect);
            _drawnRect = new Rect(x, y, w, h);

            // Position the popup just below the bottom-right corner of the drawn area
            double popupX = x + w - 10;
            double popupY = y + h + 8;

            // Keep popup on screen
            CreatePopup.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double popupWidth = CreatePopup.DesiredSize.Width;
            double popupHeight = CreatePopup.DesiredSize.Height;

            if (popupX + popupWidth > PaddockCanvas.ActualWidth)
                popupX = PaddockCanvas.ActualWidth - popupWidth - 8;
            if (popupY + popupHeight > PaddockCanvas.ActualHeight)
                popupY = y - popupHeight - 8;

            Canvas.SetLeft(CreatePopup, popupX);
            Canvas.SetTop(CreatePopup, popupY);
            CreatePopup.Visibility = Visibility.Visible;

            e.Handled = true;
        }
        else
        {
            // Too small — treat as a simple right-click and show context menu
            ShowCanvasContextMenu(e);
        }
    }

    private void CreatePopup_Click(object sender, MouseButtonEventArgs e)
    {
        CreatePopup.Visibility = Visibility.Collapsed;

        _viewModel.BeginCreatePaddock(_drawnRect);
        e.Handled = true;
    }

    /// <summary>
    /// Creates a new paddock at the centre of the primary screen. Used by
    /// the tray menu so users always have a reliable creation entry point,
    /// even when the overlay window is embedded behind the shell and cannot
    /// receive right-click-drag input.
    /// </summary>
    public void CreatePaddockAtScreenCenter()
    {
        const double defaultWidth = 300;
        const double defaultHeight = 250;

        double canvasWidth = PaddockCanvas.ActualWidth > 0
            ? PaddockCanvas.ActualWidth
            : SystemParameters.PrimaryScreenWidth;
        double canvasHeight = PaddockCanvas.ActualHeight > 0
            ? PaddockCanvas.ActualHeight
            : SystemParameters.PrimaryScreenHeight;

        double x = Math.Max(0, (canvasWidth - defaultWidth) / 2);
        double y = Math.Max(0, (canvasHeight - defaultHeight) / 2);

        _viewModel.BeginCreatePaddock(new Rect(x, y, defaultWidth, defaultHeight));
    }

    private void DismissCreatePopup()
    {
        CreatePopup.Visibility = Visibility.Collapsed;
    }

    private void ShowCanvasContextMenu(MouseButtonEventArgs e)
    {
        var menu = new ContextMenu();

        var createItem = new MenuItem { Header = "New Paddock" };
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
