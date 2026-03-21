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
    {
        var x = paddock.X;
        var y = paddock.Y;

        // Snap to screen edges (respecting spacing gap)
        if (Math.Abs(x - spacingGap) < SnapDistance) x = spacingGap;
        if (Math.Abs(y - spacingGap) < SnapDistance) y = spacingGap;
        if (Math.Abs(x + paddock.Width - (screenWidth - spacingGap)) < SnapDistance)
            x = screenWidth - paddock.Width - spacingGap;
        if (Math.Abs(y + paddock.Height - (screenHeight - spacingGap)) < SnapDistance)
            y = screenHeight - paddock.Height - spacingGap;

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
        foreach (var paddock in paddocks)
        {
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

            // Maintain spacing from other paddocks
            foreach (var other in paddocks)
            {
                if (other.Id == paddock.Id) continue;

                // Check if paddocks overlap or are too close horizontally
                bool verticalOverlap = paddock.Y < other.Y + other.Height &&
                                       paddock.Y + paddock.Height > other.Y;
                bool horizontalOverlap = paddock.X < other.X + other.Width &&
                                         paddock.X + paddock.Width > other.X;

                if (verticalOverlap)
                {
                    double gapRight = other.X - (paddock.X + paddock.Width);
                    double gapLeft = paddock.X - (other.X + other.Width);

                    if (gapRight >= 0 && gapRight < spacing)
                    {
                        // Paddock is to the left of other but too close
                        paddock.X = other.X - paddock.Width - spacing;
                    }
                    else if (gapLeft >= 0 && gapLeft < spacing)
                    {
                        // Paddock is to the right of other but too close
                        paddock.X = other.X + other.Width + spacing;
                    }
                }

                if (horizontalOverlap)
                {
                    double gapBelow = other.Y - (paddock.Y + paddock.Height);
                    double gapAbove = paddock.Y - (other.Y + other.Height);

                    if (gapBelow >= 0 && gapBelow < spacing)
                    {
                        // Paddock is above other but too close
                        paddock.Y = other.Y - paddock.Height - spacing;
                    }
                    else if (gapAbove >= 0 && gapAbove < spacing)
                    {
                        // Paddock is below other but too close
                        paddock.Y = other.Y + other.Height + spacing;
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
        double colWidth = (screenWidth - totalSpacing) / columns;
        double rowHeight = screenHeight - spacing * 2;

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
            double extraHeight = (height - spacing * (extraCount - 1)) / extraCount;

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
        double colWidth = (screenWidth - spacing * 3) / 2.0;
        double rowHeight = (screenHeight - spacing * 3) / 2.0;

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
