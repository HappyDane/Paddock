using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Paddock.App.Helpers;
using Paddock.Core.Services;

namespace Paddock.App.Views;

/// <summary>
/// The eyedropper: pick any colour on screen — typically off the wallpaper —
/// for a paddock or its icon tint.
///
/// Like <see cref="DrawAreaWindow"/> this is a full-screen window that exists
/// only for the moment it is needed. It shows a snapshot taken just before it
/// opened, so what you see is the real screen and the window itself is never
/// in the picture.
/// </summary>
public partial class EyedropperWindow : Window
{
    private const int ZoomPixels = 15;
    private const double LoupeOffset = 22;

    private readonly ScreenSnapshot _snapshot;
    private readonly ImageBrush _zoom;
    private Color? _current;
    private Color? _result;

    private EyedropperWindow(ScreenSnapshot snapshot)
    {
        InitializeComponent();

        _snapshot = snapshot;

        var screen = ScreenHelper.VirtualScreen;
        Left = screen.Left;
        Top = screen.Top;
        Width = screen.Width;
        Height = screen.Height;

        Snapshot.Source = snapshot.Image;

        _zoom = new ImageBrush(snapshot.Image)
        {
            ViewboxUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill,
        };
        RenderOptions.SetBitmapScalingMode(_zoom, BitmapScalingMode.NearestNeighbor);
        Zoom.Fill = _zoom;

        Loaded += OnLoaded;
    }

    /// <summary>Lets the user pick a colour on screen. Returns null if they cancel.</summary>
    public static Color? PickColor()
    {
        var snapshot = ScreenSnapshot.Capture();
        if (snapshot is null)
            return null;

        var window = new EyedropperWindow(snapshot);
        window.ShowDialog();

        if (window._result is { } picked)
            Log.Info($"Eyedropper picked {HexColor.Format(picked.R, picked.G, picked.B)}.");

        return window._result;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Activate();
        Focus();

        HintPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(HintPanel, Math.Max(0, (ActualWidth - HintPanel.DesiredSize.Width) / 2));
        Canvas.SetTop(HintPanel, Math.Max(24, ActualHeight * 0.12));

        Follow(Mouse.GetPosition(this));
    }

    private void OnMouseMove(object sender, MouseEventArgs e) => Follow(e.GetPosition(this));

    /// <summary>Moves the loupe to a point (window DIPs) and reads the pixel under it.</summary>
    private void Follow(Point point)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
            return;

        var x = (int)Math.Clamp(point.X * _snapshot.Width / ActualWidth, 0, _snapshot.Width - 1);
        var y = (int)Math.Clamp(point.Y * _snapshot.Height / ActualHeight, 0, _snapshot.Height - 1);

        var colour = _snapshot.ColorAt(x, y);
        _current = colour;

        _zoom.Viewbox = new Rect(x - ZoomPixels / 2, y - ZoomPixels / 2, ZoomPixels, ZoomPixels);
        Preview.Background = new SolidColorBrush(colour);
        HexText.Text = HexColor.Format(colour.R, colour.G, colour.B);

        Loupe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = Loupe.DesiredSize;

        // Below-right of the cursor, flipped where it would run off the screen.
        var left = point.X + LoupeOffset + size.Width > ActualWidth ? point.X - LoupeOffset - size.Width : point.X + LoupeOffset;
        var top = point.Y + LoupeOffset + size.Height > ActualHeight ? point.Y - LoupeOffset - size.Height : point.Y + LoupeOffset;
        Canvas.SetLeft(Loupe, Math.Max(0, left));
        Canvas.SetTop(Loupe, Math.Max(0, top));
    }

    private void OnPick(object sender, MouseButtonEventArgs e)
    {
        Follow(e.GetPosition(this));
        _result = _current;
        e.Handled = true;
        Close();
    }

    private void OnCancel(object sender, MouseButtonEventArgs e)
    {
        _result = null;
        e.Handled = true;
        Close();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                _result = null;
                Close();
                break;

            case Key.Enter:
            case Key.Space:
                _result = _current;
                Close();
                break;

            case Key.Left:
                Nudge(-1, 0);
                break;

            case Key.Right:
                Nudge(1, 0);
                break;

            case Key.Up:
                Nudge(0, -1);
                break;

            case Key.Down:
                Nudge(0, 1);
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>Moves the cursor by one physical pixel, for picking exactly the right one.</summary>
    private void Nudge(int dx, int dy)
    {
        var position = System.Windows.Forms.Cursor.Position;
        var moved = new System.Drawing.Point(position.X + dx, position.Y + dy);
        System.Windows.Forms.Cursor.Position = moved;

        // WPF hears about a programmatic move late; work from where it now is.
        Follow(PointFromScreen(new Point(moved.X, moved.Y)));
    }
}
