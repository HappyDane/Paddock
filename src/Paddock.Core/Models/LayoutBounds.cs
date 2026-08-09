namespace Paddock.Core.Models;

/// <summary>
/// A rectangular region used for snapping and clamping — typically a single
/// monitor's work area, expressed in device-independent pixels. Left/Top can be
/// negative when a monitor sits to the left of or above the primary one.
/// </summary>
public readonly record struct LayoutBounds(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;

    public bool Contains(double x, double y)
        => x >= Left && x < Right && y >= Top && y < Bottom;

    /// <summary>Bounds anchored at the origin — the single-screen shorthand.</summary>
    public static LayoutBounds FromSize(double width, double height)
        => new(0, 0, width, height);
}
