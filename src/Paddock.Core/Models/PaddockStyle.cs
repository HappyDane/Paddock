namespace Paddock.Core.Models;

public class PaddockStyle
{
    /// <summary>The default fill: a dark graphite that suits any wallpaper.</summary>
    public const string DefaultBackgroundColor = "#1C1C1E";

    public string BackgroundColor { get; set; } = DefaultBackgroundColor;
    public double Opacity { get; set; } = 0.88;
    public double CornerRadius { get; set; } = 16;
    public string TitleColor { get; set; } = "#98989D";

    /// <summary>
    /// Hairline edge that separates the paddock from the wallpaper. Paddocks
    /// cannot cast a shadow (each one is exactly its own window, so a shadow
    /// would be clipped), so a faint border does the same job.
    /// </summary>
    public string BorderColor { get; set; } = "#26FFFFFF";

    public double BorderThickness { get; set; } = 1;

    /// <summary>How big the icons in this paddock are drawn.</summary>
    public IconSize IconSize { get; set; } = IconSize.Medium;

    /// <summary>Typeface for the title and icon labels.</summary>
    public PaddockFont Font { get; set; } = PaddockFont.System;

    /// <summary>
    /// Frosted glass: the wallpaper behind the paddock shows through blurred,
    /// under <see cref="BackgroundColor"/> at <see cref="Opacity"/> (which then
    /// works as the strength of the tint over the glass).
    /// </summary>
    public bool FrostedGlass { get; set; }

    /// <summary>
    /// Colour every icon in the paddock takes on, as <c>#RRGGBB</c>, or
    /// <c>null</c> to draw icons as they are. Each icon keeps its own light and
    /// shade; only the hue changes (see <see cref="Services.IconTint"/>).
    /// </summary>
    public string? IconTint { get; set; }

    /// <summary>How far icons move toward <see cref="IconTint"/>: 0 not at all, 1 completely.</summary>
    public double IconTintStrength { get; set; } = DefaultIconTintStrength;

    public const double DefaultIconTintStrength = 0.8;
}

public enum IconSize
{
    Small,
    Medium,
    Large,
}

/// <summary>A short, curated list of typefaces every Windows 10/11 machine has.</summary>
public enum PaddockFont
{
    /// <summary>Segoe UI Variable / Segoe UI — the Windows system font.</summary>
    System,

    /// <summary>Bahnschrift — narrow and tidy, fits longer names.</summary>
    Compact,

    /// <summary>Georgia — a calm serif.</summary>
    Serif,

    /// <summary>Cascadia Mono / Consolas — fixed width.</summary>
    Mono,
}
