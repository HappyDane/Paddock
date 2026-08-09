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
    /// When <paramref name="spacingGap"/> is greater than zero, snaps to the spacing
    /// distance from edges and other paddocks instead of flush against them.
    /// </summary>
    public (double X, double Y) SnapToEdges(
        PaddockModel paddock,
        IReadOnlyList<PaddockModel> allPaddocks,
        double screenWidth,
        double screenHeight,
        double spacingGap = 0)
        => SnapToEdges(paddock, allPaddocks, LayoutBounds.FromSize(screenWidth, screenHeight), spacingGap);

    /// <summary>
    /// Snap a paddock's position to the edges of <paramref name="bounds"/> (the
    /// monitor it currently sits on) or to nearby paddocks.
    /// </summary>
    public (double X, double Y) SnapToEdges(
        PaddockModel paddock,
        IReadOnlyList<PaddockModel> allPaddocks,
        LayoutBounds bounds,
        double spacingGap = 0)
    {
        var x = paddock.X;
        var y = paddock.Y;

        // Snap to screen edges (respecting spacing gap)
        if (Math.Abs(x - (bounds.Left + spacingGap)) < SnapDistance)
            x = bounds.Left + spacingGap;
        if (Math.Abs(y - (bounds.Top + spacingGap)) < SnapDistance)
            y = bounds.Top + spacingGap;
        if (Math.Abs(x + paddock.Width - (bounds.Right - spacingGap)) < SnapDistance)
            x = bounds.Right - paddock.Width - spacingGap;
        if (Math.Abs(y + paddock.Height - (bounds.Bottom - spacingGap)) < SnapDistance)
            y = bounds.Bottom - paddock.Height - spacingGap;

        // Snap to other paddocks (respecting spacing gap)
        foreach (var other in allPaddocks)
        {
            if (other.Id == paddock.Id) continue;

            // Snap to right edge of other (with spacing)
            if (Math.Abs(x - (other.X + other.Width + spacingGap)) < SnapDistance)
                x = other.X + other.Width + spacingGap;

            // Snap to left edge of other (with spacing)
            if (Math.Abs(x + paddock.Width + spacingGap - other.X) < SnapDistance)
                x = other.X - paddock.Width - spacingGap;

            // Snap to bottom edge of other (with spacing)
            if (Math.Abs(y - (other.Y + other.Height + spacingGap)) < SnapDistance)
                y = other.Y + other.Height + spacingGap;

            // Snap to top edge of other (with spacing)
            if (Math.Abs(y + paddock.Height + spacingGap - other.Y) < SnapDistance)
                y = other.Y - paddock.Height - spacingGap;
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
        => ClampToBounds(paddocks, LayoutBounds.FromSize(screenWidth, screenHeight));

    /// <summary>
    /// Clamp paddock positions into the given region, keeping the whole paddock
    /// visible. Used on startup and whenever the monitor layout changes so a
    /// paddock never ends up parked off-screen.
    /// </summary>
    public void ClampToBounds(IList<PaddockModel> paddocks, LayoutBounds bounds)
    {
        foreach (var paddock in paddocks)
        {
            ClampToBounds(paddock, bounds);
        }
    }

    /// <summary>Clamps a single paddock into the given region.</summary>
    public void ClampToBounds(PaddockModel paddock, LayoutBounds bounds)
    {
        // Never grow a paddock beyond the region it has to fit into.
        paddock.Width = Math.Min(paddock.Width, bounds.Width);
        paddock.Height = Math.Min(paddock.Height, bounds.Height);

        if (paddock.X + paddock.Width > bounds.Right)
            paddock.X = Math.Max(bounds.Left, bounds.Right - paddock.Width);

        if (paddock.Y + paddock.Height > bounds.Bottom)
            paddock.Y = Math.Max(bounds.Top, bounds.Bottom - paddock.Height);

        if (paddock.X < bounds.Left) paddock.X = bounds.Left;
        if (paddock.Y < bounds.Top) paddock.Y = bounds.Top;
    }

    /// <summary>
    /// Adjusts positions of unlocked paddocks so that adjacent paddocks maintain the
    /// configured spacing from each other and from screen edges. Locked paddocks are
    /// never moved; other paddocks shift away from them when necessary.
    /// </summary>
    public void ApplyIntelligentSpacing(
        IList<PaddockModel> paddocks,
        double spacing,
        double screenWidth,
        double screenHeight)
    {
        for (var i = 0; i < paddocks.Count; i++)
        {
            var paddock = paddocks[i];
            if (paddock.Locked) continue;

            // Maintain margin from screen edges
            if (paddock.X < spacing)
                paddock.X = spacing;
            if (paddock.Y < spacing)
                paddock.Y = spacing;
            if (paddock.X + paddock.Width > screenWidth - spacing)
                paddock.X = screenWidth - paddock.Width - spacing;
            if (paddock.Y + paddock.Height > screenHeight - spacing)
                paddock.Y = screenHeight - paddock.Height - spacing;

            // Maintain spacing from the paddocks already placed. Only earlier
            // entries act as anchors, so a pass over the list settles instead of
            // both neighbours shoving each other in turn.
            for (var j = 0; j < i; j++)
            {
                var other = paddocks[j];
                if (other.Id == paddock.Id) continue;

                bool verticalOverlap = paddock.Y < other.Y + other.Height &&
                                       paddock.Y + paddock.Height > other.Y;
                bool horizontalOverlap = paddock.X < other.X + other.Width &&
                                         paddock.X + paddock.Width > other.X;

                if (verticalOverlap)
                {
                    double gapLeft = paddock.X - (other.X + other.Width);
                    double gapRight = other.X - (paddock.X + paddock.Width);

                    if (gapLeft >= 0 && gapLeft < spacing)
                    {
                        // Paddock sits to the right of the anchor but too close
                        paddock.X = other.X + other.Width + spacing;
                    }
                    else if (gapRight >= 0 && gapRight < spacing)
                    {
                        // Paddock sits to the left of the anchor but too close
                        paddock.X = other.X - paddock.Width - spacing;
                    }
                }

                if (horizontalOverlap)
                {
                    double gapAbove = paddock.Y - (other.Y + other.Height);
                    double gapBelow = other.Y - (paddock.Y + paddock.Height);

                    if (gapAbove >= 0 && gapAbove < spacing)
                    {
                        // Paddock sits below the anchor but too close
                        paddock.Y = other.Y + other.Height + spacing;
                    }
                    else if (gapBelow >= 0 && gapBelow < spacing)
                    {
                        // Paddock sits above the anchor but too close
                        paddock.Y = other.Y - paddock.Height - spacing;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Arranges unlocked paddocks according to a named preset layout.
    /// Locked paddocks are not moved.
    /// Supported presets: "2-column", "3-column", "sidebar", "quadrant".
    /// </summary>
    public void AutoArrange(
        IList<PaddockModel> paddocks,
        string preset,
        double screenWidth,
        double screenHeight,
        double spacing)
    {
        var movable = paddocks.Where(p => !p.Locked).ToList();
        if (movable.Count == 0) return;

        switch (preset)
        {
            case "2-column":
                ArrangeColumns(movable, 2, screenWidth, screenHeight, spacing);
                break;
            case "3-column":
                ArrangeColumns(movable, 3, screenWidth, screenHeight, spacing);
                break;
            case "sidebar":
                ArrangeSidebar(movable, screenWidth, screenHeight, spacing);
                break;
            case "quadrant":
                ArrangeQuadrant(movable, screenWidth, screenHeight, spacing);
                break;
            default:
                throw new ArgumentException($"Unknown layout preset: {preset}", nameof(preset));
        }
    }

    private static void ArrangeColumns(
        List<PaddockModel> paddocks,
        int columns,
        double screenWidth,
        double screenHeight,
        double spacing)
    {
        double totalSpacing = spacing * (columns + 1);
        double colWidth = Math.Max(100, (screenWidth - totalSpacing) / columns);
        double rowHeight = Math.Max(80, screenHeight - spacing * 2);

        for (int i = 0; i < paddocks.Count; i++)
        {
            int col = i % columns;
            int row = i / columns;
            int rowsInLayout = (paddocks.Count + columns - 1) / columns;

            if (rowsInLayout > 1)
                rowHeight = (screenHeight - spacing * (rowsInLayout + 1)) / rowsInLayout;

            paddocks[i].X = spacing + col * (colWidth + spacing);
            paddocks[i].Y = spacing + row * (rowHeight + spacing);
            paddocks[i].Width = colWidth;
            paddocks[i].Height = rowHeight;
        }
    }

    private static void ArrangeSidebar(
        List<PaddockModel> paddocks,
        double screenWidth,
        double screenHeight,
        double spacing)
    {
        double usableWidth = screenWidth - spacing * 3; // left margin, gap, right margin
        double sidebarWidth = usableWidth / 3.0;
        double mainWidth = usableWidth - sidebarWidth;
        double height = screenHeight - spacing * 2;

        if (paddocks.Count >= 1)
        {
            paddocks[0].X = spacing;
            paddocks[0].Y = spacing;
            paddocks[0].Width = sidebarWidth;
            paddocks[0].Height = height;
        }

        if (paddocks.Count >= 2)
        {
            paddocks[1].X = spacing + sidebarWidth + spacing;
            paddocks[1].Y = spacing;
            paddocks[1].Width = mainWidth;
            paddocks[1].Height = height;
        }

        // Stack any remaining paddocks in the main area
        if (paddocks.Count > 2)
        {
            int extraCount = paddocks.Count - 2;
            double extraHeight = Math.Max(80, (height - spacing * (extraCount - 1)) / extraCount);

            for (int i = 2; i < paddocks.Count; i++)
            {
                int idx = i - 2;
                paddocks[i].X = spacing + sidebarWidth + spacing;
                paddocks[i].Y = spacing + idx * (extraHeight + spacing);
                paddocks[i].Width = mainWidth;
                paddocks[i].Height = extraHeight;
            }

            // Resize the second paddock to share the main area
            paddocks[1].Height = extraHeight;
        }
    }

    private static void ArrangeQuadrant(
        List<PaddockModel> paddocks,
        double screenWidth,
        double screenHeight,
        double spacing)
    {
        double colWidth = Math.Max(100, (screenWidth - spacing * 3) / 2.0);
        double rowHeight = Math.Max(80, (screenHeight - spacing * 3) / 2.0);

        // Positions: top-left, top-right, bottom-left, bottom-right
        var positions = new[]
        {
            (X: spacing, Y: spacing),
            (X: spacing + colWidth + spacing, Y: spacing),
            (X: spacing, Y: spacing + rowHeight + spacing),
            (X: spacing + colWidth + spacing, Y: spacing + rowHeight + spacing),
        };

        for (int i = 0; i < paddocks.Count && i < 4; i++)
        {
            paddocks[i].X = positions[i].X;
            paddocks[i].Y = positions[i].Y;
            paddocks[i].Width = colWidth;
            paddocks[i].Height = rowHeight;
        }
    }
}
