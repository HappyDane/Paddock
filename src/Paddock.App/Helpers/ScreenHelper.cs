using System.Windows;
using Paddock.Core.Models;
using Screen = System.Windows.Forms.Screen;

namespace Paddock.App.Helpers;

/// <summary>
/// Bridges Win32 screen geometry (physical pixels) and WPF window geometry
/// (device-independent pixels).
///
/// Paddock windows are positioned with <see cref="Window.Left"/> /
/// <see cref="Window.Top"/>, which are DIPs measured from the primary monitor's
/// top-left corner — so every monitor rectangle we compare against has to be in
/// the same units.
/// </summary>
internal static class ScreenHelper
{
    /// <summary>Physical pixels per device-independent pixel.</summary>
    internal static double DipScale
    {
        get
        {
            var primary = Screen.PrimaryScreen;
            var logicalWidth = SystemParameters.PrimaryScreenWidth;

            if (primary is null || logicalWidth <= 0)
                return 1.0;

            var scale = primary.Bounds.Width / logicalWidth;
            return scale > 0 ? scale : 1.0;
        }
    }

    /// <summary>Current mouse position in DIPs, valid across all monitors.</summary>
    internal static Point GetCursorPosition()
    {
        var position = System.Windows.Forms.Control.MousePosition;
        var scale = DipScale;
        return new Point(position.X / scale, position.Y / scale);
    }

    /// <summary>The union of all monitors, in DIPs.</summary>
    internal static LayoutBounds VirtualScreen => new(
        SystemParameters.VirtualScreenLeft,
        SystemParameters.VirtualScreenTop,
        SystemParameters.VirtualScreenWidth,
        SystemParameters.VirtualScreenHeight);

    /// <summary>
    /// Work areas (screen minus taskbar) of every monitor, in DIPs.
    /// </summary>
    internal static IReadOnlyList<LayoutBounds> GetWorkAreas()
    {
        var scale = DipScale;
        var areas = new List<LayoutBounds>();

        foreach (var screen in Screen.AllScreens)
        {
            var area = screen.WorkingArea;
            areas.Add(new LayoutBounds(
                area.X / scale,
                area.Y / scale,
                area.Width / scale,
                area.Height / scale));
        }

        if (areas.Count == 0)
            areas.Add(new LayoutBounds(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight));

        return areas;
    }

    /// <summary>
    /// The work area a paddock belongs to: the monitor holding its centre, or
    /// failing that the one it overlaps most, or the primary monitor.
    /// </summary>
    internal static LayoutBounds GetWorkAreaFor(double x, double y, double width, double height)
    {
        var areas = GetWorkAreas();
        var centreX = x + width / 2;
        var centreY = y + height / 2;

        foreach (var area in areas)
        {
            if (area.Contains(centreX, centreY))
                return area;
        }

        var best = areas[0];
        var bestOverlap = 0.0;

        foreach (var area in areas)
        {
            var overlapWidth = Math.Min(x + width, area.Right) - Math.Max(x, area.Left);
            var overlapHeight = Math.Min(y + height, area.Bottom) - Math.Max(y, area.Top);
            if (overlapWidth <= 0 || overlapHeight <= 0)
                continue;

            var overlap = overlapWidth * overlapHeight;
            if (overlap > bestOverlap)
            {
                bestOverlap = overlap;
                best = area;
            }
        }

        return best;
    }

    /// <summary>The work area of the monitor the given paddock sits on.</summary>
    internal static LayoutBounds GetWorkAreaFor(PaddockModel paddock)
        => GetWorkAreaFor(paddock.X, paddock.Y, paddock.Width, paddock.Height);
}
