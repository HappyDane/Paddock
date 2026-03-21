using Paddock.Core.Models;

namespace Paddock.Core.Services;

/// <summary>
/// Handles paddock positioning, snapping, and layout presets.
/// </summary>
public class LayoutEngine
{
    private const double SnapDistance = 20.0;

    /// <summary>
    /// Snap a paddock's position to nearby edges (screen edges or other paddocks).
    /// </summary>
    public (double X, double Y) SnapToEdges(
        PaddockModel paddock,
        IReadOnlyList<PaddockModel> allPaddocks,
        double screenWidth,
        double screenHeight)
    {
        var x = paddock.X;
        var y = paddock.Y;

        // Snap to screen edges
        if (Math.Abs(x) < SnapDistance) x = 0;
        if (Math.Abs(y) < SnapDistance) y = 0;
        if (Math.Abs(x + paddock.Width - screenWidth) < SnapDistance)
            x = screenWidth - paddock.Width;
        if (Math.Abs(y + paddock.Height - screenHeight) < SnapDistance)
            y = screenHeight - paddock.Height;

        // Snap to other paddocks
        foreach (var other in allPaddocks)
        {
            if (other.Id == paddock.Id) continue;

            // Snap to right edge of other
            if (Math.Abs(x - (other.X + other.Width)) < SnapDistance)
                x = other.X + other.Width;

            // Snap to left edge of other
            if (Math.Abs(x + paddock.Width - other.X) < SnapDistance)
                x = other.X - paddock.Width;

            // Snap to bottom edge of other
            if (Math.Abs(y - (other.Y + other.Height)) < SnapDistance)
                y = other.Y + other.Height;

            // Snap to top edge of other
            if (Math.Abs(y + paddock.Height - other.Y) < SnapDistance)
                y = other.Y - paddock.Height;
        }

        return (x, y);
    }

    /// <summary>
    /// Clamp paddock positions to remain within screen bounds after a resolution change.
    /// </summary>
    public void ClampToScreen(
        IList<PaddockModel> paddocks,
        double screenWidth,
        double screenHeight)
    {
        foreach (var paddock in paddocks)
        {
            if (paddock.X + paddock.Width > screenWidth)
                paddock.X = Math.Max(0, screenWidth - paddock.Width);

            if (paddock.Y + paddock.Height > screenHeight)
                paddock.Y = Math.Max(0, screenHeight - paddock.Height);

            if (paddock.X < 0) paddock.X = 0;
            if (paddock.Y < 0) paddock.Y = 0;
        }
    }
}
