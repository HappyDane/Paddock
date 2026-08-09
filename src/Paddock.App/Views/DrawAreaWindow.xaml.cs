using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Paddock.App.Helpers;

namespace Paddock.App.Views;

/// <summary>
/// Full-screen scrim shown while the user draws a new paddock.
///
/// This exists only for the moment of drawing. Paddock deliberately keeps no
/// permanent full-screen window, so that right-clicks, rubber-band selection and
/// icon drags on the bare desktop keep working exactly as Windows intends.
/// </summary>
public partial class DrawAreaWindow : Window
{
    /// <summary>Below this, a drag counts as a click and gets a default size.</summary>
    private const double MinDrawSize = 60;

    private const double DefaultPaddockWidth = 300;
    private const double DefaultPaddockHeight = 250;

    private bool _isDrawing;
    private Point _start;
    private Rect? _result;

    private DrawAreaWindow()
    {
        InitializeComponent();

        var screen = ScreenHelper.VirtualScreen;
        Left = screen.Left;
        Top = screen.Top;
        Width = screen.Width;
        Height = screen.Height;

        Loaded += OnLoaded;
    }

    /// <summary>
    /// Shows the scrim and returns the area the user drew, in screen
    /// coordinates (DIPs), or <c>null</c> if they cancelled.
    /// </summary>
    public static Rect? PickArea()
    {
        var window = new DrawAreaWindow();
        window.ShowDialog();
        return window._result;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Activate();
        Focus();

        HintPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(HintPanel, Math.Max(0, (Width - HintPanel.DesiredSize.Width) / 2));
        Canvas.SetTop(HintPanel, Math.Max(24, Height * 0.12));
    }

    private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(DrawCanvas);
        _isDrawing = true;

        HintPanel.Visibility = Visibility.Collapsed;

        Canvas.SetLeft(SelectionRect, _start.X);
        Canvas.SetTop(SelectionRect, _start.Y);
        SelectionRect.Width = 0;
        SelectionRect.Height = 0;
        SelectionRect.Visibility = Visibility.Visible;

        CaptureMouse();
        e.Handled = true;
    }

    private void Overlay_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDrawing)
            return;

        var current = e.GetPosition(DrawCanvas);

        var x = Math.Min(_start.X, current.X);
        var y = Math.Min(_start.Y, current.Y);
        var width = Math.Abs(current.X - _start.X);
        var height = Math.Abs(current.Y - _start.Y);

        // Keep the rectangle inside the drawing surface.
        if (x < 0) { width += x; x = 0; }
        if (y < 0) { height += y; y = 0; }
        if (x + width > DrawCanvas.ActualWidth) width = DrawCanvas.ActualWidth - x;
        if (y + height > DrawCanvas.ActualHeight) height = DrawCanvas.ActualHeight - y;

        Canvas.SetLeft(SelectionRect, x);
        Canvas.SetTop(SelectionRect, y);
        SelectionRect.Width = Math.Max(0, width);
        SelectionRect.Height = Math.Max(0, height);
    }

    private void Overlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDrawing)
            return;

        _isDrawing = false;
        ReleaseMouseCapture();

        var width = SelectionRect.Width;
        var height = SelectionRect.Height;
        var x = Canvas.GetLeft(SelectionRect);
        var y = Canvas.GetTop(SelectionRect);

        if (width < MinDrawSize || height < MinDrawSize)
        {
            // A plain click gets a default-sized paddock centred on the cursor.
            width = DefaultPaddockWidth;
            height = DefaultPaddockHeight;
            x = _start.X - width / 2;
            y = _start.Y - height / 2;
        }

        // Canvas coordinates are window-relative; shift them into screen space.
        _result = new Rect(x + Left, y + Top, width, height);

        e.Handled = true;
        Close();
    }

    private void Overlay_Cancel(object sender, MouseButtonEventArgs e)
    {
        _result = null;
        e.Handled = true;
        Close();
    }

    private void Overlay_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        _result = null;
        e.Handled = true;
        Close();
    }
}
